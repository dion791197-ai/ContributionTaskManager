using System.ComponentModel;
using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using GitHubGoal.Interop;
using GitHubGoal.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace GitHubGoal.Views;

/// <summary>
/// The floating glass widget: frameless, always on top by default, draggable
/// anywhere on its surface, and remembering where it was left.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan CountDuration = TimeSpan.FromMilliseconds(550);

    private readonly ISettingsService _settings;
    private readonly UISettings _uiSettings = new();

    private OverlappedPresenter? _presenter;

    // Drag state, tracked in physical screen pixels.
    private bool _isDragging;
    private (int X, int Y) _dragOrigin;
    private PointInt32 _windowOrigin;

    // Count animation.
    private DispatcherTimer? _countTimer;
    private DateTimeOffset _countStartedAt;
    private double _countFrom;
    private double _countTo;
    private double _percentFrom;
    private double _percentTo;

    private Storyboard? _spinner;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        ViewModel = viewModel;
        _settings = settings;

        InitializeComponent();

        ConfigureWindow();
        ApplyTheme();
        RestorePlacement();
        HookDragging();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.GoalJustCompleted += OnGoalJustCompleted;

        Activated += OnActivated;
        Closed += OnClosed;

        // Reflect whatever state the view model already holds.
        UpdateVisuals(animate: false);
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Raised when the user hides the widget; the host keeps the app in the tray.</summary>
    public event Action? HideRequested;

    /// <summary>Raised when the settings button is pressed.</summary>
    public event Action? SettingsRequested;

    // ================= window chrome =================

    private void ConfigureWindow()
    {
        Title = "GitHub Goal";

        // Frameless: keep the resize border (so the widget can be resized) but drop the
        // caption bar entirely, then draw our own card inside.
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsAlwaysOnTop = _settings.Current.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);

        ExtendsContentIntoTitleBar = true;

        // A widget should not compete for space in Alt+Tab or the taskbar; the tray
        // icon is how it gets recalled.
        AppWindow.IsShownInSwitchers = false;

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        NativeWindow.ApplyRoundedCorners(WinRT.Interop.WindowNative.GetWindowHandle(this));

        TrySetBackdrop();
    }

    /// <summary>
    /// Prefers desktop acrylic (translucent, blurs the desktop behind) and falls back
    /// to Mica, then to the card tint alone on machines where neither is available.
    /// </summary>
    private void TrySetBackdrop()
    {
        try
        {
            if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
                return;
            }

            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
        }
        catch (Exception)
        {
            // Leave SystemBackdrop null; GlassCard still renders a usable frosted card.
        }
    }

    public void ApplyTheme()
    {
        RootGrid.RequestedTheme = _settings.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // Glass intensity scales the card tint without touching the system backdrop.
        GlassCard.Opacity = 0.55 + (_settings.Current.GlassIntensity * 0.45);
    }

    public void ApplyAlwaysOnTop(bool alwaysOnTop)
    {
        if (_presenter is not null)
        {
            _presenter.IsAlwaysOnTop = alwaysOnTop;
        }
    }

    // ================= placement =================

    private void RestorePlacement()
    {
        var settings = _settings.Current;

        var width = (int)Math.Round(settings.WindowWidth);
        var height = (int)Math.Round(settings.WindowHeight);

        // Settings store logical pixels; AppWindow works in physical ones.
        var scale = GetScaleFactor();
        var physicalWidth = (int)Math.Round(width * scale);
        var physicalHeight = (int)Math.Round(height * scale);

        int x, y;

        if (settings.WindowX is { } savedX && settings.WindowY is { } savedY)
        {
            x = (int)Math.Round(savedX);
            y = (int)Math.Round(savedY);
        }
        else
        {
            // First run: tuck into the top-right of the primary work area.
            var work = NativeWindow.PrimaryWorkArea();
            x = work.Right - physicalWidth - 24;
            y = work.Top + 24;
        }

        (x, y) = ClampToVisibleArea(x, y, physicalWidth, physicalHeight);

        AppWindow.MoveAndResize(new RectInt32(x, y, physicalWidth, physicalHeight));
    }

    /// <summary>
    /// Keeps the widget reachable when the monitor it was on has been unplugged or the
    /// layout changed, by nudging it back inside the nearest monitor's work area.
    /// </summary>
    private static (int X, int Y) ClampToVisibleArea(int x, int y, int width, int height)
    {
        var work = NativeWindow.WorkAreaForPoint(x + (width / 2), y + (height / 2));

        // If the saved spot is wholly outside every monitor, WorkAreaForPoint returns the
        // nearest one, so clamping against it always lands somewhere visible.
        var clampedX = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        var clampedY = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        return (clampedX, clampedY);
    }

    private double GetScaleFactor() =>
        NativeWindow.ScaleFor(WinRT.Interop.WindowNative.GetWindowHandle(this));

    private void SavePlacement()
    {
        var settings = _settings.Current;
        var scale = GetScaleFactor();

        settings.WindowX = AppWindow.Position.X;
        settings.WindowY = AppWindow.Position.Y;
        settings.WindowWidth = AppWindow.Size.Width / scale;
        settings.WindowHeight = AppWindow.Size.Height / scale;

        _settings.Save(settings);
    }

    // ================= dragging =================

    private void HookDragging()
    {
        // Handled manually rather than via SetTitleBar: a WinUI title-bar region
        // swallows clicks on the buttons nested inside it, and we want the whole card
        // to be a drag surface anyway.
        RootGrid.PointerPressed += OnPointerPressed;
        RootGrid.PointerMoved += OnPointerMoved;
        RootGrid.PointerReleased += OnPointerReleased;
        RootGrid.PointerCaptureLost += OnPointerCaptureLost;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        _dragOrigin = NativeWindow.CursorPosition();
        _windowOrigin = AppWindow.Position;

        RootGrid.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var cursor = NativeWindow.CursorPosition();

        AppWindow.Move(new PointInt32(
            _windowOrigin.X + (cursor.X - _dragOrigin.X),
            _windowOrigin.Y + (cursor.Y - _dragOrigin.Y)));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        EndDrag();
        RootGrid.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        SavePlacement();
    }

    // ================= animation =================

    private bool AnimationsEnabled
    {
        get
        {
            if (_settings.Current.ReduceMotion)
            {
                return false;
            }

            try
            {
                // Honours the system-wide "Show animations in Windows" setting.
                return _uiSettings.AnimationsEnabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Count):
            case nameof(MainViewModel.Goal):
                UpdateVisuals(animate: true);
                break;

            case nameof(MainViewModel.IsBusy):
                UpdateSpinner();
                break;
        }
    }

    private void UpdateVisuals(bool animate)
    {
        var progress = ViewModel.Progress;

        AnimateProgressBar(progress.Fraction, animate);
        AnimateCount(progress.Contributions, progress.Percent, animate);

        if (!progress.IsComplete)
        {
            FadeCompletionGlow(0, animate);
        }
        else
        {
            FadeCompletionGlow(1, animate);
        }
    }

    private void AnimateProgressBar(double fraction, bool animate)
    {
        if (!animate || !AnimationsEnabled)
        {
            ProgressScale.ScaleX = fraction;
            return;
        }

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = fraction,
            Duration = new Duration(TimeSpan.FromMilliseconds(650)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, ProgressScale);
        Storyboard.SetTargetProperty(animation, "ScaleX");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    /// <summary>
    /// Tweens the headline number so it counts up rather than jumping. Driven by a
    /// timer because the value is text, not an animatable property.
    /// </summary>
    private void AnimateCount(int target, int targetPercent, bool animate)
    {
        _countTimer?.Stop();

        if (!animate || !AnimationsEnabled)
        {
            CountLabel.Text = target.ToString();
            PercentLabel.Text = $"{targetPercent}%";
            return;
        }

        _countFrom = double.TryParse(CountLabel.Text, out var current) ? current : 0;
        _countTo = target;
        _percentFrom = double.TryParse(PercentLabel.Text.TrimEnd('%'), out var currentPercent) ? currentPercent : 0;
        _percentTo = targetPercent;

        if (Math.Abs(_countFrom - _countTo) < 0.5 && Math.Abs(_percentFrom - _percentTo) < 0.5)
        {
            return;
        }

        _countStartedAt = DateTimeOffset.UtcNow;

        _countTimer ??= CreateCountTimer();
        _countTimer.Start();
    }

    private DispatcherTimer CreateCountTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (_, _) =>
        {
            var elapsed = DateTimeOffset.UtcNow - _countStartedAt;
            var t = Math.Clamp(elapsed.TotalMilliseconds / CountDuration.TotalMilliseconds, 0d, 1d);

            // Matches the CubicEase.EaseOut used by the bar so the two move together.
            var eased = 1 - Math.Pow(1 - t, 3);

            CountLabel.Text = ((int)Math.Round(_countFrom + ((_countTo - _countFrom) * eased))).ToString();
            PercentLabel.Text = $"{(int)Math.Round(_percentFrom + ((_percentTo - _percentFrom) * eased))}%";

            if (t >= 1d)
            {
                timer.Stop();
                CountLabel.Text = ((int)_countTo).ToString();
                PercentLabel.Text = $"{(int)_percentTo}%";
            }
        };

        return timer;
    }

    private void FadeCompletionGlow(double opacity, bool animate)
    {
        if (Math.Abs(CompletionGlow.Opacity - opacity) < 0.01)
        {
            return;
        }

        if (!animate || !AnimationsEnabled)
        {
            CompletionGlow.Opacity = opacity;
            return;
        }

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, CompletionGlow);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void UpdateSpinner()
    {
        if (ViewModel.IsBusy && AnimationsEnabled)
        {
            _spinner ??= CreateSpinner();
            _spinner.Begin();
        }
        else
        {
            _spinner?.Stop();
            RefreshRotation.Angle = 0;
        }
    }

    private Storyboard CreateSpinner()
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, RefreshRotation);
        Storyboard.SetTargetProperty(animation, "Angle");
        storyboard.Children.Add(animation);

        return storyboard;
    }

    private void OnGoalJustCompleted()
    {
        if (!AnimationsEnabled)
        {
            return;
        }

        // A single restrained pulse of the card — no confetti.
        var storyboard = new Storyboard();

        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(180),
            Value = 1.02,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(520),
            Value = 1,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });

        GlassCard.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform();
        GlassCard.RenderTransform = scale;

        Storyboard.SetTarget(pulse, scale);
        Storyboard.SetTargetProperty(pulse, "ScaleX");
        storyboard.Children.Add(pulse);

        var pulseY = new DoubleAnimationUsingKeyFrames();
        foreach (var frame in pulse.KeyFrames)
        {
            pulseY.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = frame.KeyTime,
                Value = ((EasingDoubleKeyFrame)frame).Value,
                EasingFunction = ((EasingDoubleKeyFrame)frame).EasingFunction,
            });
        }

        Storyboard.SetTarget(pulseY, scale);
        Storyboard.SetTargetProperty(pulseY, "ScaleY");
        storyboard.Children.Add(pulseY);

        storyboard.Begin();
    }

    // ================= events =================

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            ViewModel.OnActivated();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnHideClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    private async void OnVerificationLinkClick(object sender, RoutedEventArgs e)
    {
        var target = ViewModel.VerificationUri ?? "https://github.com/login/device";

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        SavePlacement();

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.GoalJustCompleted -= OnGoalJustCompleted;
        _countTimer?.Stop();
        _spinner?.Stop();
    }
}
