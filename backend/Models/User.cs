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

    // --- TWO-FACTOR ----------------------------------------------------------
    //
    // The shared secret an authenticator app derives its six digits from, always
    // encrypted - see TotpSecretProtector for why this one cannot be hashed the
    // way Password is. Null means the account has no second factor, which is
    // what every account created before this existed has.
    public string? TotpSecret { get; set; }

    // Where a secret lives between "show me the QR code" and "here are six
    // digits proving I scanned it". Separate from TotpSecret so that starting an
    // enrollment - or abandoning one halfway - never disturbs a second factor
    // that is already working.
    public string? TotpPendingSecret { get; set; }

    // When the second factor was switched on, and the flag for whether it is.
    // TotpSecret alone would do, but a date answers "since when" for free and
    // the Settings page shows it.
    public DateTime? TotpEnabledAt { get; set; }

    // The last 30-second time step this account signed in with, so the same six
    // digits cannot be used twice - see TwoFactor.VerifyCode.
    public long? TotpLastUsedStep { get; set; }

    public List<RecoveryCode> RecoveryCodes { get; set; } = [];
}