using Inventria;
using Inventria.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Replaces the default console-only logger with one that also writes to a
// rolling file, so the seed warnings below (the generated/default admin and
// employee passwords) and everything else survive the terminal that printed
// them closing. Levels are hardcoded rather than read from the "Logging"
// section of appsettings.json, matching what that section already specifies
// - Serilog does not use Microsoft.Extensions.Logging's filtering pipeline,
// so once it owns logging that section stops doing anything.
builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "inventria-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

// 1. Add CORS policy
// Origins come from configuration so a deployment can point at its real
// frontend host without a code change - override with Cors:AllowedOrigins, e.g.
// Cors__AllowedOrigins__0=https://inventria.example.com
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    // Fall back to the `npm run dev` origin rather than starting with a policy
    // that rejects every browser request.
    allowedOrigins = ["http://localhost:5173"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSvelteFrontend",
        policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  // The session cookie is HttpOnly, so the browser only attaches
                  // it to cross-origin calls when credentials are allowed here.
                  .AllowCredentials();
        });
});

// 2. Add database connection
// Backs InventriaDbContext's audit trail (see its SaveChanges override),
// which reads the signed-in username off the current request. AddDbContext
// resolves the context's IHttpContextAccessor constructor parameter from
// this automatically.
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<InventriaDbContext>(options =>
    options.UseSqlServer(connectionString));

// Backs GET /health below - what Docker's healthcheck, a reverse proxy, or
// any future uptime monitor pings to ask "is this instance usable". A custom
// check rather than the framework's AddDbContextCheck: that extension lives
// in the separate Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
// package, and CanConnectAsync alone is enough to answer up/down.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// 3. Configure JWT Authentication
// The signing key is a secret and is never committed: supply it through
// user-secrets locally, or the Jwt__Key environment variable in deployment.
// Refuse to start without a usable one rather than fall back to a shared
// default that would let anyone holding it mint Admin tokens.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than the 32 bytes HMAC-SHA256 requires. " +
        "Set it out of source control, e.g.\n" +
        "  dotnet user-secrets set \"Jwt:Key\" \"$(openssl rand -base64 48)\"\n" +
        "or export Jwt__Key=... before starting the app.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            // Browsers hold the token in an HttpOnly cookie and cannot set an
            // Authorization header from it, so read it off the request instead.
            // Any header already present still wins, which keeps non-browser
            // clients (inventria.http, curl) working unchanged.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue(AuthCookie.Name, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Throttle sign-in attempts.
// The limiter below is per client address: it is what stops a script from
// hammering the login endpoint, which matters beyond guessing because verifying
// a password is deliberately expensive and an unauthenticated flood of them is a
// way to spend the server's CPU. LoginThrottle is the other half, counting
// failures per account so that guessing spread across many addresses still runs
// out of attempts; both are needed, neither replaces the other.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginThrottle>();

builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // { message: "..." } like every other error this API returns, because
        // that is the one field the frontend knows how to show.
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many sign-in attempts from this device. Please wait a few minutes and try again." },
            cancellationToken);
    };

    options.AddPolicy(LoginThrottle.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Behind a reverse proxy every request arrives from the proxy, so a
            // deployment that terminates TLS elsewhere needs forwarded headers
            // configured or this collapses into one shared bucket.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Comfortably more than a shift's worth of sign-ins from one
                // station, and far less than a guessing run needs.
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                // Rejected rather than queued: a caller past the limit should be
                // told to wait, not held on an open connection until it is let
                // through, which is its own way to exhaust the server.
                QueueLimit = 0
            }));
});

// Where this warehouse's days start and end. Resolved once here rather than
// per request: the zone cannot change while the app runs, and an id the system
// does not recognise should stop startup the way a missing Jwt:Key does instead
// of surfacing as a dashboard that is wrong by a few hours. See WarehouseClock.
builder.Services.AddSingleton(WarehouseClock.FromConfiguration(builder.Configuration));

// Shared by InventoryController's POST /receive and PurchaseOrdersController's
// line-receive endpoint - see StockReceivingService for why the concurrency
// handling around InventoryBalance.RowVersion has to live in one place.
builder.Services.AddScoped<StockReceivingService>();
builder.Services.AddScoped<StockPickingService>();

builder.Services.AddControllers();

