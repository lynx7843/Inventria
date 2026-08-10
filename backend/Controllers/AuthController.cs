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

    // A BCrypt hash of nothing in particular, verified against when the username
    // does not exist. Skipping the verify in that case made the answer come back
    // in about a millisecond instead of the ~100 the hash costs, so anyone with a
    // stopwatch could sort real usernames from invented ones without ever
    // guessing a password - which is the first half of guessing a password.
    // Hashed once at startup at the same work factor HashPassword uses for real
    // accounts, so the two paths cost the same.
    private static readonly string AbsentUserPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("no account has this password");

    // Inject IConfiguration to access the secret key from appsettings.json
    public AuthController(InventriaDbContext context, IConfiguration configuration, LoginThrottle throttle)
    {
        _context = context;
        _configuration = configuration;
        _throttle = throttle;
    }

    // NOTE: Account creation lives solely at POST /api/users
    // (UsersController.CreateUser, [Authorize(Roles = "Admin")]).
    // A public register endpoint let any caller pick their own Role and mint
    // themselves an Admin account, bypassing every role check in the API.
    // The first Admin is seeded from configuration at startup - see Program.cs.

    // Two limits guard this endpoint, because they stop different attacks. The
    // policy here caps how fast one address can call it at all, which is what
    // keeps a flood from spending the server's CPU on BCrypt; the throttle below
    // caps how many times any account can be guessed at, from anywhere.
    [EnableRateLimiting(LoginThrottle.RateLimitPolicy)]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Checked before the account is looked up, so a locked-out username costs
        // nothing to reject - and so the answer cannot depend on whether the
        // username is real.
        if (_throttle.IsLockedOut(request.Username, out var retryAfter))
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"Too many failed sign-in attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s)."
            });
        }

        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username);

        // Verified in every case, including the one where there is nothing to
        // verify against: `user == null || !Verify(...)` short-circuits, and that
        // short circuit is the timing side channel. The result is deliberately
        // computed before it is combined with the existence check.
        var passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, user?.Password ?? AbsentUserPasswordHash);

        if (user == null || !passwordMatches)
        {
            // Counted for usernames that do not exist too, so that a 429 says
            // "this username has been guessed at a lot" and never "this username
            // is real".
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

// Neither field is a credential check - the password is verified against the
// stored hash below, not by these attributes. They are here so that a request
// with nothing in it is answered as a malformed request instead of reaching
// BCrypt, which throws on a null password and would return that as a 500.
public class LoginRequest
{
    [NotBlank(ErrorMessage = "Username is required.")]
    [StringLength(100, ErrorMessage = "Username cannot be longer than 100 characters.")]
    public string Username { get; set; } = string.Empty;

    // Same 72 as UserRequest, and for the same reason: BCrypt hashes the first
    // 72 bytes and ignores the rest, so no account can have a password longer
    // than this to be checked against.
    [NotBlank(ErrorMessage = "Password is required.")]
    [StringLength(72, ErrorMessage = "Password cannot be longer than 72 characters.")]
    public string Password { get; set; } = string.Empty;
}