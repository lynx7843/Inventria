using System.Security.Cryptography;
using System.Text;
using OtpNet;

namespace Inventria;

/// <summary>
/// The arithmetic behind the authenticator app, and the recovery codes that
/// exist for when the phone holding it does not.
///
/// TOTP (RFC 6238) is what every authenticator app speaks: the server and the
/// phone share twenty random bytes, both hash them together with the current
/// 30-second time step, and both arrive at the same six digits without ever
/// exchanging anything. No SMS, no gateway, nothing per-message to pay for, and
/// nothing to deliver that can be intercepted on its way.
/// </summary>
public static class TwoFactor
{
    /// <summary>What the authenticator app shows above the digits.</summary>
    public const string Issuer = "Inventria";

    /// <summary>
    /// Twenty bytes, as RFC 4226 specifies for HMAC-SHA1 - and, more to the
    /// point, what every authenticator app expects when it scans a QR code.
    /// </summary>
    private const int SecretBytes = 20;

    /// <summary>
    /// How far either side of now a code is accepted: one 30-second step, so a
    /// phone whose clock has drifted half a minute still works. Wider than this
    /// starts accepting codes long after they left the screen.
    /// </summary>
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    public static byte[] NewSecret() => KeyGeneration.GenerateRandomKey(SecretBytes);

    /// <summary>The secret as an authenticator app writes it, for typing in by hand.</summary>
    public static string ToBase32(byte[] secret) => Base32Encoding.ToString(secret);

    /// <summary>
    /// The otpauth:// URI a QR code encodes. Rendered to an actual QR client
    /// side - see the Settings page - so the secret never passes through an
    /// image service on its way to the phone.
    /// </summary>
    public static string OtpAuthUri(string username, byte[] secret)
    {
        // The label is "Issuer:account" and the issuer is repeated as a
        // parameter: old apps read one, current ones read the other, and
        // disagreeing means the entry shows up unlabelled next to everything
        // else the person has enrolled.
        var label = Uri.EscapeDataString($"{Issuer}:{username}");

        return $"otpauth://totp/{label}" +
               $"?secret={ToBase32(secret)}" +
               $"&issuer={Uri.EscapeDataString(Issuer)}" +
               "&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>
    /// Whether <paramref name="code"/> is currently valid for
    /// <paramref name="secret"/>, and which time step it matched.
    /// </summary>
    /// <remarks>
    /// The matched step is what the caller records to stop the same code being
    /// used twice. A code is live for its own step plus the window either side,
    /// so without that a code read over someone's shoulder - or replayed off a
    /// logged request - stays good for another minute and a half.
    /// </remarks>
    public static bool VerifyCode(byte[] secret, string code, out long matchedStep)
    {
        matchedStep = 0;

        var trimmed = (code ?? string.Empty).Replace(" ", "").Trim();
        if (trimmed.Length != 6 || !trimmed.All(char.IsAsciiDigit)) return false;

        return new Totp(secret).VerifyTotp(trimmed, out matchedStep, Window);
    }

    // --- RECOVERY CODES ------------------------------------------------------

    /// <summary>
    /// Ten is enough that losing one to a mistyped attempt is not a crisis and
    /// few enough to print on a card and put in a drawer.
    /// </summary>
    public const int RecoveryCodeCount = 10;

    // No I, L, O, U, 0 or 1: these are read off a screen and typed back in,
    // often having been written down in between, and every pair left out here
    // is a pair somebody would otherwise confuse.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    // Sixteen characters of the alphabet above is a shade over 78 bits, which
    // is what lets these be stored under a fast hash - see HashRecoveryCode.
    private const int CodeLength = 16;

    private const int GroupSize = 4;

    /// <summary>
    /// A fresh set of codes, in the form they are shown to the person once and
    /// never again.
    /// </summary>
    public static List<string> NewRecoveryCodes() =>
        Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();

    private static string NewRecoveryCode()
    {
        var characters = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            // RandomNumberGenerator.GetInt32 rather than Random: this is a
            // credential, and a predictable one is not a credential.
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        // Grouped for reading back to someone over the radio, and for typing.
        // Normalize() puts it back together, so the dashes are presentation
        // only and nothing depends on them coming back.
        return string.Join('-', Enumerable
            .Range(0, CodeLength / GroupSize)
            .Select(group => new string(characters, group * GroupSize, GroupSize)));
    }

    /// <summary>
    /// A typed code reduced to what is compared: case and grouping are how it
    /// was displayed, not part of the secret.
    /// </summary>
    public static string Normalize(string? code) =>
        new((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// How a recovery code is stored.
    /// </summary>
    /// <remarks>
    /// SHA-256 rather than the BCrypt used for passwords, and deliberately. A
    /// password is whatever a person chose, so it needs a slow hash to make
    /// guessing at the likely ones expensive; a recovery code is 78 bits from a
    /// cryptographic RNG, and there is no "likely" to guess at - the cheapest
    /// attack on it is exhausting the space, which no hash speed brings within
    /// reach. Going the other way would cost more than it bought: verifying a
    /// recovery code means comparing against every unused code on the account,
    /// and ten BCrypt verifications per attempt is a lever for pushing on the
    /// server rather than a defence.
    /// </remarks>
    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
}
