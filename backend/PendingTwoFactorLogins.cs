using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Inventria;

/// <summary>
/// Holds a sign-in between "the password was right" and "the six digits were
/// right too".
///
/// The two steps are separate requests, so something has to remember the first
/// one. That something must not be a session: whatever the browser gets after
/// step one has to be worthless for reaching any other endpoint, or the second
/// factor is a formality anyone can skip by ignoring the prompt. So it is not a
/// JWT - a token signed with the same key as a real session, handed to someone
/// who has not finished authenticating, is one forgotten claim check away from
/// being a session. It is an opaque random string that means nothing except as a
/// key into this table, on this server, for the next few minutes.
///
/// Per process and in memory, exactly like LoginThrottle and with the same
/// honest limit: a restart makes anyone mid-sign-in start over, and behind a
/// load balancer the second request has to reach the instance that served the
/// first. Both are a retry of a sign-in rather than a lost session, which is
/// cheap enough not to justify a table and a write on every login.
/// </summary>
public sealed class PendingTwoFactorLogins
{
    /// <summary>
    /// Long enough to unlock a phone and find the right entry, short enough
    /// that a ticket left on a warehouse terminal's screen is dead before
    /// anyone wanders past it.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;

    public PendingTwoFactorLogins(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Starts a pending sign-in for <paramref name="userId"/> and returns the
    /// ticket that finishes it.
    /// </summary>
    public string Issue(int userId)
    {
        // 32 bytes from a cryptographic RNG: this is guessable-into-an-account
        // if it is guessable at all, so it gets the same treatment as a session
        // token rather than a GUID.
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        _cache.Set(KeyFor(ticket), userId, Lifetime);

        return ticket;
    }

    /// <summary>
    /// The account a ticket belongs to, consuming it, or null when the ticket
    /// is unknown or expired.
    /// </summary>
    /// <remarks>
    /// Consumed on the way out whether or not the code that follows turns out
    /// to be right. A ticket that survived a wrong code would be a budget of
    /// unlimited guesses at six digits bought with one password check; making
    /// each attempt cost a fresh password means LoginThrottle's counter is what
    /// bounds the guessing, which is the whole point of it.
    /// </remarks>
    public int? Redeem(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return null;

        var key = KeyFor(ticket);
        if (!_cache.TryGetValue(key, out int userId)) return null;

        _cache.Remove(key);

        return userId;
    }

    private static string KeyFor(string ticket) => $"pending-2fa:{ticket}";
}
