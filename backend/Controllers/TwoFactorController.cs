using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// Turning an authenticator app on and off for one's own account - the second
/// half of what the Settings page's security panel offers, alongside
/// UserProfileController's password change.
///
/// Its own controller rather than more methods on UserProfileController: that
/// one is about the details of an account, and every route here is a credential
/// operation that has to re-check the password first. Nothing here is an Admin
/// action - an Admin cannot enrol or disable somebody else's phone, because the
/// point of the factor is that it is held by one person.
///
/// Enrolling is three steps for a reason. A secret is generated and shown, the
/// person proves their app produced the right digits from it, and only then is
/// it switched on - so an account can never end up requiring a code from an app
/// that never successfully scanned the QR.
/// </summary>
[Authorize]
[Route("api/users/me/two-factor")]
[ApiController]
public class TwoFactorController : ControllerBase
{
    private readonly InventriaDbContext _context;
    private readonly TotpSecretProtector _protector;

    public TwoFactorController(InventriaDbContext context, TotpSecretProtector protector)
    {
        _context = context;
        _protector = protector;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private User? CurrentUser() => _context.Users
        .Include(u => u.RecoveryCodes)
        .FirstOrDefault(u => u.Id == CurrentUserId);

    private int UnusedRecoveryCodes(User user) => user.RecoveryCodes.Count(c => c.UsedAt == null);

    private object StatusFor(User user) => new
    {
        Enabled = user.TotpEnabledAt != null,
        EnabledAt = user.TotpEnabledAt,
        RecoveryCodesRemaining = UnusedRecoveryCodes(user),
        // Lets the page explain a disabled button rather than just disabling it.
        Available = _protector.IsConfigured
    };

    /// <summary>What the Settings page reads to decide what to offer.</summary>
    [HttpGet]
    public IActionResult GetStatus()
    {
        var user = CurrentUser();
        if (user == null) return NotFound(new { Message = "User not found." });

        return Ok(StatusFor(user));
    }

    /// <summary>
    /// Step one: mint a secret and hand back what the phone needs to store it.
    /// Nothing changes about how this account signs in until Confirm succeeds.
    /// </summary>
    [HttpPost("enroll")]
    public IActionResult Enroll([FromBody] ConfirmPasswordRequest request)
    {
        if (!_protector.IsConfigured)
        {
            // A secret this server cannot encrypt is one it must not store, so
            // this stops here rather than falling back to plaintext. 503 rather
            // than 400: nothing about the request is wrong, the server is not
            // set up for it.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                Message = "Two-factor authentication is not configured on this server. " +
                          "An administrator needs to set Auth:TotpEncryptionKey."
            });
        }

        var user = CurrentUser();
        if (user == null) return NotFound(new { Message = "User not found." });

        // The same check ChangePassword makes, for the same reason: whoever
        // holds the session cookie is not necessarily the account's owner, and
        // enrolling a phone is exactly how a stolen session becomes permanent.
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
        {
            return BadRequest(new { Message = "Current password is incorrect." });
        }

        if (user.TotpEnabledAt != null)
        {
            return Conflict(new { Message = "Two-factor authentication is already on for this account. Turn it off first to enrol a different device." });
        }

        var secret = TwoFactor.NewSecret();
        user.TotpPendingSecret = _protector.Protect(secret, user.Id);
        _context.SaveChanges();

