using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GitHubGoal;

/// <summary>
/// Custom entry point (replaces the XAML-generated Main via DISABLE_XAML_GENERATED_MAIN).
/// WinUI startup failures surface as stowed COM exceptions with no console output, so
/// everything here is wrapped and written to a log file next to the executable.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            Application.Start(initParams =>
            {
                var queue = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(queue));
                _ = new App();
            });

            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup", ex);
            return 1;
        }
    }
}

/// <summary>
/// Appends startup and background failures to a log beside the executable. WinUI
/// swallows a lot of these, so without it they are invisible.
/// </summary>
internal static class CrashLog
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "startup-error.log");

    public static void Write(string stage, Exception? ex)
    {
        try
        {
            var text = $"""
                ===== {DateTimeOffset.Now:O} [{stage}] =====
                {ex}

                """;
            File.AppendAllText(LogPath, text);
        }
        catch
        {
            // Logging must never mask the original failure.
        }
    }
}
