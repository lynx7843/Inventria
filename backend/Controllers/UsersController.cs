using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using BCrypt.Net;

namespace Inventria.Controllers;

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
        // Prevent duplicate usernames
        if (_context.Users.Any(u => u.Username == request.Username))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        var newUser = new User
        {
            Username = request.Username,
            Password = BCrypt.Net.BCrypt.HashPassword(request.Password), // Secure hash
            Role = request.Role
        };

        _context.Users.Add(newUser);
        _context.SaveChanges();

        return Ok(new { Message = "User created successfully." });
    }

    [HttpPut("{id}")]
    public IActionResult UpdateUser(int id, [FromBody] UserRequest request)
    {
        var user = _context.Users.Find(id);
        if (user == null) return NotFound(new { Message = "User not found." });

        // Check for duplicate username on another account
        if (_context.Users.Any(u => u.Username == request.Username && u.Id != id))
        {
            return BadRequest(new { Message = "Username already exists." });
        }

        user.Username = request.Username;
        user.Role = request.Role;

        // Only update the password if the admin actually typed a new one
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        _context.SaveChanges();

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
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}