        // The only time the secret leaves the server. It goes to the person who
        // just proved they know the account's password, over the same TLS
        // connection as everything else, and the page renders it to a QR code
        // locally rather than sending it anywhere to be drawn.
        return Ok(new
        {
            Message = "Scan this with your authenticator app, then enter the code it shows.",
            Secret = TwoFactor.ToBase32(secret),
            OtpAuthUri = TwoFactor.OtpAuthUri(user.Username, secret)
        });
    }

    /// <summary>
    /// Step two: the six digits proving the app really did take the secret.
    /// Switches the factor on and issues the recovery codes.
    /// </summary>
    [HttpPost("confirm")]
    public IActionResult Confirm([FromBody] TwoFactorCodeRequest request)
    {
        var user = CurrentUser();
        if (user == null) return NotFound(new { Message = "User not found." });

        var secret = _protector.Unprotect(user.TotpPendingSecret, user.Id);
        if (secret == null)
        {
            return BadRequest(new { Message = "Start again from the beginning - there is no enrolment in progress." });
        }

        if (!TwoFactor.VerifyCode(secret, request.Code, out var step))
        {
            return BadRequest(new { Message = "That code is not right. Check your phone's clock is correct, then try the next one." });
        }

        user.TotpSecret = user.TotpPendingSecret;
        user.TotpPendingSecret = null;
        user.TotpEnabledAt = DateTime.UtcNow;

        // The code that turned the factor on counts as used, so it cannot also
        // be the code that signs someone in a moment later.
        user.TotpLastUsedStep = step;

        var codes = ReplaceRecoveryCodes(user);

        _context.SaveChanges();

        return Ok(new
        {
            Message = "Two-factor authentication is on. Save these recovery codes somewhere safe.",
            RecoveryCodes = codes,
            Status = StatusFor(user)
        });
    }

    /// <summary>
    /// A fresh set of recovery codes, invalidating the old ones - for when the
    /// printed card is lost, or has been used down to the last one or two.
    /// </summary>
    [HttpPost("recovery-codes")]
    public IActionResult RegenerateRecoveryCodes([FromBody] ConfirmPasswordRequest request)
    {
        var user = CurrentUser();
        if (user == null) return NotFound(new { Message = "User not found." });

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
        {
            return BadRequest(new { Message = "Current password is incorrect." });
        }

        if (user.TotpEnabledAt == null)
        {
            return BadRequest(new { Message = "Two-factor authentication is not on for this account." });
        }

        var codes = ReplaceRecoveryCodes(user);
        _context.SaveChanges();

        return Ok(new
        {
            Message = "New recovery codes issued. The previous ones no longer work.",
            RecoveryCodes = codes,
            Status = StatusFor(user)
        });
    }

    /// <summary>
    /// Turns the factor off and destroys everything behind it - the secret, any
    /// half-finished enrolment, and every recovery code.
    /// </summary>
    [HttpPost("disable")]
    public IActionResult Disable([FromBody] ConfirmPasswordRequest request)
    {
        var user = CurrentUser();
        if (user == null) return NotFound(new { Message = "User not found." });

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
        {
            return BadRequest(new { Message = "Current password is incorrect." });
        }

        // Cleared rather than kept "in case it is turned back on". A secret
        // nobody is using is a secret with no one watching it, and re-enrolling
        // costs one scan of a QR code.
        user.TotpSecret = null;
        user.TotpPendingSecret = null;
        user.TotpEnabledAt = null;
        user.TotpLastUsedStep = null;
        _context.RecoveryCodes.RemoveRange(user.RecoveryCodes);
        user.RecoveryCodes.Clear();

        _context.SaveChanges();

        return Ok(new { Message = "Two-factor authentication is off.", Status = StatusFor(user) });
    }

    /// <summary>
    /// Swaps in a new set of codes, returning the plaintext for the one and only
    /// time it exists anywhere but the person's hands. Does not save - the
    /// caller does, so this composes into whatever else that request changed.
    /// </summary>
    private List<string> ReplaceRecoveryCodes(User user)
    {
        // Including the used ones: a set of codes is replaced whole, and
        // leaving spent hashes behind would only make "how many are left"
        // harder to answer.
        _context.RecoveryCodes.RemoveRange(user.RecoveryCodes);
        user.RecoveryCodes.Clear();

        var codes = TwoFactor.NewRecoveryCodes();

        foreach (var code in codes)
        {
            user.RecoveryCodes.Add(new RecoveryCode { CodeHash = TwoFactor.HashRecoveryCode(code) });
        }

        return codes;
    }
}

public class ConfirmPasswordRequest
{
    [NotBlank(ErrorMessage = "Current password is required.")]
    [StringLength(72, ErrorMessage = "Password cannot be longer than 72 characters.")]
    public string CurrentPassword { get; set; } = string.Empty;
}

public class TwoFactorCodeRequest
{
    [NotBlank(ErrorMessage = "Enter the six-digit code from your authenticator app.")]
    [StringLength(32, ErrorMessage = "That is not a code this system issues.")]
    public string Code { get; set; } = string.Empty;
}
