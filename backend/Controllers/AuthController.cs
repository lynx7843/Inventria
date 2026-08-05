using Microsoft.AspNetCore.Authorization;
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

    // NOTE: Account creation lives solely at POST /api/users
    // (UsersController.CreateUser, [Authorize(Roles = "Admin")]).
    // A public register endpoint let any caller pick their own Role and mint
    // themselves an Admin account, bypassing every role check in the API.
    // The first Admin is seeded from configuration at startup - see Program.cs.

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username);
        
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        // --- Generate JWT Token ---
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
        var expires = DateTime.UtcNow.AddHours(8); // Standard shift length

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username), // Using system ID instead of personal names
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        // The token goes back as an HttpOnly cookie and is deliberately kept out
        // of the response body: script on the page - including injected script -
        // must never be able to read it. Role and username are returned because
        // the UI routes on them, and neither is a credential.
        Response.Cookies.Append(AuthCookie.Name, jwtString, AuthCookie.Build(_configuration, expires));

        return Ok(new
        {
            message = "Login successful",
            role = user.Role,
            username = user.Username
        });
    }

    // Clearing an HttpOnly cookie has to happen server-side - the browser cannot
    // delete it from script. Anonymous so an already-expired session can still
    // clean up after itself.
    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthCookie.Name, AuthCookie.Build(_configuration));
        return Ok(new { message = "Logged out" });
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}