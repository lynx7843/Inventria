using Inventria;
using Inventria.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

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
        var message = context.ModelState.Values
            .SelectMany(state => state.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
            ?? "The request was not valid.";

        return new BadRequestObjectResult(new { Message = message });
    };
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

// 4. Enable Authentication & Authorization (Must be in this exact order)
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
    // or migrations haven't been applied.
    if (!db.Database.CanConnect())
    {
        logger.LogWarning("Skipping admin seed: cannot connect to the database.");
        return;
    }

    if (db.Users.Any()) return;

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