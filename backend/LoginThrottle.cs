using Microsoft.Extensions.Caching.Memory;

namespace Inventria;

/// <summary>
/// Counts failed sign-in attempts per account and locks one out once there have
/// been too many in a row.
///
/// The IP rate limiter in Program.cs caps how fast any one client can call the
/// login endpoint; it does not stop a patient attacker, and it does not stop one
/// spread across many addresses. This does: the budget belongs to the account
/// being guessed at, so every attempt against it counts no matter where it came
/// from. A successful sign-in clears the count, so a warehouse user who mistypes
/// a password twice and then gets it right starts from zero again.
///
/// The counter is per process and in memory, which is the honest limit of it: a
/// restart forgives everyone, and a second instance behind a load balancer keeps
/// its own tally. Making it durable means a table and a write on every failed
/// login, and this is a brake on guessing rather than a ledger - the failures an
/// attacker is trying to hide in are exactly the ones nobody wants to persist.
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>Name of the per-IP rate limiting policy applied to the login endpoint.</summary>
    public const string RateLimitPolicy = "login";

    // Ten wrong passwords is well past a typo and well short of a guessing run.
    private const int MaxFailures = 10;

    // How long the count survives, and therefore how long a locked-out account
    // stays locked. Long enough that guessing at any useful rate is hopeless,
    // short enough that someone who genuinely forgot their password is not off
    // the floor for the afternoon - which matters because this cuts both ways:
    // anyone who knows a username can spend failures on it and lock the real
    // owner out. That is the standard trade, and the window is the price cap.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public LoginThrottle(IMemoryCache cache)
    {
        _cache = cache;
    }

    private sealed class Attempts
    {
        public int Failures;
        public DateTimeOffset ResetsAt;
    }

    /// <summary>
    /// Whether this username is currently refusing attempts, and for how long.
    ///
    /// Callers must ask this before looking the account up, and must ask it for
    /// usernames that do not exist too. A lockout that only ever applied to real
    /// accounts would answer "does this username exist?" out loud.
    /// </summary>
    public bool IsLockedOut(string username, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        if (!_cache.TryGetValue(Key(username), out Attempts? attempts) || attempts is null)
        {
            return false;
        }

        if (Volatile.Read(ref attempts.Failures) < MaxFailures)
        {
            return false;
        }

        retryAfter = attempts.ResetsAt - DateTimeOffset.UtcNow;
        return retryAfter > TimeSpan.Zero;
    }

    /// <summary>Records a rejected sign-in attempt against this username.</summary>
    public void RecordFailure(string username)
    {
        // Two simultaneous failures can both create an entry and one of the
        // increments is then lost. That costs the attacker's budget an attempt
        // it should have spent, which is not worth a lock on the login path.
        var attempts = _cache.GetOrCreate(Key(username), entry =>
        {
            // Absolute, not sliding: the window runs from the first failure, so
            // continuing to guess cannot push the reset further away.
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new Attempts { ResetsAt = DateTimeOffset.UtcNow + Window };
        })!;

        Interlocked.Increment(ref attempts.Failures);
    }

    /// <summary>Clears the count after the right password was given.</summary>
    public void RecordSuccess(string username) => _cache.Remove(Key(username));

    // SQL Server's default collation is case-insensitive, so "Admin" and "admin"
    // sign in to the same account and must therefore share one budget. Trimmed
    // for the same reason: usernames are stored trimmed.
    private static string Key(string username) => $"login-failures:{username.Trim().ToLowerInvariant()}";
}
