using GitHubGoal.Core.Services;
using GitHubGoal.Interop;
using GitHubGoal.ViewModels;
using GitHubGoal.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GitHubGoal;

/// <summary>
/// Composition root. The object graph is small and fixed, so it is wired by hand
/// rather than through a container.
/// </summary>
public partial class App : Application
{
    private readonly HttpClient _http = GitHubHttp.Create();

    private SettingsService _settings = null!;
    private CredentialService _credentials = null!;
    private ContributionService _contributions = null!;
    private StartupService _startup = null!;
    private MainViewModel _viewModel = null!;

    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings = new SettingsService();
        _settings.Load();

        _credentials = new CredentialService();
        _startup = new StartupService();
        _contributions = new ContributionService(
            new GitHubService(_http),
            _credentials,
            _settings);

        _viewModel = new MainViewModel(
            _contributions,
            new GitHubAuthService(_http),
            _credentials,
            _settings,
            DispatcherQueue.GetForCurrentThread());

        // Subscribed once here rather than in CreateWindow: the widget can be closed
        // and recreated from the tray, which would otherwise stack up handlers.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.TrayTooltip))
            {
                UpdateTrayTooltip();
            }
        };

        _window = CreateWindow();
        SetUpTray();

        // Launched by the Run key: start hidden so login is not interrupted.
        var startMinimised = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase));

        if (!startMinimised)
        {
            _window.Activate();
        }

        _ = _viewModel.InitializeAsync().ContinueWith(
            _ => _window?.DispatcherQueue.TryEnqueue(UpdateTrayTooltip),
            TaskScheduler.Default);
    }

    private MainWindow CreateWindow()
    {
        var window = new MainWindow(_viewModel, _settings);

        window.HideRequested += HideWidget;
        window.SettingsRequested += ShowSettings;

        // Closing the widget hides it, matching how utility apps behave; Quit in the
        // tray menu is the only way out.
        window.Closed += (_, _) => _window = null;

        return window;
    }

    // ================= tray =================

    private void SetUpTray()
    {
        try
        {
            _tray = new TrayIcon(
                Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                _viewModel.TrayTooltip);
        }
        catch (InvalidOperationException ex)
        {
            // Without a tray icon the widget still works, but it cannot be recalled
            // once hidden — so record why rather than failing silently.
            CrashLog.Write("TrayIcon", ex);
            return;
        }

        _tray.Activated += ShowWidget;
        _tray.MenuBuilder = BuildTrayMenu;
    }

    private IReadOnlyList<TrayIcon.MenuEntry> BuildTrayMenu()
    {
        var status = _viewModel.IsSignedIn
            ? $"Today: {_viewModel.Count} / {_viewModel.Goal}"
            : "Not signed in";

        return
        [
            TrayIcon.MenuEntry.Header("GitHub Contributions"),
            TrayIcon.MenuEntry.Header(status),
            TrayIcon.MenuEntry.Separator,
            new TrayIcon.MenuEntry("Open Widget", ShowWidget),
            new TrayIcon.MenuEntry("Refresh", () => Dispatch(() => _ = _viewModel.RefreshAsync()), IsEnabled: _viewModel.IsSignedIn),
            new TrayIcon.MenuEntry("Settings", () => Dispatch(ShowSettings)),
            TrayIcon.MenuEntry.Separator,
            new TrayIcon.MenuEntry("Quit", () => Dispatch(Quit)),
        ];
    }

    private void UpdateTrayTooltip() => _tray?.SetTooltip(_viewModel.TrayTooltip);

    /// <summary>
    /// Tray callbacks arrive on the message-only window's thread, which is the UI
    /// thread here, but menu actions can run re-entrantly inside TrackPopupMenu — so
    /// everything is posted rather than invoked inline.
    /// </summary>
    private void Dispatch(Action action)
    {
        var queue = _window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        queue?.TryEnqueue(() => action());
    }

    // ================= window lifetime =================

    private void ShowWidget() => Dispatch(() =>
    {
        _window ??= CreateWindow();
        _window.Activate();
        _viewModel.OnActivated();
    });

    private void HideWidget()
    {
        // AppWindow.Hide keeps the window alive so its position and state survive.
        _window?.AppWindow.Hide();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settings, _startup, _contributions);
        _settingsWindow = new SettingsWindow(viewModel);

        viewModel.SettingsChanged += OnSettingsChanged;

        _settingsWindow.Closed += (_, _) =>
        {
            viewModel.SettingsChanged -= OnSettingsChanged;
            _settingsWindow = null;
        };

        _settingsWindow.Activate();
    }

    private void OnSettingsChanged()
    {
        _viewModel.ApplySettings();
        _window?.ApplyTheme();
        _window?.ApplyAlwaysOnTop(_settings.Current.AlwaysOnTop);

        if (!_contributions.IsSignedIn)
        {
            _viewModel.SignOut();
        }
    }

    private void Quit()
    {
        _viewModel.Dispose();
        _tray?.Dispose();
        _tray = null;

        _settingsWindow?.Close();
        _window?.Close();

        Exit();
    }
}
