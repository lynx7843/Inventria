using Inventria.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSvelteFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173") // Your SvelteKit URL
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// 2. Add database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<InventriaDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Add Controller support
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline for development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// 4. Apply CORS policy (Must be before MapControllers)
app.UseCors("AllowSvelteFrontend");

// 5. Map the API controllers (like AuthController)
app.MapControllers();

app.Run();