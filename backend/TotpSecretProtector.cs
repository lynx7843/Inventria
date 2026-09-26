using System.Security.Cryptography;
using System.Text;

namespace Inventria;

/// <summary>
/// Encrypts a TOTP secret on its way into the database and back out again.
///
/// A password is stored as a BCrypt hash because nothing ever needs the
/// password back - verifying one only means hashing the attempt and comparing.
/// A TOTP secret is the opposite: every sign-in has to recompute the six digits
/// from it, so the server needs the secret itself, and hashing is not available.
/// Anyone who reads the Users table with the secrets in the clear can generate
/// valid codes for every account on the system, indefinitely and undetectably -
/// which is worse than reading the password hashes, since those still have to be
/// cracked. So it is encrypted, with a key that lives in configuration rather
/// than in the database, and a database backup on its own is no longer enough.
///
/// AES-GCM rather than AES-CBC: it authenticates as well as encrypts, so a
/// tampered or truncated column fails to decrypt instead of quietly producing
/// the wrong 20 bytes and locking someone out with no explanation.
/// </summary>
public sealed class TotpSecretProtector
{
    /// <summary>Where the key is configured. See appsettings.json.</summary>
    public const string ConfigurationKey = "Auth:TotpEncryptionKey";

    // Bumped if the format below ever changes, so old rows stay readable while
    // new ones are written differently.
    private const byte Version = 1;

    private const int NonceSize = 12;  // 96 bits, what GCM is specified for
    private const int TagSize = 16;

    private readonly byte[]? _key;

    private TotpSecretProtector(byte[]? key) => _key = key;

    /// <summary>
    /// Whether this server can hold TOTP secrets at all. False when no key is
    /// configured, which is not an error on its own - it only means the
    /// two-factor endpoints refuse to enrol anyone rather than storing a secret
    /// they cannot protect.
    /// </summary>
    public bool IsConfigured => _key is not null;

    /// <summary>
    /// The protector described by <see cref="ConfigurationKey"/>, or one that
    /// refuses to encrypt when nothing is configured.
    /// </summary>
    /// <remarks>
    /// Deliberately not a hard startup failure the way a missing Jwt:Key is.
    /// Every existing deployment predates this setting, and refusing to start
    /// would take a working warehouse offline over a feature nobody there has
    /// switched on yet. Startup logs the absence instead, and the only thing
    /// that actually stops is enrolling.
    /// </remarks>
    public static TotpSecretProtector FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configured)) return new TotpSecretProtector(null);

        // The configured value is a high-entropy random string, not a password,
        // so SHA-256 is the right way to turn it into the 32 bytes AES-256
        // wants: there is nothing here for a slow KDF to protect against
        // guessing, and this keeps the setting's ergonomics identical to
        // Jwt:Key - generate it with `openssl rand -base64 48` and paste it.
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(configured));

        return new TotpSecretProtector(key);
    }

    /// <summary>
    /// Encrypts <paramref name="secret"/> for storage against
    /// <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// The user id is authenticated alongside the ciphertext rather than
    /// encrypted into it, so a row copied from one account to another fails to
    /// decrypt. Without that, anyone who could write to the Users table could
    /// point their own enrolled secret at an administrator's account and pass
    /// its second factor with their own phone.
    /// </remarks>
    public string Protect(byte[] secret, int userId)
    {
        var key = RequireKey();

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, secret, ciphertext, tag, AssociatedData(userId));

        var blob = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        blob[0] = Version;
        nonce.CopyTo(blob, 1);
        tag.CopyTo(blob, 1 + NonceSize);
        ciphertext.CopyTo(blob, 1 + NonceSize + TagSize);

        return Convert.ToBase64String(blob);
    }

    /// <summary>
    /// The secret behind a stored blob, or null when it cannot be read - a
    /// wrong or rotated key, a tampered row, or a blob written for a different
    /// account. Null rather than an exception because every caller's answer is
    /// the same either way: this account cannot complete its second factor, and
    /// needs a recovery code.
    /// </summary>
    public byte[]? Unprotect(string? stored, int userId)
    {
        if (_key is null || string.IsNullOrEmpty(stored)) return null;

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(stored);
        }
        catch (FormatException)
        {
            return null;
        }

        if (blob.Length < 1 + NonceSize + TagSize || blob[0] != Version) return null;

        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = blob.AsSpan(1 + NonceSize + TagSize);
        var secret = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, secret, AssociatedData(userId));
        }
        catch (CryptographicException)
        {
            // What GCM throws when the tag does not match: the row is not the
            // one that was written here, for this account, with this key.
            return null;
        }

        return secret;
    }

    private byte[] RequireKey() => _key ?? throw new InvalidOperationException(
        $"{ConfigurationKey} is not set, so two-factor secrets cannot be stored. " +
        "Set it out of source control, e.g.\n" +
        "  dotnet user-secrets set \"Auth:TotpEncryptionKey\" \"$(openssl rand -base64 48)\"\n" +
        "or export Auth__TotpEncryptionKey=... before starting the app.");

    private static byte[] AssociatedData(int userId) => Encoding.UTF8.GetBytes($"totp:{userId}");
}
