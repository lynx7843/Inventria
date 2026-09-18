namespace Inventria.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Will store "Admin" or "Employee"
    public string Email { get; set; } = string.Empty;

    // The Settings page's two notification toggles. Defaulted true/false to
    // match what the page already showed for everyone before these existed -
    // a migration that changed what an existing account is subscribed to would
    // be a surprise, not a preference.
    public bool NotifyLowStock { get; set; } = true;
    public bool NotifyDailySummary { get; set; } = false;

    // Relative to wwwroot, e.g. "uploads/avatars/3f2b....jpg" - never a
    // client-supplied filename or an absolute path. See AvatarStorage.
    public string? AvatarPath { get; set; }
}