namespace GitHubGoal.Core.Models;

/// <summary>
/// The daily-goal calculation, kept as a pure value type so it can be unit tested
/// without any GitHub or UI dependency.
/// </summary>
public readonly record struct GoalProgress
{
    /// <summary>Smallest goal we accept; a zero goal would make percentages undefined.</summary>
    public const int MinimumGoal = 1;

    public GoalProgress(int contributions, int goal)
    {
        Contributions = Math.Max(0, contributions);
        Goal = Math.Max(MinimumGoal, goal);
    }

    public int Contributions { get; }

    public int Goal { get; }

    /// <summary>Progress clamped to 0..1 — what the progress indicator should fill to.</summary>
    public double Fraction => Math.Clamp((double)Contributions / Goal, 0d, 1d);

    /// <summary>Unclamped ratio; 15 of 10 gives 1.5. Used for the percentage label.</summary>
    public double RawFraction => (double)Contributions / Goal;

    /// <summary>
    /// Whole-number percentage, not capped, so exceeding the goal reads as 150%.
    /// Rounds away from zero: 7/10 is 70%, 1/3 is 33%.
    /// </summary>
    public int Percent => (int)Math.Round(RawFraction * 100d, MidpointRounding.AwayFromZero);

    /// <summary>How many more contributions are needed. Zero once the goal is met.</summary>
    public int Remaining => Math.Max(0, Goal - Contributions);

    public bool IsComplete => Contributions >= Goal;

    public bool IsExceeded => Contributions > Goal;

    /// <summary>
    /// The supporting line under the counter, answering "how much is left?" at a glance.
    /// </summary>
    public string RemainingText => this switch
    {
        { IsExceeded: true } => "Goal exceeded",
        { IsComplete: true } => "Goal completed",
        { Remaining: 1 } => "1 contribution to goal",
        var p => $"{p.Remaining} contributions to goal",
    };

    public override string ToString() => $"{Contributions} / {Goal}";
}
