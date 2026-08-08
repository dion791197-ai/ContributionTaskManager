using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GitHubGoal.Core.Models;
using GitHubGoal.Core.Utilities;

namespace GitHubGoal.Core.Services;

public interface IGitHubService
{
    Task<GitHubUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken = default);

    Task<ContributionCalendar> GetContributionsAsync(
        string token,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<int> GetTodayContributionsAsync(
        string token,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to GitHub's GraphQL API.
///
/// The contribution count is only exposed through GraphQL — there is no REST endpoint
/// for it — via viewer.contributionsCollection.contributionCalendar. The REST events
/// API is not a substitute: it omits private contributions and is heavily cached.
/// </summary>
public sealed class GitHubService : IGitHubService
{
    private const string GraphQlUrl = "https://api.github.com/graphql";

    // contributionsCollection buckets days using the offset carried by $from/$to, so
    // passing local-midnight boundaries makes the returned calendar align with the
    // user's local calendar rather than UTC.
    private const string ContributionsQuery = """
        query($from: DateTime!, $to: DateTime!) {
          viewer {
            contributionsCollection(from: $from, to: $to) {
              contributionCalendar {
                totalContributions
                weeks {
                  contributionDays {
                    date
                    contributionCount
                  }
                }
              }
            }
          }
        }
        """;

    private const string ViewerQuery = """
        query {
          viewer {
            login
            name
            avatarUrl
          }
        }
        """;

    private readonly HttpClient _http;

    public GitHubService(HttpClient http)
    {
        _http = http;
    }

    public async Task<GitHubUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken = default)
    {
        using var document = await ExecuteAsync(token, ViewerQuery, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var viewer = document.RootElement.GetProperty("data").GetProperty("viewer");

            return new GitHubUser(
                viewer.GetProperty("login").GetString() ?? throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned a user with no login."),
                viewer.TryGetProperty("name", out var name) ? name.GetString() : null,
                viewer.TryGetProperty("avatarUrl", out var avatar) ? avatar.GetString() : null);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned an unexpected profile shape.", ex);
        }
    }

    public async Task<ContributionCalendar> GetContributionsAsync(
        string token,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, object?>
        {
            // "o" round-trips with the offset intact, which is the part that matters.
            ["from"] = from.ToString("o"),
            ["to"] = to.ToString("o"),
        };

        using var document = await ExecuteAsync(token, ContributionsQuery, variables, cancellationToken).ConfigureAwait(false);

        try
        {
            var calendar = document.RootElement
                .GetProperty("data")
                .GetProperty("viewer")
                .GetProperty("contributionsCollection")
                .GetProperty("contributionCalendar");

            var total = calendar.GetProperty("totalContributions").GetInt32();
            var days = new List<ContributionDay>();

            foreach (var week in calendar.GetProperty("weeks").EnumerateArray())
            {
                foreach (var day in week.GetProperty("contributionDays").EnumerateArray())
                {
                    var raw = day.GetProperty("date").GetString();
                    if (DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var date))
                    {
                        days.Add(new ContributionDay(date, day.GetProperty("contributionCount").GetInt32()));
                    }
                }
            }

            return new ContributionCalendar(days, total);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned an unexpected contribution shape.", ex);
        }
    }

    public async Task<int> GetTodayContributionsAsync(
        string token,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = LocalDay.Bounds(now, zone);
        var calendar = await GetContributionsAsync(token, from, to, cancellationToken).ConfigureAwait(false);

        // Prefer the cell matching today's local date. GitHub can return adjacent days
        // when the range straddles its own bucket boundary, so we do not just take the
        // total — except when the calendar came back with a single day, which is the
        // normal case for a one-day range.
        var today = LocalDay.DateFor(now, zone);
        var match = calendar.Days.FirstOrDefault(d => d.Date == today);

        if (match is not null)
        {
            return match.Count;
        }

        return calendar.Days.Count == 1 ? calendar.Days[0].Count : calendar.Total;
    }

    private async Task<JsonDocument> ExecuteAsync(
        string token,
        string query,
        Dictionary<string, object?>? variables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new GitHubException(GitHubErrorKind.NotConfigured, "No GitHub access token is available.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException(GitHubErrorKind.NoNetwork, "Could not reach GitHub.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubException(GitHubErrorKind.Timeout, "The request to GitHub timed out.", ex);
        }

        using (response)
        {
            ThrowForStatus(response);

            JsonDocument document;
            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned an unreadable response.", ex);
            }

            // GraphQL reports failures in the body with HTTP 200, so errors must be
            // inspected even on success.
            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                var type = first.TryGetProperty("type", out var t) ? t.GetString() : null;
                var detail = first.TryGetProperty("message", out var m) ? m.GetString() : null;

                document.Dispose();

                throw type switch
                {
                    "RATE_LIMITED" => new GitHubException(GitHubErrorKind.RateLimited, "GitHub rate limit reached."),
                    "FORBIDDEN" or "UNAUTHORIZED" => new GitHubException(GitHubErrorKind.Unauthorized, "The saved sign-in is no longer valid."),
                    _ => new GitHubException(GitHubErrorKind.Unknown, detail ?? "GitHub reported an error."),
                };
            }

            if (!document.RootElement.TryGetProperty("data", out _))
            {
                document.Dispose();
                throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned no data.");
            }

            return document;
        }
    }

    private static void ThrowForStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new GitHubException(GitHubErrorKind.Unauthorized, "The saved sign-in is no longer valid."),

            // GitHub returns 403 for both permission problems and secondary rate limits;
            // the retry headers disambiguate.
            HttpStatusCode.Forbidden when HasExhaustedRateLimit(response) =>
                new GitHubException(GitHubErrorKind.RateLimited, "GitHub rate limit reached."),
            HttpStatusCode.Forbidden => new GitHubException(GitHubErrorKind.Unauthorized, "GitHub refused the request."),

            (HttpStatusCode)429 => new GitHubException(GitHubErrorKind.RateLimited, "GitHub rate limit reached."),
            HttpStatusCode.RequestTimeout => new GitHubException(GitHubErrorKind.Timeout, "The request to GitHub timed out."),

            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError =>
                new GitHubException(GitHubErrorKind.ServiceUnavailable, "GitHub is temporarily unavailable."),

            _ => new GitHubException(GitHubErrorKind.Unknown, $"GitHub returned {(int)response.StatusCode}."),
        };
    }

    private static bool HasExhaustedRateLimit(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
        && int.TryParse(values.FirstOrDefault(), out var remaining)
        && remaining == 0;
}
