namespace GitHubGoal.Core.Utilities;

/// <summary>Formats the "Updated ..." footer line.</summary>
public static class RelativeTime
{
    public static string Describe(DateTimeOffset moment, DateTimeOffset now)
    {
        var elapsed = now - moment;

        // Clock skew or a timestamp a hair in the future still reads as current.
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromSeconds(60))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 min ago" : $"{minutes} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }
}
