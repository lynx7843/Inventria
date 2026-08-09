using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;

namespace Inventria.Controllers;

[Authorize(Roles = UserRoles.Admin)]
[Route("api/[controller]")]
[ApiController]
public class UsersController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public UsersController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult GetUsers()
    {
        // Select only non-sensitive data to send to the frontend
        var users = _context.Users
            .Select(u => new { u.Id, u.Username, u.Role })
            .ToList();
            
        return Ok(users);
    }

    [HttpPost]
    public IActionResult CreateUser([FromBody] UserRequest request)
    {
        // The DTO cannot require this: the same shape is used for updates, where
        // an empty password means "leave it alone". A new account has nothing to
        // leave alone, so a blank one here would be an account with a hash of ""
        // as its password.
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Message = "Password is required for a new user." });
        }

        // Surrounding spaces are invisible in the UI but not to the unique index,
        // so " admin" would sit next to "admin" as a second, confusable account.
        var username = request.Username.Trim();

        // Prevent duplicate usernames
        if (_context.Users.Any(u => u.Username == username))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        var newUser = new User
        {
            Username = username,
            Password = BCrypt.Net.BCrypt.HashPassword(request.Password), // Secure hash
            Role = request.Role
        };

        _context.Users.Add(newUser);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            // Another create claimed this username between the check above and
            // this insert. Same answer the check would have given.
            return BadRequest(new { Message = "Username already exists." });
        }

        return Ok(new { Message = "User created successfully." });
    }

    [HttpPut("{id}")]
    public IActionResult UpdateUser(int id, [FromBody] UserRequest request)
    {
        var user = _context.Users.Find(id);
        if (user == null) return NotFound(new { Message = "User not found." });

        var username = request.Username.Trim();

        // Check for duplicate username on another account
        if (_context.Users.Any(u => u.Username == username && u.Id != id))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        user.Username = username;
        user.Role = request.Role;

        // Only update the password if the admin actually typed a new one - and a
        // line of spaces is not one, it is a field they tabbed through.
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        return Ok(new { Message = "User updated successfully." });
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteUser(int id)
    {
        var user = _context.Users.Find(id);
        if (user == null) return NotFound(new { Message = "User not found." });

        _context.Users.Remove(user);
        _context.SaveChanges();

        return Ok(new { Message = "User deleted successfully." });
    }
}

// Add the DTO at the bottom
public class UserRequest
{
    // 100 characters because that is the width of the column; without the limit
    // a longer name is a truncation error from SQL Server, which reaches the
    // caller as a 500 rather than as "that is too long".
    [NotBlank(ErrorMessage = "Username is required.")]
    [StringLength(100, ErrorMessage = "Username cannot be longer than 100 characters.")]
    public string Username { get; set; } = string.Empty;

    // Not [NotBlank]: an update sends this empty to mean "keep the current
    // password", so whether a blank one is allowed depends on the action and is
    // decided in CreateUser. The cap is BCrypt's - it hashes the first 72 bytes
    // and ignores the rest, so anything longer is a password whose tail does not
    // actually protect the account.
    [StringLength(72, ErrorMessage = "Password cannot be longer than 72 characters.")]
    public string Password { get; set; } = string.Empty;

    // The whitelist. Any other string produces an account that logs in and then
    // has no screen to land on, and nothing downstream would have caught it:
    // [Authorize(Roles = ...)] simply never matches, so the account silently
    // fails every check instead of being rejected here where it can be fixed.
    [AllowedValues(UserRoles.Admin, UserRoles.Employee,
        ErrorMessage = "Role must be either Admin or Employee.")]
    public string Role { get; set; } = string.Empty;
}