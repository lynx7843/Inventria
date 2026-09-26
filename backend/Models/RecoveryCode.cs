namespace Inventria.Models;

/// <summary>
/// One single-use way back into an account whose authenticator app is gone.
///
/// Without these, a lost or wiped phone leaves an account that nobody can sign
/// in to and nobody can repair from inside the app - not the account holder,
/// not an Admin, since the Users screen can reset a password but has no way to
/// clear a second factor. The only remaining fix is an UPDATE against the Users
/// table, which needs whoever holds the database credentials and is exactly the
/// kind of out-of-band favour that ends up being done badly.
///
/// Stored as a hash and marked used rather than deleted, so a code that has
/// already rescued the account cannot rescue it twice - see
/// TwoFactor.HashRecoveryCode for why the hash is a fast one.
/// </summary>
public class RecoveryCode
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Hex SHA-256 of the normalized code. Never the code itself.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this code was spent, or null while it is still good. Kept rather
    /// than deleted so "you have three codes left" and "one of your codes was
    /// used on Tuesday" are both answerable.
    /// </summary>
    public DateTime? UsedAt { get; set; }
}
