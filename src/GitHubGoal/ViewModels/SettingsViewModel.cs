using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;

namespace GitHubGoal.ViewModels;

/// <summary>
/// Backs the Settings window. Every setter writes through to disk immediately and
/// raises <see cref="SettingsChanged"/> so the widget can react live.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IStartupService _startup;
    private readonly IContributionService _contributions;
    private readonly IEntitlementService _entitlements;

    private bool _loading;

    public SettingsViewModel(
        ISettingsService settings,
        IStartupService startup,
        IContributionService contributions,
        IEntitlementService entitlements)
    {
        _settings = settings;
        _startup = startup;
        _contributions = contributions;
        _entitlements = entitlements;

        _entitlements.PlanChanged += OnPlanChanged;

        _loading = true;

        var current = settings.Current;

        _dailyGoal = current.DailyGoal;
        _themeIndex = (int)current.Theme;
        _accentIndex = (int)current.Accent;
        _glassIntensity = current.GlassIntensity;
        _alwaysOnTop = current.AlwaysOnTop;
        _autoRefresh = current.AutoRefresh;
        _refreshIntervalMinutes = current.RefreshIntervalMinutes;
        _reduceMotion = current.ReduceMotion;
        _oauthClientId = current.OAuthClientId ?? string.Empty;

        // Read from the registry rather than the settings file, so a change made in
        // Task Manager is reflected here.
        _launchAtStartup = startup.IsEnabled;

        _loading = false;
    }

    /// <summary>Raised after any setting is persisted.</summary>
    public event Action? SettingsChanged;

    public IReadOnlyList<int> GoalPresets => AppSettings.GoalPresets;

    [ObservableProperty]
    private int _dailyGoal;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _accentIndex;

    [ObservableProperty]
    private double _glassIntensity;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private bool _autoRefresh;

    [ObservableProperty]
    private int _refreshIntervalMinutes;

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private string _oauthClientId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatus), nameof(IsConnected))]
    private bool _signedOut;

    public bool IsConnected => !SignedOut && _contributions.IsSignedIn;

    public string ConnectionStatus => IsConnected
        ? $"Connected as {_contributions.CurrentUser?.Login ?? _settings.Current.LastKnownLogin ?? "your account"}"
        : "Not connected";

    [RelayCommand]
    private void Disconnect()
    {
        _contributions.SignOut();
        SignedOut = true;
        SettingsChanged?.Invoke();
    }

    // --- subscription -----------------------------------------------------

    /// <summary>The three tiers, for the comparison list in Settings.</summary>
    public IReadOnlyList<PlanInfo> Plans => PlanCatalog.All;

    public SubscriptionPlan CurrentPlan => _entitlements.CurrentPlan;

    public string CurrentPlanName => PlanCatalog.For(CurrentPlan).Name;

    public string CurrentPlanDescription => PlanCatalog.For(CurrentPlan).Description;

    public bool IsPaidPlan => CurrentPlan != SubscriptionPlan.Free;

    [ObservableProperty]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    private string? _licenseMessage;

    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKey))
        {
            LicenseMessage = "Enter a licence key.";
            return;
        }

        try
        {
            var plan = await _entitlements.ActivateAsync(LicenseKey);

            // No validator is wired up yet, so every key resolves to Free. Say so plainly
            // rather than reporting a success that did not happen.
            LicenseMessage = plan == SubscriptionPlan.Free
                ? "That key was not accepted."
                : $"{PlanCatalog.For(plan).Name} activated.";

            if (plan != SubscriptionPlan.Free)
            {
                LicenseKey = string.Empty;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LicenseMessage = "Could not verify the key right now.";
        }
    }

    [RelayCommand]
    private void RemoveLicense()
    {
        _entitlements.Deactivate();
        LicenseMessage = "Licence removed.";
    }

    private void OnPlanChanged()
    {
        OnPropertyChanged(nameof(CurrentPlan));
        OnPropertyChanged(nameof(CurrentPlanName));
        OnPropertyChanged(nameof(CurrentPlanDescription));
        OnPropertyChanged(nameof(IsPaidPlan));
        SettingsChanged?.Invoke();
    }

    // --- persistence ------------------------------------------------------

    partial void OnDailyGoalChanged(int value) => Persist(s => s.DailyGoal = Math.Max(GoalProgress.MinimumGoal, value));

    partial void OnThemeIndexChanged(int value) => Persist(s => s.Theme = (AppTheme)Math.Clamp(value, 0, 2));

    partial void OnAccentIndexChanged(int value) => Persist(s => s.Accent = (AccentChoice)Math.Clamp(value, 0, 2));

    partial void OnGlassIntensityChanged(double value) => Persist(s => s.GlassIntensity = value);

    partial void OnAlwaysOnTopChanged(bool value) => Persist(s => s.AlwaysOnTop = value);

    partial void OnAutoRefreshChanged(bool value) => Persist(s => s.AutoRefresh = value);

    partial void OnRefreshIntervalMinutesChanged(int value) => Persist(s => s.RefreshIntervalMinutes = Math.Max(1, value));

    partial void OnReduceMotionChanged(bool value) => Persist(s => s.ReduceMotion = value);

    partial void OnOauthClientIdChanged(string value) =>
        Persist(s => s.OAuthClientId = string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _startup.SetEnabled(value);
        Persist(s => s.LaunchAtStartup = value);
    }

    /// <summary>
    /// Releases the entitlement subscription. The Settings window is opened and closed
    /// repeatedly, and the service outlives it, so the handler would otherwise accumulate.
    /// </summary>
    public void Dispose()
    {
        _entitlements.PlanChanged -= OnPlanChanged;
    }

    private void Persist(Action<AppSettings> apply)
    {
        if (_loading)
        {
            return;
        }

        var settings = _settings.Current;
        apply(settings);
        _settings.Save(settings);

        SettingsChanged?.Invoke();
    }
}
