using GitHubGoal.Core.Utilities;
using Xunit;

namespace GitHubGoal.Core.Tests;

public class LocalDayTests
{
    // Fixed zones so the suite behaves the same on any machine.
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo Moscow = FindZone("Russian Standard Time", "Europe/Moscow");   // UTC+3, no DST
    private static readonly TimeZoneInfo NewYork = FindZone("Eastern Standard Time", "America/New_York"); // DST
    private static readonly TimeZoneInfo Kathmandu = FindZone("Nepal Standard Time", "Asia/Kathmandu");   // UTC+05:45

    private static TimeZoneInfo FindZone(string windowsId, string ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
    }

    [Fact]
    public void Day_starts_at_local_midnight_not_utc_midnight()
    {
        // 21:30 UTC is already the next day in Moscow (00:30 local).
        var instant = new DateTimeOffset(2026, 3, 10, 21, 30, 0, TimeSpan.Zero);

        var start = LocalDay.StartOf(instant, Moscow);

        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.FromHours(3)), start);
        Assert.Equal(new DateOnly(2026, 3, 11), LocalDay.DateFor(instant, Moscow));
    }

    [Fact]
    public void Utc_and_local_dates_differ_across_the_boundary()
    {
        var instant = new DateTimeOffset(2026, 3, 10, 22, 15, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 10), LocalDay.DateFor(instant, Utc));
        Assert.Equal(new DateOnly(2026, 3, 11), LocalDay.DateFor(instant, Moscow));
    }

    [Fact]
    public void One_second_before_midnight_still_belongs_to_the_old_day()
    {
        var instant = new DateTimeOffset(2026, 3, 10, 23, 59, 59, TimeSpan.FromHours(3));

        Assert.Equal(new DateOnly(2026, 3, 10), LocalDay.DateFor(instant, Moscow));
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.FromHours(3)), LocalDay.StartOf(instant, Moscow));
    }

    [Fact]
    public void Midnight_exactly_starts_the_new_day()
    {
        var instant = new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.FromHours(3));

        Assert.Equal(new DateOnly(2026, 3, 11), LocalDay.DateFor(instant, Moscow));
        Assert.Equal(instant, LocalDay.StartOf(instant, Moscow));
    }

    [Fact]
    public void Bounds_cover_the_whole_day_without_overlapping_the_next()
    {
        var instant = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(3));

        var (from, to) = LocalDay.Bounds(instant, Moscow);

        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.FromHours(3)), from);
        // Inclusive end: one tick short of the next midnight.
        Assert.Equal(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.FromHours(3)).AddTicks(-1), to);
        Assert.True(to - from < TimeSpan.FromDays(1));
    }

    [Fact]
    public void Half_hour_offset_zones_are_handled()
    {
        // Kathmandu is UTC+05:45.
        var instant = new DateTimeOffset(2026, 6, 15, 20, 0, 0, TimeSpan.Zero); // 01:45 next day local

        Assert.Equal(new DateOnly(2026, 6, 16), LocalDay.DateFor(instant, Kathmandu));
        Assert.Equal(TimeSpan.FromMinutes(345), LocalDay.StartOf(instant, Kathmandu).Offset);
    }

    [Fact]
    public void Spring_forward_day_still_resolves_to_a_real_instant()
    {
        // US DST begins 2026-03-08; clocks jump 02:00 -> 03:00. Midnight is unaffected
        // here, but the day is only 23 hours long.
        var instant = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.FromHours(-4));

        var (from, to) = LocalDay.Bounds(instant, NewYork);

        Assert.Equal(TimeSpan.FromHours(-5), from.Offset); // still EST at midnight
        Assert.Equal(TimeSpan.FromHours(-4), to.Offset);   // EDT by the end of the day
        Assert.Equal(TimeSpan.FromHours(23), (to - from) + TimeSpan.FromTicks(1));
    }

    [Fact]
    public void Fall_back_day_is_twenty_five_hours_long()
    {
        // US DST ends 2026-11-01; 01:00-02:00 happens twice.
        var instant = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5));

        var (from, to) = LocalDay.Bounds(instant, NewYork);

        Assert.Equal(TimeSpan.FromHours(-4), from.Offset); // EDT at midnight
        Assert.Equal(TimeSpan.FromHours(-5), to.Offset);   // EST by the end
        Assert.Equal(TimeSpan.FromHours(25), (to - from) + TimeSpan.FromTicks(1));
    }

    [Fact]
    public void Start_of_day_is_stable_no_matter_which_offset_the_caller_supplies()
    {
        // Same instant expressed three ways must yield the same local day.
        var utc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var moscow = utc.ToOffset(TimeSpan.FromHours(3));
        var newYork = utc.ToOffset(TimeSpan.FromHours(-4));

        var expected = LocalDay.StartOf(utc, Moscow);

        Assert.Equal(expected, LocalDay.StartOf(moscow, Moscow));
        Assert.Equal(expected, LocalDay.StartOf(newYork, Moscow));
    }
}
