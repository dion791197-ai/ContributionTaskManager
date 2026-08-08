using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using GitHubGoal.Core.Utilities;
using Microsoft.UI.Dispatching;

namespace GitHubGoal.ViewModels;

/// <summary>
/// Drives the widget. Holds no networking of its own — everything goes through
/// <see cref="IContributionService"/> — and exposes only display-ready strings.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IContributionService _contributions;
    private readonly IGitHubAuthService _auth;
    private readonly ICredentialService _credentials;
    private readonly ISettingsService _settings;
    private readonly DispatcherQueue _dispatcher;

    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _clockTimer;

    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _authCts;
    private DateTimeOffset? _lastUpdatedAt;
    private bool _disposed;

    public MainViewModel(
        IContributionService contributions,
        IGitHubAuthService auth,
        ICredentialService credentials,
        ISettingsService settings,
        DispatcherQueue dispatcher)
    {
        _contributions = contributions;
        _auth = auth;
        _credentials = credentials;
        _settings = settings;
        _dispatcher = dispatcher;

        _goal = settings.Current.DailyGoal;

        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();

        // Re-renders "Updated 3 min ago" without hitting the network.
        _clockTimer = _dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(20);
        _clockTimer.Tick += (_, _) => OnPropertyChanged(nameof(UpdatedText));
        _clockTimer.Start();

        ApplySettings();
    }

    // --- state ------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress), nameof(CountText), nameof(GoalText), nameof(PercentText),
        nameof(RemainingText), nameof(IsGoalComplete), nameof(TargetFraction), nameof(TrayTooltip))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress), nameof(CountText), nameof(GoalText), nameof(PercentText),
        nameof(RemainingText), nameof(IsGoalComplete), nameof(TargetFraction), nameof(TrayTooltip))]
    private int _goal = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWidgetContent))]
    private bool _isSignedIn;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText), nameof(HasWarning))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private bool _isStale;

    [ObservableProperty]
    private string? _userLogin;

    [ObservableProperty]
    private string? _avatarUrl;

    // --- device-flow sign-in ---------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWidgetContent))]
    private bool _isAuthorizing;

    [ObservableProperty]
    private string? _userCode;

    [ObservableProperty]
    private string? _verificationUri;

    /// <summary>
    /// Seeded with the invitation copy rather than left null: x:Bind's FallbackValue
    /// only applies when a binding fails, not when it resolves to null.
    /// </summary>
    [ObservableProperty]
    private string? _authStatus = "Connect your GitHub account to track daily contributions.";

    // --- derived display values -------------------------------------------

    public GoalProgress Progress => new(Count, Goal);

    public string CountText => Count.ToString();

    public string GoalText => $"/ {Goal}";

    public string PercentText => $"{Progress.Percent}%";

    public string RemainingText => Progress.RemainingText;

    public bool IsGoalComplete => Progress.IsComplete;

    /// <summary>Where the progress bar should animate to.</summary>
    public double TargetFraction => Progress.Fraction;

    /// <summary>False while signing in, so the card can swap to the code prompt.</summary>
    public bool ShowsWidgetContent => IsSignedIn && !IsAuthorizing;

    public bool HasWarning => IsStale || ErrorMessage is not null;

    public string UpdatedText
    {
        get
        {
            if (ErrorMessage is { } error)
            {
                return _lastUpdatedAt is { } last
                    ? $"{error} · Last updated {RelativeTime.Describe(last, DateTimeOffset.UtcNow)}"
                    : error;
            }

            return _lastUpdatedAt is { } moment
                ? $"Updated {RelativeTime.Describe(moment, DateTimeOffset.UtcNow)}"
                : "Not updated yet";
        }
    }

    public string TrayTooltip => IsSignedIn
        ? $"GitHub Contributions\nToday: {Count} / {Goal}"
        : "GitHub Contributions — not signed in";

    // --- commands ---------------------------------------------------------

    /// <summary>Raised when the goal is reached, so the view can play the completion cue once.</summary>
    public event Action? GoalJustCompleted;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || !IsSignedIn)
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();

        IsBusy = true;
        try
        {
            var wasComplete = Progress.IsComplete;
            var result = await _contributions.RefreshAsync(_refreshCts.Token).ConfigureAwait(true);

            if (result.Snapshot is { } snapshot)
            {
                Count = snapshot.Count;
                _lastUpdatedAt = snapshot.FetchedAt;
                IsStale = snapshot.IsStale;
            }

            if (result.Error is { } error)
            {
                ErrorMessage = error.UserMessage;

                if (error.Kind == GitHubErrorKind.Unauthorized)
                {
                    IsSignedIn = false;
                    UserLogin = null;
                    AvatarUrl = null;
                }
            }
            else
            {
                ErrorMessage = null;
                IsStale = false;
                UserLogin = _contributions.CurrentUser?.DisplayName;
                AvatarUrl = _contributions.CurrentUser?.AvatarUrl;

                if (!wasComplete && Progress.IsComplete)
                {
                    GoalJustCompleted?.Invoke();
                }
            }

            OnPropertyChanged(nameof(UpdatedText));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer refresh.
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the device flow end to end and stores the token on success.</summary>
    [RelayCommand]
    public async Task SignInAsync()
    {
        var clientId = _settings.Current.OAuthClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            AuthStatus = "Add your OAuth Client ID in Settings first.";
            IsAuthorizing = true;
            return;
        }

        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();

        IsAuthorizing = true;
        AuthStatus = "Contacting GitHub…";
        UserCode = null;

        try
        {
            var request = await _auth.RequestDeviceCodeAsync(clientId, _authCts.Token).ConfigureAwait(true);

            UserCode = request.UserCode;
            VerificationUri = request.VerificationUri;
            AuthStatus = "Enter this code on GitHub";

            var token = await _auth.WaitForAccessTokenAsync(clientId, request, _authCts.Token).ConfigureAwait(true);

            _credentials.Write(CredentialService.GitHubTokenTarget, "github", token);

            IsAuthorizing = false;
            UserCode = null;
            IsSignedIn = true;

            await RefreshAsync().ConfigureAwait(true);
            StartAutoRefresh();
        }
        catch (GitHubException ex)
        {
            AuthStatus = ex.UserMessage == "Unable to update" ? ex.Message : ex.UserMessage;
            UserCode = null;
        }
        catch (OperationCanceledException)
        {
            IsAuthorizing = false;
            UserCode = null;
        }
    }

    [RelayCommand]
    public void CancelSignIn()
    {
        _authCts?.Cancel();
        IsAuthorizing = false;
        UserCode = null;
        AuthStatus = null;
    }

    public void SignOut()
    {
        _authCts?.Cancel();
        _refreshCts?.Cancel();
        _refreshTimer.Stop();

        _contributions.SignOut();

        IsSignedIn = false;
        UserLogin = null;
        AvatarUrl = null;
        Count = 0;
        _lastUpdatedAt = null;
        ErrorMessage = null;
        IsStale = false;
        OnPropertyChanged(nameof(UpdatedText));
    }

    // --- lifecycle --------------------------------------------------------

    /// <summary>Loads cached state and kicks off the first refresh.</summary>
    public async Task InitializeAsync()
    {
        IsSignedIn = _contributions.IsSignedIn;

        if (_contributions.LoadCachedSnapshot() is { } cached)
        {
            Count = cached.Count;
            _lastUpdatedAt = cached.FetchedAt;
            IsStale = true;
            UserLogin = _contributions.CurrentUser?.DisplayName;
            AvatarUrl = _contributions.CurrentUser?.AvatarUrl;
            OnPropertyChanged(nameof(UpdatedText));
        }

        if (IsSignedIn)
        {
            await RefreshAsync().ConfigureAwait(true);
            StartAutoRefresh();
        }
    }

    /// <summary>Re-reads settings after the Settings window changes something.</summary>
    public void ApplySettings()
    {
        var settings = _settings.Current;

        Goal = settings.DailyGoal;
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _refreshTimer.Stop();

        var settings = _settings.Current;
        if (!settings.AutoRefresh || !IsSignedIn)
        {
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, settings.RefreshIntervalMinutes));
        _refreshTimer.Start();
    }

    /// <summary>Called when the widget is shown again, so stale data updates promptly.</summary>
    public void OnActivated()
    {
        if (!IsSignedIn || IsBusy)
        {
            return;
        }

        // Only bother if the data has aged past roughly a minute.
        if (_lastUpdatedAt is null || DateTimeOffset.UtcNow - _lastUpdatedAt > TimeSpan.FromMinutes(1))
        {
            _ = RefreshAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _refreshTimer.Stop();
        _clockTimer.Stop();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _authCts?.Cancel();
        _authCts?.Dispose();
    }
}
