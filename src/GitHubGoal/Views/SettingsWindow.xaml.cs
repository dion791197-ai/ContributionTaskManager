using GitHubGoal.Interop;
using GitHubGoal.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GitHubGoal.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        Title = "GitHub Goal — Settings";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        NativeWindow.ApplyRoundedCorners(WinRT.Interop.WindowNative.GetWindowHandle(this));

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        CenterOnScreen(460, 660);
    }

    public SettingsViewModel ViewModel { get; }

    private void CenterOnScreen(int logicalWidth, int logicalHeight)
    {
        var scale = NativeWindow.ScaleFor(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var width = (int)Math.Round(logicalWidth * scale);
        var height = (int)Math.Round(logicalHeight * scale);

        var work = NativeWindow.PrimaryWorkArea();

        // Clamp so the window still fits on a short screen.
        height = Math.Min(height, work.Height - 40);

        AppWindow.MoveAndResize(new RectInt32(
            work.Left + ((work.Width - width) / 2),
            work.Top + ((work.Height - height) / 2),
            width,
            height));
    }

    private void OnGoalPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: int preset })
        {
            ViewModel.DailyGoal = preset;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
