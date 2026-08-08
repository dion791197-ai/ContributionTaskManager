namespace GitHubGoal.Core.Utilities;

/// <summary>
/// Local-calendar-day boundaries.
///
/// "Today" must follow the user's Windows time zone, not UTC — at 01:00 local in
/// UTC+3 the UTC date is still yesterday, and using it would show the wrong count
/// for three hours every night.
/// </summary>
public static class LocalDay
{
    /// <summary>
    /// The instant local midnight begins for the day containing <paramref name="instant"/>.
    /// </summary>
    public static DateTimeOffset StartOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return Resolve(local.Date, zone);
    }

    /// <summary>
    /// The half-open end of the day — i.e. the start of tomorrow. Callers querying an
    /// inclusive API should subtract a tick.
    /// </summary>
    public static DateTimeOffset EndOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return Resolve(local.Date.AddDays(1), zone);
    }

    /// <summary>Start and inclusive end of the local day, ready for a range query.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Bounds(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var start = StartOf(instant, zone);
        var end = EndOf(instant, zone).AddTicks(-1);
        return (start, end);
    }

    /// <summary>The local calendar date for an instant.</summary>
    public static DateOnly DateFor(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).Date);

    /// <summary>
    /// Turns a wall-clock local time into an absolute instant, coping with the two
    /// times a year when that mapping is not one-to-one.
    /// </summary>
    private static DateTimeOffset Resolve(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        // Spring forward: this wall-clock time never happens. Some zones (e.g. parts of
        // Brazil, Iran) shift at midnight exactly, so 00:00 itself can be skipped.
        // Walk forward to the first instant that does exist.
        if (zone.IsInvalidTime(unspecified))
        {
            for (var minutes = 1; minutes <= 24 * 60; minutes++)
            {
                var candidate = unspecified.AddMinutes(minutes);
                if (!zone.IsInvalidTime(candidate))
                {
                    unspecified = candidate;
                    break;
                }
            }
        }

        // Fall back: this wall-clock time happens twice. GetUtcOffset resolves ambiguity
        // to standard time, which is the later of the two — consistent with how Windows
        // itself reports the day boundary.
        var offset = zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}
