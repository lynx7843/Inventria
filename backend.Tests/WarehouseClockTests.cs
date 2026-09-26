using Inventria;
using Microsoft.Extensions.Configuration;

namespace Inventria.Tests;

/// <summary>
/// Where one warehouse day ends and the next begins.
///
/// Timestamps are stored in UTC and these tests never change that - every
/// expected value below is a UTC instant. What is under test is only which UTC
/// instant a given local midnight lands on, which is the whole of what
/// WarehouseClock decides.
///
/// The daylight-saving cases use a zone built here rather than a real one.
/// Chile and Lebanon really do move their clocks at midnight, which is the case
/// that matters, but pinning a test to their rules pins it to a tzdata release:
/// governments change these, and a test that starts failing because Santiago
/// moved its transition is reporting on the news rather than on this code.
/// </summary>
public class WarehouseClockTests
{
    /// <summary>A clock stopped at a chosen instant, for asking what "today" is.</summary>
    private sealed class FrozenTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static WarehouseClock ClockFor(string timeZoneId, DateTime? utcNow = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WarehouseClock.ConfigurationKey] = timeZoneId
            })
            .Build();

        return WarehouseClock.FromConfiguration(
            configuration,
            utcNow is null ? null : new FrozenTime(utcNow.Value));
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // --- CONFIGURATION -------------------------------------------------------

    [Fact]
    public void An_unconfigured_warehouse_keeps_measuring_days_in_utc()
    {
        var configuration = new ConfigurationBuilder().Build();

        var clock = WarehouseClock.FromConfiguration(configuration);

        Assert.Equal(TimeZoneInfo.Utc, clock.Zone);

        // The point of the default: every figure that used DateTime.UtcNow.Date
        // before this setting existed still gets exactly that.
        Assert.Equal(Utc(2026, 9, 26), clock.StartOfDay(new DateOnly(2026, 9, 26)));
    }

    [Fact]
    public void A_blank_timezone_is_the_same_as_none()
    {
        Assert.Equal(TimeZoneInfo.Utc, ClockFor("   ").Zone);
    }

    [Fact]
    public void An_unknown_timezone_stops_startup_rather_than_reporting_the_wrong_day()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ClockFor("Europe/Narnia"));

        // The message has to name both the setting and the value, because the
        // symptom this prevents - figures wrong by a whole number of hours -
        // gives no hint at all about where to look.
        Assert.Contains(WarehouseClock.ConfigurationKey, error.Message);
        Assert.Contains("Europe/Narnia", error.Message);
    }

    // --- FIXED OFFSETS -------------------------------------------------------

    [Fact]
    public void A_day_starts_at_local_midnight_not_utc_midnight()
    {
        var clock = ClockFor("Asia/Tokyo");

        // Tokyo is UTC+9 all year, so its 26th began at 15:00 on the 25th in UTC.
        Assert.Equal(Utc(2026, 9, 25, 15), clock.StartOfDay(new DateOnly(2026, 9, 26)));
    }

    [Fact]
    public void A_day_west_of_utc_starts_after_utc_midnight()
    {
        var clock = ClockFor("America/New_York");

        // -4 in September, daylight saving being in effect.
        Assert.Equal(Utc(2026, 9, 26, 4), clock.StartOfDay(new DateOnly(2026, 9, 26)));
    }

    [Fact]
    public void The_end_of_a_day_is_the_start_of_the_next_one()
    {
        var clock = ClockFor("Asia/Tokyo");
        var day = new DateOnly(2026, 9, 26);

        // Exclusive, so a filter uses < and cannot either miss the last instant
        // of the day or count the first instant of the next one twice.
        Assert.Equal(clock.StartOfDay(day.AddDays(1)), clock.EndOfDay(day));
        Assert.Equal(Utc(2026, 9, 26, 15), clock.EndOfDay(day));
    }

    [Fact]
    public void Today_is_the_date_it_is_in_the_warehouse()
    {
        // 22:00 UTC on the 25th is already 08:00 on the 26th in Sydney - the
        // evening shift whose work this used to file under the wrong day.
        var clock = ClockFor("Australia/Sydney", Utc(2026, 9, 25, 22));

        Assert.Equal(new DateOnly(2026, 9, 26), clock.Today());
        Assert.Equal(clock.StartOfDay(new DateOnly(2026, 9, 26)), clock.StartOfToday());
    }

    // --- DAYLIGHT SAVING -----------------------------------------------------

    /// <summary>
    /// A zone that moves its clocks at midnight, in both directions: forward an
    /// hour at 00:00 on 15 March, and back again at 01:00 daylight time on
    /// 1 November. Standard time is UTC, so every expectation below reads
    /// directly as an offset from the local wall clock.
    /// </summary>
    private static TimeZoneInfo MidnightShiftZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            dateStart: DateTime.MinValue.Date,
            dateEnd: DateTime.MaxValue.Date,
            daylightDelta: TimeSpan.FromHours(1),
            daylightTransitionStart: TimeZoneInfo.TransitionTime.CreateFixedDateRule(
                new DateTime(1, 1, 1, 0, 0, 0), 3, 15),
            daylightTransitionEnd: TimeZoneInfo.TransitionTime.CreateFixedDateRule(
                new DateTime(1, 1, 1, 1, 0, 0), 11, 1));

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/MidnightShift", TimeSpan.Zero, "Midnight Shift", "Standard", "Daylight", [rule]);
    }

    [Fact]
    public void A_day_whose_midnight_never_happened_starts_when_the_clocks_jumped()
    {
        var clock = new WarehouseClock(MidnightShiftZone());

        // 15 March goes straight from 23:59 on the 14th to 01:00. Asking for
        // the UTC instant of a local time that did not occur throws, so the
        // day has to start at the jump itself - 01:00 local, which at the new
        // +1 offset is midnight UTC.
        Assert.Equal(Utc(2026, 3, 15), clock.StartOfDay(new DateOnly(2026, 3, 15)));

        // And the day before it is still a whole day long, ending where this
        // one starts.
        Assert.Equal(Utc(2026, 3, 14), clock.StartOfDay(new DateOnly(2026, 3, 14)));
        Assert.Equal(Utc(2026, 3, 15), clock.EndOfDay(new DateOnly(2026, 3, 14)));
    }

    [Fact]
    public void A_day_whose_midnight_happened_twice_starts_at_the_first_one()
    {
        var clock = new WarehouseClock(MidnightShiftZone());

        // 1 November reaches 01:00 on daylight time and falls back to 00:00 on
        // standard, so its first hour is lived twice. The day starts at the
        // first of the two midnights - 23:00 UTC on 31 October. Resolving it
        // the other way would hand that hour to October and count it in both
        // days.
        Assert.Equal(Utc(2026, 10, 31, 23), clock.StartOfDay(new DateOnly(2026, 11, 1)));

        // Which makes 1 November a 25-hour day, as it should be.
        Assert.Equal(TimeSpan.FromHours(25),
            clock.EndOfDay(new DateOnly(2026, 11, 1)) - clock.StartOfDay(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void A_spring_forward_day_is_an_hour_short()
    {
        var clock = new WarehouseClock(MidnightShiftZone());

        Assert.Equal(TimeSpan.FromHours(23),
            clock.EndOfDay(new DateOnly(2026, 3, 15)) - clock.StartOfDay(new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public void Consecutive_days_never_overlap_or_leave_a_gap_across_a_transition()
    {
        var clock = new WarehouseClock(MidnightShiftZone());

        // The property that actually matters to a report: every instant falls
        // in exactly one day. Walked across both transitions rather than
        // asserted at them, since a boundary rule can be right on the day it
        // was written for and wrong on the day after.
        foreach (var start in new[] { new DateOnly(2026, 3, 13), new DateOnly(2026, 10, 30) })
        {
            for (var offset = 0; offset < 4; offset++)
            {
                var day = start.AddDays(offset);
                Assert.Equal(clock.StartOfDay(day.AddDays(1)), clock.EndOfDay(day));
                Assert.True(clock.EndOfDay(day) > clock.StartOfDay(day));
            }
        }
    }
}
