using GitHubGoal.Core.Models;
using Xunit;

namespace GitHubGoal.Core.Tests;

public class GoalProgressTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 10)]
    [InlineData(7, 10, 70)]
    [InlineData(10, 10, 100)]
    [InlineData(15, 10, 150)]
    public void Percent_matches_the_expected_reading(int contributions, int goal, int expected)
    {
        Assert.Equal(expected, new GoalProgress(contributions, goal).Percent);
    }

    [Theory]
    [InlineData(0, 10, 0d)]
    [InlineData(7, 10, 0.7d)]
    [InlineData(10, 10, 1d)]
    [InlineData(15, 10, 1d)] // the bar fills, it does not overflow
    public void Fraction_is_clamped_for_the_progress_indicator(int contributions, int goal, double expected)
    {
        Assert.Equal(expected, new GoalProgress(contributions, goal).Fraction, precision: 6);
    }

    [Fact]
    public void RawFraction_is_not_clamped_so_the_label_can_exceed_100()
    {
        Assert.Equal(1.5d, new GoalProgress(15, 10).RawFraction, precision: 6);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(7, 10, 3)]
    [InlineData(10, 10, 0)]
    [InlineData(15, 10, 0)] // never negative
    public void Remaining_never_goes_below_zero(int contributions, int goal, int expected)
    {
        Assert.Equal(expected, new GoalProgress(contributions, goal).Remaining);
    }

    [Theory]
    [InlineData(0, 10, false, false)]
    [InlineData(9, 10, false, false)]
    [InlineData(10, 10, true, false)]
    [InlineData(15, 10, true, true)]
    public void Completion_flags_track_the_goal(int contributions, int goal, bool complete, bool exceeded)
    {
        var progress = new GoalProgress(contributions, goal);

        Assert.Equal(complete, progress.IsComplete);
        Assert.Equal(exceeded, progress.IsExceeded);
    }

    [Theory]
    [InlineData(7, 10, "3 contributions to goal")]
    [InlineData(9, 10, "1 contribution to goal")] // singular
    [InlineData(10, 10, "Goal completed")]
    [InlineData(15, 10, "Goal exceeded")]
    public void RemainingText_reads_naturally(int contributions, int goal, string expected)
    {
        Assert.Equal(expected, new GoalProgress(contributions, goal).RemainingText);
    }

    [Fact]
    public void Goal_below_one_is_coerced_so_percentage_stays_defined()
    {
        var progress = new GoalProgress(5, 0);

        Assert.Equal(GoalProgress.MinimumGoal, progress.Goal);
        Assert.Equal(500, progress.Percent);
    }

    [Fact]
    public void Negative_contributions_are_treated_as_zero()
    {
        Assert.Equal(0, new GoalProgress(-3, 10).Contributions);
    }

    [Fact]
    public void Percent_rounds_away_from_zero()
    {
        // 1/3 is 33.33% and 2/3 is 66.67%
        Assert.Equal(33, new GoalProgress(1, 3).Percent);
        Assert.Equal(67, new GoalProgress(2, 3).Percent);
    }

    [Fact]
    public void ToString_is_the_headline_format()
    {
        Assert.Equal("7 / 10", new GoalProgress(7, 10).ToString());
    }
}
