namespace GitHubGoal.Core.Models;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum AccentChoice
{
    GitHubGreen = 0,
    System = 1,
    Custom = 2,
}

/// <summary>
/// Everything persisted between runs. Deliberately contains no secrets — the access
/// token lives in Windows Credential Manager, never in this file.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Goal presets offered in Settings; any other value is treated as "Custom".</summary>
    public static readonly int[] GoalPresets = [1, 3, 5, 10, 15, 20];

    public int DailyGoal { get; set; } = 10;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>0 = nearly clear, 1 = heavily frosted. Drives the glass tint opacity.</summary>
    public double GlassIntensity { get; set; } = 0.5;

    public AccentChoice Accent { get; set; } = AccentChoice.GitHubGreen;

    /// <summary>Used only when <see cref="Accent"/> is <see cref="AccentChoice.Custom"/>.</summary>
    public string CustomAccentHex { get; set; } = "#2EA043";

    public bool AlwaysOnTop { get; set; } = true;

    public bool LaunchAtStartup { get; set; }

    public bool AutoRefresh { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = 5;

    public bool ReduceMotion { get; set; }

    // --- window placement -------------------------------------------------
    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    public double WindowWidth { get; set; } = 320;

    public double WindowHeight { get; set; } = 196;

    /// <summary>
    /// GitHub OAuth App client ID. Public by design for the device flow — there is no
    /// client secret involved — so plain storage here is appropriate.
    /// </summary>
    public string? OAuthClientId { get; set; }

    // --- subscription -----------------------------------------------------

    /// <summary>
    /// Last resolved tier, cached so the UI is not briefly wrong at startup.
    ///
    /// A cache, not an authority: this file is user-writable, so nothing that must
    /// actually be paid for should trust it. See EntitlementService.
    /// </summary>
    public SubscriptionPlan CachedPlan { get; set; } = SubscriptionPlan.Free;

    /// <summary>When the cached tier lapses; null means it does not expire.</summary>
    public DateTimeOffset? CachedPlanExpiresAt { get; set; }

    /// <summary>Remembered so the header can render before the first network call returns.</summary>
    public string? LastKnownLogin { get; set; }

    public string? LastKnownName { get; set; }

    public string? LastKnownAvatarUrl { get; set; }

    // --- offline cache ----------------------------------------------------
    public int? CachedCount { get; set; }

    /// <summary>ISO date the cached count belongs to; a new local day invalidates it.</summary>
    public string? CachedDate { get; set; }

    public DateTimeOffset? CachedAt { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
