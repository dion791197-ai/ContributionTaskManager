using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using Xunit;

namespace GitHubGoal.Core.Tests;

public sealed class ContributionServiceTests : IDisposable
{
    private static readonly TimeZoneInfo Moscow =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    private readonly string _directory;
    private readonly SettingsService _settings;
    private readonly FakeCredentials _credentials = new();

    public ContributionServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GitHubGoalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new SettingsService(Path.Combine(_directory, "settings.json"));
        _settings.Load();
        _credentials.Write(CredentialService.GitHubTokenTarget, "octocat", "gho_token");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ContributionService Build(IGitHubService gitHub, DateTimeOffset now) =>
        new(gitHub, _credentials, _settings, new FixedTime(now), () => Moscow);

    [Fact]
    public async Task A_successful_refresh_returns_and_caches_the_count()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var service = Build(new FakeGitHub { Count = 7 }, now);

        var result = await service.RefreshAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(7, result.Snapshot!.Count);
        Assert.False(result.Snapshot.IsStale);
        Assert.Equal(new DateOnly(2026, 6, 15), result.Snapshot.LocalDate);
        Assert.Equal(7, _settings.Current.CachedCount);
        Assert.Equal("octocat", _settings.Current.LastKnownLogin);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_showing_the_cached_value_as_stale()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        await Build(new FakeGitHub { Count = 7 }, now).RefreshAsync();

        var offline = Build(new FakeGitHub { Throw = new GitHubException(GitHubErrorKind.NoNetwork, "offline") }, now.AddMinutes(5));
        var result = await offline.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubErrorKind.NoNetwork, result.Error!.Kind);
        Assert.Equal("No internet connection", result.Error.UserMessage);

        // The number stays on screen rather than blanking out.
        Assert.NotNull(result.Snapshot);
        Assert.Equal(7, result.Snapshot!.Count);
        Assert.True(result.Snapshot.IsStale);
    }

    [Fact]
    public void Cache_from_a_previous_day_is_not_reused()
    {
        var yesterday = new DateTimeOffset(2026, 6, 14, 9, 0, 0, TimeSpan.Zero);
        var settings = _settings.Current;
        settings.CachedCount = 12;
        settings.CachedDate = new DateOnly(2026, 6, 14).ToString("O");
        settings.CachedAt = yesterday;
        _settings.Save(settings);

        var today = Build(new FakeGitHub(), new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

        Assert.Null(today.LoadCachedSnapshot());
    }

    [Fact]
    public void Cache_is_keyed_to_the_local_day_not_the_utc_day()
    {
        // 21:00 UTC on the 14th is already the 15th in UTC+3, so a cache written for
        // the 15th must still be considered current.
        var settings = _settings.Current;
        settings.CachedCount = 5;
        settings.CachedDate = new DateOnly(2026, 6, 15).ToString("O");
        settings.CachedAt = new DateTimeOffset(2026, 6, 14, 21, 5, 0, TimeSpan.Zero);
        _settings.Save(settings);

        var service = Build(new FakeGitHub(), new DateTimeOffset(2026, 6, 14, 21, 30, 0, TimeSpan.Zero));

        var cached = service.LoadCachedSnapshot();

        Assert.NotNull(cached);
        Assert.Equal(5, cached!.Count);
    }

    [Fact]
    public async Task An_expired_token_signs_the_user_out()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var service = Build(new FakeGitHub { Throw = new GitHubException(GitHubErrorKind.Unauthorized, "bad token") }, now);

        Assert.True(service.IsSignedIn);

        var result = await service.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.False(service.IsSignedIn);
        Assert.Null(_credentials.Read(CredentialService.GitHubTokenTarget));
    }

    [Fact]
    public async Task Refreshing_without_a_token_reports_not_configured()
    {
        _credentials.Delete(CredentialService.GitHubTokenTarget);
        var service = Build(new FakeGitHub(), DateTimeOffset.UtcNow);

        var result = await service.RefreshAsync();

        Assert.Equal(GitHubErrorKind.NotConfigured, result.Error!.Kind);
    }

    [Fact]
    public void Signing_out_clears_the_token_and_the_cached_identity()
    {
        var settings = _settings.Current;
        settings.LastKnownLogin = "octocat";
        settings.CachedCount = 7;
        _settings.Save(settings);

        Build(new FakeGitHub(), DateTimeOffset.UtcNow).SignOut();

        Assert.Null(_credentials.Read(CredentialService.GitHubTokenTarget));
        Assert.Null(_settings.Current.LastKnownLogin);
        Assert.Null(_settings.Current.CachedCount);
    }

    private sealed class FakeGitHub : IGitHubService
    {
        public int Count { get; set; }

        public GitHubException? Throw { get; set; }

        public Task<GitHubUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken = default) =>
            Throw is not null
                ? Task.FromException<GitHubUser>(Throw)
                : Task.FromResult(new GitHubUser("octocat", "The Octocat", null));

        public Task<ContributionCalendar> GetContributionsAsync(string token, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Throw is not null
                ? Task.FromException<ContributionCalendar>(Throw)
                : Task.FromResult(ContributionCalendar.Empty);

        public Task<int> GetTodayContributionsAsync(string token, TimeZoneInfo zone, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Throw is not null ? Task.FromException<int>(Throw) : Task.FromResult(Count);
    }

    private sealed class FakeCredentials : ICredentialService
    {
        private readonly Dictionary<string, string> _store = new();

        public string? Read(string target) => _store.GetValueOrDefault(target);

        public void Write(string target, string userName, string secret) => _store[target] = secret;

        public void Delete(string target) => _store.Remove(target);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => Moscow;
    }
}
