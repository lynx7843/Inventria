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
    private readonly PendingTwoFactorLogins _pending;
    private readonly TotpSecretProtector _protector;
    private static readonly string AbsentUserPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("no account has this password");
    public AuthController(
        InventriaDbContext context,
        IConfiguration configuration,
        LoginThrottle throttle,
        PendingTwoFactorLogins pending,
        TotpSecretProtector protector)
    {
        _context = context;
        _configuration = configuration;
        _throttle = throttle;
        _pending = pending;
        _protector = protector;
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

        // Deliberately before the second factor, not after. The count exists to
        // stop passwords being guessed, and the password has just been guessed
        // correctly - leaving the failures on the account would let anyone who
        // knows a password lock its owner out by stopping at the prompt.
        _throttle.RecordSuccess(request.Username);

        // The password was right, which is now only half of it. Nothing that
        // resembles a session is issued here: the ticket is an opaque key into
        // a five-minute server-side note and reaches no other endpoint. See
        // PendingTwoFactorLogins.
        if (user.TotpEnabledAt != null)
        {
            return Ok(new
            {
                message = "Enter the code from your authenticator app.",
                twoFactorRequired = true,
                ticket = _pending.Issue(user.Id)
            });
        }

        return SignInAs(user);
    }

    // The second step, for an account with an authenticator app enrolled. It
    // takes the ticket from step one rather than the username and password
    // again: re-checking the password here would mean holding it in the
    // browser between the two requests for no gain, since the ticket is already
    // proof it was checked.
    [EnableRateLimiting(LoginThrottle.RateLimitPolicy)]
    [HttpPost("login/2fa")]
    public IActionResult LoginTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        // Redeemed whatever happens next - one ticket buys one attempt at the
        // code, so guessing at six digits costs a password check each time.
        var userId = _pending.Redeem(request.Ticket);
        if (userId == null)
        {
            return Unauthorized(new { message = "This sign-in expired. Enter your username and password again." });
        }

        var user = _context.Users.Find(userId.Value);
        if (user?.TotpEnabledAt == null)
        {
            // The account was found and its second factor turned off between
            // the two requests, or deleted outright. Either way there is
            // nothing to finish here.
            return Unauthorized(new { message = "This sign-in expired. Enter your username and password again." });
        }

        var secret = _protector.Unprotect(user.TotpSecret, user.Id);

        if (secret != null && TwoFactor.VerifyCode(secret, request.Code, out var step))
        {
            // Same digits, same 30-second step: already spent. A code stays
            // valid for a minute and a half across the drift window, and
            // without this that is a minute and a half in which one shoulder
            // surfer's reading of it still works.
            if (user.TotpLastUsedStep == step)
            {
                _throttle.RecordFailure(user.Username);
                return Unauthorized(new { message = "That code has already been used. Wait for the next one." });
            }

            user.TotpLastUsedStep = step;
            _context.SaveChanges();

            return SignInAs(user);
        }

        // Not a valid code - so try it as a recovery code, which is what
        // someone whose phone is gone will be typing. The lookup is by hash, so
        // the code itself is never compared against anything stored.
        var hash = TwoFactor.HashRecoveryCode(request.Code);
        var recovery = _context.RecoveryCodes
            .FirstOrDefault(c => c.UserId == user.Id && c.UsedAt == null && c.CodeHash == hash);

        if (recovery != null)
        {
            recovery.UsedAt = DateTime.UtcNow;
            _context.SaveChanges();

            var remaining = _context.RecoveryCodes.Count(c => c.UserId == user.Id && c.UsedAt == null);

            return SignInAs(user, remaining);
        }

        // The password was already right, so this is the guessing that matters
        // now and it spends from the same budget.
        _throttle.RecordFailure(user.Username);

        return Unauthorized(new { message = "That code is not valid. Try the next one, or use a recovery code." });
    }

    // Issues the session cookie. Reached only once every factor the account has
    // turned on has actually been checked.
    private IActionResult SignInAs(User user, int? recoveryCodesRemaining = null)
    {
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
            username = user.Username,
            // Present only on a sign-in that just spent one. Somebody using
            // recovery codes has lost their phone and needs to know they are
            // working through a finite pile.
            usedRecoveryCode = recoveryCodesRemaining != null,
            recoveryCodesRemaining
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

public class TwoFactorLoginRequest
{
    [NotBlank(ErrorMessage = "This sign-in expired. Enter your username and password again.")]
    public string Ticket { get; set; } = string.Empty;

    // One field for both kinds of code. A person who has lost their phone
    // should not have to find a different box to type into, and the two are
    // told apart by what they are rather than by where they were entered.
    // The cap is the longest a grouped recovery code can be.
    [NotBlank(ErrorMessage = "Enter the code from your authenticator app, or a recovery code.")]
    [StringLength(32, ErrorMessage = "That is not a code this system issues.")]
    public string Code { get; set; } = string.Empty;
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