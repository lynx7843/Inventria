using Microsoft.AspNetCore.Http;

namespace Inventria;

/// <summary>
/// The session cookie that carries the JWT. It is HttpOnly, so page scripts -
/// including anything injected through an XSS - cannot read the token or copy
/// it back out to an attacker.
/// </summary>
public static class AuthCookie
{
    public const string Name = "inventria_session";

    /// <summary>
    /// Builds the cookie policy for the current environment. The defaults suit a
    /// frontend served from the same site as this API. A cross-site deployment
    /// needs Auth:CookieSameSite=None, which browsers only honour on a Secure
    /// cookie over HTTPS.
    /// </summary>
    /// <param name="expires">Expiry to stamp on the cookie, or null when clearing it.</param>
    public static CookieOptions Build(IConfiguration configuration, DateTimeOffset? expires = null)
    {
        var configured = configuration["Auth:CookieSameSite"];
        var sameSite = Enum.TryParse<SameSiteMode>(configured, ignoreCase: true, out var parsed)
            ? parsed
            : SameSiteMode.Lax;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = configuration.GetValue("Auth:CookieSecure", true),
            SameSite = sameSite,
            Path = "/",
            Expires = expires
        };
    }
}
