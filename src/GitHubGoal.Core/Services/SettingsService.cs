using System.Text.Json;
using System.Text.Json.Serialization;
using GitHubGoal.Core.Models;

namespace GitHubGoal.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%\GitHubGoal.
/// Contains no secrets — see <see cref="CredentialService"/> for the access token.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private AppSettings _current = new();

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitHubGoal",
            "settings.json");
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                    if (loaded is not null)
                    {
                        _current = Sanitize(loaded);
                        return _current;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A corrupt or unreadable settings file should never stop the app from
                // starting; fall back to defaults and let the next Save overwrite it.
            }

            _current = new AppSettings();
            return _current;
        }
    }

    public void Save(AppSettings settings)
    {
        var sanitized = Sanitize(settings);

        lock (_gate)
        {
            _current = sanitized;

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a sibling file then move into place, so a crash mid-write cannot
            // leave a half-written settings file behind.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(sanitized, SerializerOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>Clamps values that a hand-edited file could put out of range.</summary>
    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.DailyGoal = Math.Clamp(settings.DailyGoal, GoalProgress.MinimumGoal, 999);
        settings.GlassIntensity = Math.Clamp(settings.GlassIntensity, 0d, 1d);
        settings.RefreshIntervalMinutes = Math.Clamp(settings.RefreshIntervalMinutes, 1, 24 * 60);
        settings.WindowWidth = Math.Clamp(settings.WindowWidth, 300d, 1200d);
        settings.WindowHeight = Math.Clamp(settings.WindowHeight, 180d, 900d);

        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = AppTheme.System;
        }

        if (!Enum.IsDefined(settings.Accent))
        {
            settings.Accent = AccentChoice.GitHubGreen;
        }

        // An unrecognised tier means a downgrade or a hand-edited file; fall back to the
        // tier that grants the least rather than the most.
        if (!Enum.IsDefined(settings.CachedPlan))
        {
            settings.CachedPlan = SubscriptionPlan.Free;
        }

        return settings;
    }
}
