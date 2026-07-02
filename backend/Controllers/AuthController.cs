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

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // 1. Find the user in the database by username
        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username);

        // 2. If user doesn't exist, return a 401 Unauthorized error
        if (user == null)
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        // 3. The BCrypt Magic: Verify the typed password against the saved hash
        bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.Password);

        if (!isPasswordValid)
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        // 4. If valid, return a success response with the user's role
        // (In a full production app, you would generate a JWT token here)
        return Ok(new
        {
            message = "Login successful",
            role = user.Role,
            username = user.Username
        });
    }
}

// Data Transfer Object to define what the incoming JSON should look like
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

// Add this at the very bottom of the file next to RegisterRequest
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}