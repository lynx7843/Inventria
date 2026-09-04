using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Inventria.Models;
using BCrypt.Net;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
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
    private readonly LoginThrottle _throttle;
    private static readonly string AbsentUserPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("no account has this password");
    public AuthController(InventriaDbContext context, IConfiguration configuration, LoginThrottle throttle)
    {
        _context = context;
        _configuration = configuration;
        _throttle = throttle;
    }

    [EnableRateLimiting(LoginThrottle.RateLimitPolicy)]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (_throttle.IsLockedOut(request.Username, out var retryAfter))
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"Too many failed sign-in attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s)."
            });
        }

        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username);
        var passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, user?.Password ?? AbsentUserPasswordHash);

        if (user == null || !passwordMatches)
        {
            _throttle.RecordFailure(request.Username);

            return Unauthorized(new { message = "Invalid username or password." });
        }

        _throttle.RecordSuccess(request.Username);

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

        Response.Cookies.Append(AuthCookie.Name, jwtString, AuthCookie.Build(_configuration, expires));

        return Ok(new
        {
            message = "Login successful",
            role = user.Role,
            username = user.Username
        });
    }

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
    [NotBlank(ErrorMessage = "Username is required.")]
    [StringLength(100, ErrorMessage = "Username cannot be longer than 100 characters.")]
    public string Username { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "Password is required.")]
    [StringLength(72, ErrorMessage = "Password cannot be longer than 72 characters.")]
    public string Password { get; set; } = string.Empty;
}