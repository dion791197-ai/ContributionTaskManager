using Microsoft.Win32;

namespace GitHubGoal.Core.Services;

public interface IStartupService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>
/// Launch-at-login for an unpackaged desktop app.
///
/// Writes the per-user Run key, which is the mechanism Windows itself provides for
/// unpackaged apps (the packaged StartupTask API requires package identity). It shows
/// up in Task Manager's Startup tab, so the user can disable it from there too.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GitHubGoal";

    private readonly string _executablePath;

    public StartupService(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string existing && existing.Contains(ValueName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (string.IsNullOrEmpty(_executablePath))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                // Quoted so a path containing spaces still parses, and flagged so the app
                // knows to start minimised to the tray rather than popping the widget open.
                key.SetValue(ValueName, $"\"{_executablePath}\" --startup", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Group policy can lock the Run key; treat as a no-op rather than crashing.
        }
    }
}