// [ApiController] rejects a request whose DTO fails validation before the action
// runs, and by default answers with a ProblemDetails document. Every other error
// in this API is { message: "..." } and that is the one field the frontend reads,
// so a validation failure is reshaped to match - otherwise the user gets the
// form's generic fallback text instead of the sentence explaining what is wrong
// with what they typed.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var complaints = context.ModelState
            .SelectMany(entry => entry.Value?.Errors.Select(error => (entry.Key, Error: error)) ?? [])
            .ToList();

        // A body the JSON reader could not read produces two complaints, not
        // one: the specific failure naming the field it choked on, and a second
        // saying the parameter that body was meant to bind to is missing. The
        // second is the first with the useful half removed - taking whichever
        // came first answered a fractional quantity with "The request field is
        // required.", which names nothing the caller can act on. Sorting is
        // stable, so within a rank the original order still decides.
        var message = complaints
            .OrderByDescending(complaint => IsFormatFailure(complaint.Error) || complaint.Key.StartsWith('$') ? 1 : 0)
            .Select(complaint => Describe(complaint.Key, complaint.Error))
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
            ?? "The request was not valid.";

        return new BadRequestObjectResult(new { Message = message });
    };

    // A value that could not be read as the type the DTO declares, as opposed to
    // one that was read and then failed a rule someone wrote a sentence for.
    static bool IsFormatFailure(ModelError error) =>
        error.Exception is not null
        || error.ErrorMessage.StartsWith("The JSON value could not be converted", StringComparison.Ordinal);

    // Not every entry in ModelState came from an attribute someone wrote a
    // sentence for. A value the JSON reader could not turn into the declared
    // type - "2.5" units, a null where an id belongs, a NaN, which JSON cannot
    // even spell - never reaches those attributes: deserialization fails first
    // and System.Text.Json contributes its own text, "The JSON value could not
    // be converted to System.Int32. Path: $.quantity | LineNumber: 0 |
    // BytePositionInLine: 42". That is true, and it is not something to show
    // someone counting boxes. Worse, some of those entries carry an exception
    // and an empty message, which fell through to "The request was not valid."
    // and said nothing about which field was wrong. Naming the field is the
    // least this can do.
    static string Describe(string key, ModelError error)
    {
        if (!IsFormatFailure(error))
        {
            return error.ErrorMessage;
        }

        // The key for these is the JSON path, "$.quantity".
        var field = key.TrimStart('$', '.');

        return string.IsNullOrEmpty(field)
            ? "The request contained a value in the wrong format."
            : $"'{field}' was sent in the wrong format.";
    }
});

// Backs the app.MapOpenApi() endpoint below; without it the document service
// is never registered and the mapped route cannot resolve one.
builder.Services.AddOpenApi();

var app = builder.Build();

// First so it wraps every middleware below it. Logs method, path, status
// code and elapsed time only - never headers or the request body, which
// matters here specifically because POST /api/Auth/login's body is a
// plaintext password and a log file is exactly the wrong place for it.
app.UseSerilogRequestLogging();

// 4. Seed the first Admin account.
// Registration requires an existing Admin token, so a brand new database would
// otherwise have no way to create its first user.
SeedFirstAdmin(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // A browsable page over the same document MapOpenApi serves, defaulting
    // to /scalar/v1. Same guard as MapOpenApi and for the same reason - a
    // page listing every route (including Users, Admin-only) is not
    // something to expose outside Development.
    app.MapScalarApiReference();
}

// Skipped in Development: the `https` profile listens on both :7149 and :5240,
// so this middleware answers browser calls to the HTTP endpoint with a 307 to a
// different origin. A cross-origin redirect is fatal to a preflighted,
// credentialed fetch, and the `http` profile only escapes it because it has no
// HTTPS port for the middleware to redirect to. Outside Development there is a
// single public origin and no such cross-origin hop, so keep enforcing HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Serves the photos AvatarStorage writes, at the same root-relative path it
// hands back as AvatarUrl. Not the default app.UseStaticFiles(): that overload
// serves from IWebHostEnvironment.WebRootFileProvider, which the host fixes to
// a NullFileProvider at startup if wwwroot did not exist at that moment - true
// of a fresh checkout, which has no wwwroot until the first photo is uploaded.
// Building the provider here, after creating the directory, means static files
// work on a first run without requiring a restart.
Directory.CreateDirectory(AvatarStorage.DirectoryOn(app.Environment));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(AvatarStorage.WebRootOf(app.Environment))
});

app.UseCors("AllowSvelteFrontend");

// After UseCors so a rejected request still comes back with the headers the
// browser needs to let the page read it - a 429 the frontend cannot see is a
// login form that looks broken. CORS also answers preflights before this point,
// so an OPTIONS request never spends a caller's login budget.
app.UseRateLimiter();

// 5. Enable Authentication & Authorization (Must be in this exact order)
app.UseAuthentication();
app.UseAuthorization();

