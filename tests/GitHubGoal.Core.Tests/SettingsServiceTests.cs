using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using Xunit;

namespace GitHubGoal.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public SettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GitHubGoalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void Load_returns_defaults_when_no_file_exists()
    {
        var settings = new SettingsService(_path).Load();

        Assert.Equal(10, settings.DailyGoal);
        Assert.True(settings.AlwaysOnTop);
        Assert.True(settings.AutoRefresh);
        Assert.Equal(5, settings.RefreshIntervalMinutes);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Values_survive_a_save_and_reload()
    {
        var service = new SettingsService(_path);
        var settings = service.Load();

        settings.DailyGoal = 15;
        settings.Theme = AppTheme.Dark;
        settings.Accent = AccentChoice.Custom;
        settings.CustomAccentHex = "#FF8800";
        settings.AlwaysOnTop = false;
        settings.GlassIntensity = 0.75;
        settings.WindowX = 120;
        settings.WindowY = 340;
        settings.OAuthClientId = "Iv1.abc123";
        service.Save(settings);

        var reloaded = new SettingsService(_path).Load();

        Assert.Equal(15, reloaded.DailyGoal);
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal(AccentChoice.Custom, reloaded.Accent);
        Assert.Equal("#FF8800", reloaded.CustomAccentHex);
        Assert.False(reloaded.AlwaysOnTop);
        Assert.Equal(0.75, reloaded.GlassIntensity, precision: 6);
        Assert.Equal(120, reloaded.WindowX);
        Assert.Equal(340, reloaded.WindowY);
        Assert.Equal("Iv1.abc123", reloaded.OAuthClientId);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_on_load()
    {
        File.WriteAllText(_path, """
            {
              "DailyGoal": -5,
              "GlassIntensity": 9.5,
              "RefreshIntervalMinutes": 0,
              "WindowWidth": 10,
              "WindowHeight": 99999
            }
            """);

        var settings = new SettingsService(_path).Load();

        Assert.Equal(GoalProgress.MinimumGoal, settings.DailyGoal);
        Assert.Equal(1d, settings.GlassIntensity, precision: 6);
        Assert.Equal(1, settings.RefreshIntervalMinutes);
        Assert.Equal(300d, settings.WindowWidth, precision: 6);
        Assert.Equal(900d, settings.WindowHeight, precision: 6);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        File.WriteAllText(_path, "{ this is not json");

        var settings = new SettingsService(_path).Load();

        Assert.Equal(10, settings.DailyGoal);
    }

    [Fact]
    public void Unknown_enum_values_fall_back_to_defaults()
    {
        File.WriteAllText(_path, """{ "Theme": "Neon", "Accent": "Chartreuse" }""");

        var settings = new SettingsService(_path).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(AccentChoice.GitHubGreen, settings.Accent);
    }

    [Fact]
    public void Saved_file_never_contains_a_token_field()
    {
        var service = new SettingsService(_path);
        var settings = service.Load();
        settings.LastKnownLogin = "octocat";
        service.Save(settings);

        var json = File.ReadAllText(_path);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var nested = Path.Combine(_directory, "a", "b", "settings.json");
        var service = new SettingsService(nested);

        service.Save(service.Load());

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var service = new SettingsService(_path);
        service.Save(service.Load());

        Assert.False(File.Exists(_path + ".tmp"));
    }
}
