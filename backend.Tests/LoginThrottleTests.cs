using Microsoft.Extensions.Caching.Memory;

namespace Inventria.Tests;

/// <summary>
/// The per-account brake on password guessing. The IP rate limiter caps how fast
/// one client can call the endpoint; this is what runs an attacker out of
/// attempts no matter how many addresses they spread across.
/// </summary>
public class LoginThrottleTests
{
    private static LoginThrottle NewThrottle() => new(new MemoryCache(new MemoryCacheOptions()));

    private static void Fail(LoginThrottle throttle, string username, int times)
    {
        for (var i = 0; i < times; i++) throttle.RecordFailure(username);
    }

    [Fact]
    public void An_account_nobody_has_guessed_at_is_not_locked_out()
    {
        Assert.False(NewThrottle().IsLockedOut("alice", out _));
    }

    [Fact]
    public void Nine_wrong_passwords_are_a_bad_morning_not_an_attack()
    {
        var throttle = NewThrottle();
        Fail(throttle, "alice", 9);

        Assert.False(throttle.IsLockedOut("alice", out _));
    }

    [Fact]
    public void The_tenth_failure_locks_the_account()
    {
        var throttle = NewThrottle();
        Fail(throttle, "alice", 10);

        Assert.True(throttle.IsLockedOut("alice", out var retryAfter));
        Assert.InRange(retryAfter.TotalMinutes, 14, 15);
    }

    [Fact]
    public void Locking_one_account_leaves_every_other_account_alone()
    {
        var throttle = NewThrottle();
        Fail(throttle, "alice", 10);

        Assert.False(throttle.IsLockedOut("bob", out _));
    }

    [Fact]
    public void Signing_in_successfully_clears_the_count()
    {
        var throttle = NewThrottle();
        Fail(throttle, "alice", 10);

        throttle.RecordSuccess("alice");

        // Someone who mistypes their password and then gets it right starts from
        // zero again.
        Assert.False(throttle.IsLockedOut("alice", out _));
    }

    [Theory]
    [InlineData("ALICE")]
    [InlineData("  alice  ")]
    public void Case_and_spacing_share_one_budget(string variant)
    {
        var throttle = NewThrottle();
        Fail(throttle, "alice", 10);

        // SQL Server's default collation is case-insensitive, so these all sign
        // in to the same account and must therefore be guessed at from the same
        // allowance.
        Assert.True(throttle.IsLockedOut(variant, out _));
    }

    [Fact]
    public void A_username_that_does_not_exist_is_counted_the_same_way()
    {
        var throttle = NewThrottle();
        Fail(throttle, "not-a-real-account", 10);

        // If only real accounts could lock out, the 429 itself would answer
        // "does this username exist?" out loud.
        Assert.True(throttle.IsLockedOut("not-a-real-account", out _));
    }

    [Fact]
    public void Failures_recorded_at_the_same_moment_still_reach_the_limit()
    {
        var throttle = NewThrottle();

        Parallel.For(0, 10, _ => throttle.RecordFailure("alice"));

        Assert.True(throttle.IsLockedOut("alice", out _));
    }
}
