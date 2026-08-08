namespace GitHubGoal.Core.Models;

/// <summary>One cell of the GitHub contribution calendar.</summary>
public sealed record ContributionDay(DateOnly Date, int Count);

/// <summary>
/// A fetched contribution calendar covering some range, plus the instant it was
/// retrieved so the UI can age it.
/// </summary>
public sealed record ContributionCalendar(IReadOnlyList<ContributionDay> Days, int Total)
{
    public static ContributionCalendar Empty { get; } = new(Array.Empty<ContributionDay>(), 0);

    /// <summary>Count for a specific local date, or 0 if GitHub returned no cell for it.</summary>
    public int CountFor(DateOnly date)
    {
        foreach (var day in Days)
        {
            if (day.Date == date)
            {
                return day.Count;
            }
        }

        return 0;
    }
}

/// <summary>
/// What the widget renders: today's count, which local date that refers to, and
/// how fresh it is.
/// </summary>
public sealed record ContributionSnapshot(
    int Count,
    DateOnly LocalDate,
    DateTimeOffset FetchedAt)
{
    /// <summary>
    /// True when this snapshot was served from cache after a failed refresh, so the
    /// UI can show a subtle staleness indicator without blanking the number.
    /// </summary>
    public bool IsStale { get; init; }
}
