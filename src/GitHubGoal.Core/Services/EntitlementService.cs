using GitHubGoal.Core.Models;

namespace GitHubGoal.Core.Services;

/// <summary>
/// Turns a license key into a tier.
///
/// Split out so the rest of the app never learns how entitlements are decided. Today the
/// only implementation is <see cref="OfflineLicenseValidator"/>; a server call or a
/// signed-key check can replace it without touching a single call site.
/// </summary>
public interface ILicenseValidator
{
    Task<License> ValidateAsync(string? key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Placeholder validator: any key at all is rejected, so the app runs on Free.
///
/// This is scaffolding, not enforcement. Wiring a real check in means replacing this
/// class — the shape of the result is already what the rest of the app consumes.
/// </summary>
public sealed class OfflineLicenseValidator : ILicenseValidator
{
    public Task<License> ValidateAsync(string? key, CancellationToken cancellationToken = default) =>
        Task.FromResult(License.Free);
}

public interface IEntitlementService
{
    SubscriptionPlan CurrentPlan { get; }

    /// <summary>Whether the current tier includes a capability.</summary>
    bool Has(PlanFeature feature);

    /// <summary>Re-resolves the tier from the stored license.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a license key and re-resolves. Returns the tier now in force.</summary>
    Task<SubscriptionPlan> ActivateAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Forgets the license and drops to Free.</summary>
    void Deactivate();

    event Action? PlanChanged;
}

/// <summary>
/// The app's single source of truth for which tier is active.
///
/// Note what this is not: it is not a protection mechanism. The resolved tier is cached
/// in settings.json so the widget renders correctly before any validation completes, and
/// that file is user-writable. Anything that must actually be paid for has to be verified
/// where it is served, not here.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly ILicenseStore _store;
    private readonly ILicenseValidator _validator;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;

    private License _license = License.Free;

    public EntitlementService(
        ILicenseStore store,
        ILicenseValidator validator,
        ISettingsService settings,
        TimeProvider? time = null)
    {
        _store = store;
        _validator = validator;
        _settings = settings;
        _time = time ?? TimeProvider.System;

        // Start from the cached tier so the UI is not briefly wrong on launch. Anything
        // expired falls back to Free until RefreshAsync says otherwise.
        var cached = _settings.Current.CachedPlan;
        if (Enum.IsDefined(cached))
        {
            _license = new License(cached, null, _settings.Current.CachedPlanExpiresAt);

            if (!_license.IsValidAt(_time.GetUtcNow()))
            {
                _license = License.Free;
            }
        }
    }

    public SubscriptionPlan CurrentPlan =>
        _license.IsValidAt(_time.GetUtcNow()) ? _license.Plan : SubscriptionPlan.Free;

    public event Action? PlanChanged;

    public bool Has(PlanFeature feature) => FeatureMatrix.Includes(CurrentPlan, feature);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var key = _store.ReadKey();
        await ApplyAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionPlan> ActivateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A license key is required.", nameof(key));
        }

        var trimmed = key.Trim();
        var license = await _validator.ValidateAsync(trimmed, cancellationToken).ConfigureAwait(false);

        // Only keep a key that actually bought something, so a typo does not linger in
        // the credential store looking like an activation.
        if (license.Plan != SubscriptionPlan.Free)
        {
            _store.SaveKey(trimmed);
        }

        Apply(license);
        return CurrentPlan;
    }

    public void Deactivate()
    {
        _store.Clear();
        Apply(License.Free);
    }

    private async Task ApplyAsync(string? key, CancellationToken cancellationToken)
    {
        var license = await _validator.ValidateAsync(key, cancellationToken).ConfigureAwait(false);
        Apply(license);
    }

    private void Apply(License license)
    {
        var previous = CurrentPlan;
        _license = license;

        var settings = _settings.Current;
        settings.CachedPlan = license.Plan;
        settings.CachedPlanExpiresAt = license.ExpiresAt;
        _settings.Save(settings);

        if (CurrentPlan != previous)
        {
            PlanChanged?.Invoke();
        }
    }
}
