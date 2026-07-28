using Inventria.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSvelteFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// 2. Add database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<InventriaDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// 4. Seed the first Admin account.
// Registration requires an existing Admin token, so a brand new database would
// otherwise have no way to create its first user.
SeedFirstAdmin(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
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
        Role = "Admin"
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