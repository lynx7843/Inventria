using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

// Self-service editing of one's own account. Deliberately its own controller
// rather than a method on UsersController: that one is [Authorize(Roles =
// Admin)] at the class level, and an Employee editing their own name and email
// is not an admin action.
[Authorize]
[Route("api/users/me")]
[ApiController]
public class UserProfileController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public UserProfileController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpPatch]
    public IActionResult UpdateMe([FromBody] UpdateMeRequest request)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = _context.Users.Find(id);
        if (user == null) return NotFound(new { Message = "User not found." });

        var username = request.Username.Trim();

        // Same duplicate check UsersController.UpdateUser makes - a username is
        // still unique account-wide even when someone is only editing their own.
        if (_context.Users.Any(u => u.Username == username && u.Id != id))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        user.Username = username;
        user.Email = request.Email?.Trim() ?? string.Empty;

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        // The JWT's Name claim still carries the old username until the next
        // login - same staleness UsersController.UpdateUser already accepts for
        // an admin renaming someone else. Returning the fresh values lets the
        // caller update what it has cached (localStorage) without waiting on that.
        return Ok(new
        {
            Message = "Profile updated successfully.",
            Username = user.Username,
            Email = user.Email
        });
    }

    // Until now the only way to change a password was an Admin overwriting it
    // from the Users screen - which means the Admin chose it, so no password on
    // the system was actually private to the person using it. This is the other
    // path: the account holder changing their own.
    [HttpPost("password")]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = _context.Users.Find(id);
        if (user == null) return NotFound(new { Message = "User not found." });

        // The one check this endpoint exists to enforce. Skipping it would let
        // whoever holds the session cookie - not necessarily the account's owner
        // - set a new password with no proof they knew the old one, turning a
        // stolen cookie into a permanent takeover instead of a session that
        // expires.
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
        {
            return BadRequest(new { Message = "Current password is incorrect." });
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        _context.SaveChanges();

        return Ok(new { Message = "Password changed successfully." });
    }
}

public class UpdateMeRequest
{
    [NotBlank(ErrorMessage = "Username is required.")]
    [StringLength(100, ErrorMessage = "Username cannot be longer than 100 characters.")]
    public string Username { get; set; } = string.Empty;

    // Optional: the account can be edited before an email is ever set.
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(255, ErrorMessage = "Email cannot be longer than 255 characters.")]
    public string? Email { get; set; }
}

public class ChangePasswordRequest
{
    [NotBlank(ErrorMessage = "Current password is required.")]
    public string CurrentPassword { get; set; } = string.Empty;

    // Same cap UserRequest.Password documents: BCrypt hashes only the first 72
    // bytes, so anything longer is a password whose tail does not actually
    // protect the account.
    [NotBlank(ErrorMessage = "New password is required.")]
    [StringLength(72, ErrorMessage = "Password cannot be longer than 72 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