// Deliberately unauthenticated and outside MapControllers' conventions - a
// health check a monitor can't reach without first getting a token isn't one
// it can use. The default response writer (no custom one configured) only
// ever prints the overall status ("Healthy"/"Unhealthy"), never the
// exception or connection string a failing AddDbContextCheck would otherwise
// have access to.
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();

static void SeedFirstAdmin(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<InventriaDbContext>();

    // Don't take the app down at startup just because SQL Server isn't up yet
    // or migrations haven't been applied. The comment said that before this
    // method could actually do it: CanConnect answers "the server replied and
    // the database is there", which is true of a database created but never
    // migrated, and the next line then asked a table that does not exist for its
    // rows. That threw out of Main, so a fresh database - the exact case the
    // seed exists for - stopped the app from starting at all.
    try
    {
        if (!db.Database.CanConnect())
        {
            logger.LogWarning("Skipping admin seed: cannot connect to the database.");
            return;
        }

        // The tables are what the seed needs, and only migrations create them.
        // Applying them here instead would mean every start of the app rewriting
        // the schema it finds, which is a decision about deployments and not one
        // to make quietly inside a seeding helper.
        var pending = db.Database.GetPendingMigrations().ToList();

        if (pending.Count > 0)
        {
            logger.LogWarning(
                "Skipping admin seed: {Count} migration(s) have not been applied, so the tables it needs do not exist yet. " +
                "Run 'dotnet ef database update' and restart. Pending: {Pending}",
                pending.Count, string.Join(", ", pending));
            return;
        }

        if (db.Users.Any()) return;

        SeedAdminUser(app, db, logger);
        SeedEmployeeUser(app, db, logger);
    }
    catch (Exception ex)
    {
        // Anything else the database does on the way up - a login that lacks
        // rights, a timeout, a half-applied schema - is worth a line in the log
        // and is not worth refusing to start over. Without an account the API
        // answers 401 and says so, which is a better failure than a process that
        // exits before it can explain itself.
        logger.LogWarning(ex, "Skipping admin seed: the database could not be prepared.");
    }
}

// Writing the account itself, once the checks above have established there is a
// schema to write it into and nobody to sign in as.
//
// Falls back to a fixed demo password ("password") rather than a randomly
// generated one when nothing is configured - this app is meant to be usable
// out of the box without hunting through startup logs for a one-time secret,
// which matters more here than the login being guessable. Anyone actually
// deploying this sets Seed:AdminPassword (or Seed__AdminPassword) and gets a
// real one instead.
static void SeedAdminUser(WebApplication app, InventriaDbContext db, Microsoft.Extensions.Logging.ILogger logger)
{
    var username = app.Configuration["Seed:AdminUsername"] ?? "admin";
    var password = app.Configuration["Seed:AdminPassword"];
    var usingDefault = string.IsNullOrWhiteSpace(password);

    if (usingDefault) password = "password";

    db.Users.Add(new User
    {
        Username = username,
        Password = BCrypt.Net.BCrypt.HashPassword(password),
        Role = UserRoles.Admin
    });
    db.SaveChanges();

    if (usingDefault)
    {
        logger.LogWarning(
            "Seeded first Admin '{Username}' with the default demo password 'password'. " +
            "Change it, or set Seed:AdminPassword before this matters.",
            username);
    }
    else
    {
        logger.LogInformation("Seeded first Admin '{Username}' from configuration.", username);
    }
}

// Same idea as SeedAdminUser, for an Employee account to sign in and try the
// non-Admin side of the app with - a fresh database otherwise has no way to
// see the Employee dashboard without an Admin first creating one by hand.
static void SeedEmployeeUser(WebApplication app, InventriaDbContext db, Microsoft.Extensions.Logging.ILogger logger)
{
    var username = app.Configuration["Seed:EmployeeUsername"] ?? "employee";
    var password = app.Configuration["Seed:EmployeePassword"];
    var usingDefault = string.IsNullOrWhiteSpace(password);

    if (usingDefault) password = "password";

    db.Users.Add(new User
    {
        Username = username,
        Password = BCrypt.Net.BCrypt.HashPassword(password),
        Role = UserRoles.Employee
    });
    db.SaveChanges();

    if (usingDefault)
    {
        logger.LogWarning(
            "Seeded first Employee '{Username}' with the default demo password 'password'. " +
            "Change it, or set Seed:EmployeePassword before this matters.",
            username);
    }
    else
    {
        logger.LogInformation("Seeded first Employee '{Username}' from configuration.", username);
    }
}

// Backs GET /health. Reports only Healthy/Unhealthy - never the exception -
// which matters because CanConnectAsync's failure mode can be a timeout that
// carries the connection string in its message, and this endpoint takes no
// credentials.
sealed class DatabaseHealthCheck(InventriaDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}