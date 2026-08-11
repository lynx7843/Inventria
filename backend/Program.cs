using Inventria;
using Inventria.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<InventriaDbContext>(options =>
    options.UseSqlServer(connectionString));

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

// 4. Seed the first Admin account.
// Registration requires an existing Admin token, so a brand new database would
// otherwise have no way to create its first user.
SeedFirstAdmin(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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

app.UseCors("AllowSvelteFrontend");

// After UseCors so a rejected request still comes back with the headers the
// browser needs to let the page read it - a 429 the frontend cannot see is a
// login form that looks broken. CORS also answers preflights before this point,
// so an OPTIONS request never spends a caller's login budget.
app.UseRateLimiter();

// 5. Enable Authentication & Authorization (Must be in this exact order)
app.UseAuthentication();
app.UseAuthorization();

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
static void SeedAdminUser(WebApplication app, InventriaDbContext db, ILogger logger)
{
    var username = app.Configuration["Seed:AdminUsername"] ?? "admin";
    var password = app.Configuration["Seed:AdminPassword"];
    var generated = false;

    if (string.IsNullOrWhiteSpace(password))
    {
        // No credential in config, so mint one rather than shipping a default.
        // It is printed once, here, and never stored in plaintext.
        password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        generated = true;
    }

    db.Users.Add(new User
    {
        Username = username,
        Password = BCrypt.Net.BCrypt.HashPassword(password),
        Role = UserRoles.Admin
    });
    db.SaveChanges();

    if (generated)
    {
        logger.LogWarning(
            "Seeded first Admin '{Username}' with a generated password: {Password}\n" +
            "This is shown only once. Sign in and change it, or set Seed:AdminPassword.",
            username, password);
    }
    else
    {
        logger.LogInformation("Seeded first Admin '{Username}' from configuration.", username);
    }
}