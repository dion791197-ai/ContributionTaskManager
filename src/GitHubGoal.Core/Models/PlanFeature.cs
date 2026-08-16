namespace GitHubGoal.Core.Models;

/// <summary>
/// Things a subscription can gate.
///
/// Intentionally empty: the tiers exist, but which capability belongs to which tier has
/// not been decided yet. Adding a gate is meant to be a two-line change — a member here
/// and a row in <see cref="FeatureMatrix.MinimumPlan"/> — so nothing else in the app has
/// to learn about pricing.
///
/// <code>
/// public enum PlanFeature
/// {
///     MultipleGoals,
///     ContributionHistory,
/// }
/// </code>
/// </summary>
public enum PlanFeature
{
}

/// <summary>
/// Maps each gated capability to the cheapest tier that includes it.
///
/// The default is deliberately permissive: anything absent from the table is available on
/// every tier. That way adding a feature to the app never silently locks it behind a
/// paywall — a capability only becomes paid when someone writes it down here.
/// </summary>
public static class FeatureMatrix
{
    private static readonly Dictionary<PlanFeature, SubscriptionPlan> Requirements = new()
    {
        // Fill in as tiers are decided, e.g.
        //   [PlanFeature.ContributionHistory] = SubscriptionPlan.Plus,
        //   [PlanFeature.MultipleGoals]       = SubscriptionPlan.Pro,
    };

    /// <summary>The cheapest tier that includes <paramref name="feature"/>.</summary>
    public static SubscriptionPlan MinimumPlan(PlanFeature feature) =>
        Requirements.TryGetValue(feature, out var required) ? required : SubscriptionPlan.Free;

    /// <summary>Whether <paramref name="plan"/> includes <paramref name="feature"/>.</summary>
    public static bool Includes(SubscriptionPlan plan, PlanFeature feature) =>
        plan >= MinimumPlan(feature);

    /// <summary>Every capability currently gated, for building comparison tables in the UI.</summary>
    public static IReadOnlyDictionary<PlanFeature, SubscriptionPlan> Gated => Requirements;
}
