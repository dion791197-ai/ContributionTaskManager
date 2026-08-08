using GitHubGoal.Core.Utilities;
using Xunit;

namespace GitHubGoal.Core.Tests;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(179, "2 min ago")]
    [InlineData(300, "5 min ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(172800, "2 days ago")]
    public void Describes_elapsed_time_in_plain_language(int secondsAgo, string expected)
    {
        Assert.Equal(expected, RelativeTime.Describe(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void A_timestamp_slightly_in_the_future_reads_as_current()
    {
        // Clock adjustments should never produce "-1 min ago".
        Assert.Equal("just now", RelativeTime.Describe(Now.AddSeconds(5), Now));
    }
}
