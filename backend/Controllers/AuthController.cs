using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using BCrypt.Net;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Inventria.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly InventriaDbContext _context;
    private readonly IConfiguration _configuration;

    // Inject IConfiguration to access the secret key from appsettings.json
    public AuthController(InventriaDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        // ... (Keep existing Register code) ...
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var newUser = new User { Username = request.Username, Password = passwordHash, Role = request.Role };
        _context.Users.Add(newUser);
        _context.SaveChanges();
        return Ok(new { Message = $"{request.Role} '{request.Username}' registered securely!" });
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username);
        
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        // --- NEW: Generate JWT Token ---
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username), // Using system ID instead of personal names
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.AddHours(8), // Token expires in 8 hours (Standard shift length)
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        return Ok(new 
        { 
            message = "Login successful", 
            token = jwtString, // Send the token to the frontend
            role = user.Role,
            username = user.Username 
        });
    }
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}