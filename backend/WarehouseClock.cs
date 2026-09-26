namespace Inventria;

/// <summary>
/// The warehouse's own calendar: where one day ends and the next begins.
///
/// Every timestamp in this database is UTC and stays that way - see
/// InventriaDbContext's value converters, and the DateTime.UtcNow every service
/// stamps a movement with. That is the right thing to store, because an instant
/// is an instant wherever it is read. What it does not answer is which day a
/// given instant fell on, and that is a question with a different answer in
/// every timezone.
///
/// Before this existed, "today" meant the UTC day. For a warehouse actually on
/// UTC that is correct and this class changes nothing. For one anywhere else it
/// was visibly wrong to the people on the floor: in Sydney the day's receipts
/// reset at 10 or 11 in the morning, and an evening shift in Los Angeles spent
/// its last hours watching its work counted against tomorrow.
///
/// Only the boundary calculation moves. Nothing here is ever written to the
/// database.
/// </summary>
public sealed class WarehouseClock
{
    /// <summary>Where the zone is configured. See appsettings.json.</summary>
    public const string ConfigurationKey = "Warehouse:TimeZone";

    private readonly TimeProvider _time;

    public WarehouseClock(TimeZoneInfo zone, TimeProvider? time = null)
    {
        Zone = zone;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The zone whose midnights this clock measures days between.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// A clock on UTC - exactly the behaviour every one of these figures had
    /// before a zone could be configured, and what an unconfigured deployment
    /// still gets.
    /// </summary>
    public static WarehouseClock Utc { get; } = new(TimeZoneInfo.Utc);

    /// <summary>
    /// The clock described by <see cref="ConfigurationKey"/>, or a UTC one when
    /// nothing is configured.
    /// </summary>
    /// <remarks>
    /// An unrecognised zone is refused rather than quietly fallen back on,
    /// the same way a missing Jwt:Key is. A typo in this setting produces
    /// numbers that are wrong by some whole number of hours and look
    /// completely ordinary, which is the failure this whole class exists to
    /// remove - discovering it at startup is the only cheap time to.
    /// </remarks>
    public static WarehouseClock FromConfiguration(IConfiguration configuration, TimeProvider? time = null)
    {
        var id = configuration[ConfigurationKey]?.Trim();

        if (string.IsNullOrEmpty(id)) return time is null ? Utc : new WarehouseClock(TimeZoneInfo.Utc, time);

        try
        {
            // IANA ids ("Europe/London") work on Windows and Windows ids
            // ("GMT Standard Time") work on Linux: .NET converts between the
            // two through ICU here, so one configured value is portable across
            // whatever the app is deployed onto.
            return new WarehouseClock(TimeZoneInfo.FindSystemTimeZoneById(id), time);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is set to '{id}', which is not a timezone this system knows. " +
                "Use an IANA id such as 'Europe/London' or 'America/New_York', or leave it blank to " +
                "keep measuring days in UTC.",
                ex);
        }
    }

    /// <summary>Now, on the clock every timestamp in the database is written with.</summary>
    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>The date it is in the warehouse right now.</summary>
    public DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Zone));

    /// <summary>
    /// The UTC instant the warehouse's current day began - the lower bound for
    /// anything counted "today".
    /// </summary>
    public DateTime StartOfToday() => StartOfDay(Today());

    /// <summary>
    /// The UTC instant <paramref name="date"/> begins in the warehouse.
    /// </summary>
    public DateTime StartOfDay(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Spring forward. In a zone that shifts its clocks at midnight - Chile
        // and Lebanon among others - the first minutes of the day simply never
        // occur, and asking for the UTC instant of a local time that did not
        // happen throws. The day starts at the moment the clocks jumped, which
        // is the first local time after midnight that does exist. Stepped a
        // minute at a time rather than by assuming the shift is an hour, since
        // it is not always.
        if (Zone.IsInvalidTime(midnight))
        {
            do
            {
                midnight = midnight.AddMinutes(1);
            }
            while (Zone.IsInvalidTime(midnight));

            return TimeZoneInfo.ConvertTimeToUtc(midnight, Zone);
        }

        // Fall back. Here midnight happens twice, and the day starts at the
        // first of them - the one at the larger, daylight-saving offset.
        // ConvertTimeToUtc resolves an ambiguous time to standard time instead,
        // which would hand the day's first hour to yesterday and count it
        // twice.
        if (Zone.IsAmbiguousTime(midnight))
        {
            var earliest = Zone.GetAmbiguousTimeOffsets(midnight).Max();
            return DateTime.SpecifyKind(midnight - earliest, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(midnight, Zone);
    }

    /// <summary>
    /// The UTC instant <paramref name="date"/> ends in the warehouse, as an
    /// exclusive upper bound: it is the start of the following day, so a filter
    /// wanting the whole of <paramref name="date"/> compares with &lt;, not
    /// &lt;=. Exclusive because the alternative is picking a last representable
    /// instant and hoping nothing is stamped after it.
    /// </summary>
    public DateTime EndOfDay(DateOnly date) => StartOfDay(date.AddDays(1));
}
