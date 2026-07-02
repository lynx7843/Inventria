using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using BCrypt.Net;

namespace Inventria.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly InventriaDbContext _context;

    // Inject the database context
    public AuthController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        // 1. The BCrypt Magic: Hash the incoming plain-text password
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        // 2. Create the user model with the new secure hash
        var newUser = new User
        {
            Username = request.Username,
            Password = passwordHash, // Saving the hash, NOT the actual password
            Role = request.Role
        };

        // 3. Save to SQL Server
        _context.Users.Add(newUser);
        _context.SaveChanges();

        return Ok(new { Message = $"{request.Role} '{request.Username}' registered securely!" });
    }
}

// Data Transfer Object to define what the incoming JSON should look like
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}