namespace GitHubGoal.Core.Models;

/// <summary>
/// The subscription tiers.
///
/// Values are ordered and deliberately spaced so tiers can be compared directly
/// (<c>plan &gt;= SubscriptionPlan.Plus</c>) and so a tier can be inserted between two
/// existing ones later without renumbering anything already persisted.
/// </summary>
public enum SubscriptionPlan
{
    Free = 0,
    Plus = 100,
    Pro = 200,
}

/// <summary>Display metadata for a tier. Says nothing about what the tier unlocks.</summary>
public sealed record PlanInfo(SubscriptionPlan Plan, string Name, string Description);

/// <summary>
/// The tiers as presented in the UI.
///
/// Only naming lives here. What each tier actually unlocks is expressed in
/// <see cref="FeatureMatrix"/>, so wording and entitlements can move independently.
/// </summary>
public static class PlanCatalog
{
    public static readonly PlanInfo Free = new(
        SubscriptionPlan.Free,
        "Free",
        "Track today's contributions against a daily goal.");

    public static readonly PlanInfo Plus = new(
        SubscriptionPlan.Plus,
        "Plus",
        "Everything in Free.");

    public static readonly PlanInfo Pro = new(
        SubscriptionPlan.Pro,
        "Pro",
        "Everything in Plus.");

    /// <summary>All tiers, cheapest first.</summary>
    public static readonly IReadOnlyList<PlanInfo> All = [Free, Plus, Pro];

    public static PlanInfo For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Pro => Pro,
        SubscriptionPlan.Plus => Plus,
        _ => Free,
    };
}
