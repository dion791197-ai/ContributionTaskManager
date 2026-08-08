using GitHubGoal.Core.Models;
using GitHubGoal.Core.Utilities;

namespace GitHubGoal.Core.Services;

/// <summary>Outcome of a refresh, including the degraded cases.</summary>
public sealed record RefreshResult(ContributionSnapshot? Snapshot, GitHubException? Error)
{
    public bool Succeeded => Error is null;
}

public interface IContributionService
{
    bool IsSignedIn { get; }

    GitHubUser? CurrentUser { get; }

    Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Cached value from a previous run, so the widget is populated at startup.</summary>
    ContributionSnapshot? LoadCachedSnapshot();

    void SignOut();
}

/// <summary>
/// Coordinates the token store, the GitHub API and the offline cache, so the view
/// model only ever deals with a snapshot or a friendly error.
/// </summary>
public sealed class ContributionService : IContributionService
{
    private readonly IGitHubService _gitHub;
    private readonly ICredentialService _credentials;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly Func<TimeZoneInfo> _zoneProvider;

    public ContributionService(
        IGitHubService gitHub,
        ICredentialService credentials,
        ISettingsService settings,
        TimeProvider? time = null,
        Func<TimeZoneInfo>? zoneProvider = null)
    {
        _gitHub = gitHub;
        _credentials = credentials;
        _settings = settings;
        _time = time ?? TimeProvider.System;

        // Resolved per call rather than cached: Windows can change time zone while the
        // widget is running, and TimeZoneInfo.Local caches until it is cleared.
        _zoneProvider = zoneProvider ?? (() =>
        {
            TimeZoneInfo.ClearCachedData();
            return TimeZoneInfo.Local;
        });
    }

    public bool IsSignedIn => !string.IsNullOrEmpty(ReadToken());

    public GitHubUser? CurrentUser { get; private set; }

    public ContributionSnapshot? LoadCachedSnapshot()
    {
        var settings = _settings.Current;

        if (settings.CachedCount is not { } count
            || settings.CachedAt is not { } cachedAt
            || !DateOnly.TryParse(settings.CachedDate, System.Globalization.CultureInfo.InvariantCulture, out var cachedDate))
        {
            return null;
        }

        // A cache from an earlier day says nothing about today's progress.
        if (cachedDate != LocalDay.DateFor(_time.GetUtcNow(), _zoneProvider()))
        {
            return null;
        }

        if (settings.LastKnownLogin is { Length: > 0 } login)
        {
            CurrentUser = new GitHubUser(login, settings.LastKnownName, settings.LastKnownAvatarUrl);
        }

        return new ContributionSnapshot(count, cachedDate, cachedAt) { IsStale = true };
    }

    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token))
        {
            return new RefreshResult(null, new GitHubException(GitHubErrorKind.NotConfigured, "Not signed in."));
        }

        var zone = _zoneProvider();
        var now = _time.GetLocalNow();

        try
        {
            // Refresh the profile only when we have not resolved it yet; it rarely
            // changes and every extra call eats the same rate limit as the counts.
            if (CurrentUser is null)
            {
                CurrentUser = await _gitHub.GetCurrentUserAsync(token, cancellationToken).ConfigureAwait(false);
            }

            var count = await _gitHub.GetTodayContributionsAsync(token, zone, now, cancellationToken).ConfigureAwait(false);

            // Re-derive the date after the call: a refresh that starts at 23:59:59 and
            // returns at 00:00:01 must be filed under the day it actually describes.
            var localDate = LocalDay.DateFor(_time.GetUtcNow(), zone);
            var snapshot = new ContributionSnapshot(count, localDate, _time.GetUtcNow());

            CacheSnapshot(snapshot);
            return new RefreshResult(snapshot, null);
        }
        catch (GitHubException ex)
        {
            if (ex.Kind == GitHubErrorKind.Unauthorized)
            {
                // The stored token is dead; drop it so the UI can prompt to sign in again.
                SignOut();
            }

            return new RefreshResult(LoadCachedSnapshot(), ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RefreshResult(
                LoadCachedSnapshot(),
                new GitHubException(GitHubErrorKind.Unknown, "Something went wrong while updating.", ex));
        }
    }

    public void SignOut()
    {
        CurrentUser = null;

        try
        {
            _credentials.Delete(CredentialService.GitHubTokenTarget);
        }
        catch (InvalidOperationException)
        {
            // Nothing useful to do if the credential store refuses; the settings clear below still applies.
        }

        var settings = _settings.Current;
        settings.LastKnownLogin = null;
        settings.LastKnownName = null;
        settings.LastKnownAvatarUrl = null;
        settings.CachedCount = null;
        settings.CachedDate = null;
        settings.CachedAt = null;
        _settings.Save(settings);
    }

    private void CacheSnapshot(ContributionSnapshot snapshot)
    {
        var settings = _settings.Current;
        settings.CachedCount = snapshot.Count;
        settings.CachedDate = snapshot.LocalDate.ToString("O");
        settings.CachedAt = snapshot.FetchedAt;
        settings.LastKnownLogin = CurrentUser?.Login;
        settings.LastKnownName = CurrentUser?.Name;
        settings.LastKnownAvatarUrl = CurrentUser?.AvatarUrl;
        _settings.Save(settings);
    }

    private string? ReadToken()
    {
        try
        {
            return _credentials.Read(CredentialService.GitHubTokenTarget);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
