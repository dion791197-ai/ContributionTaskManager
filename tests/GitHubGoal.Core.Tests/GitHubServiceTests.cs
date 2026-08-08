using System.Net;
using System.Text;
using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using Xunit;

namespace GitHubGoal.Core.Tests;

public class GitHubServiceTests
{
    private const string Token = "gho_testtoken";

    private static readonly TimeZoneInfo Moscow =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    private static GitHubService ServiceReturning(string body, HttpStatusCode status = HttpStatusCode.OK, Action<HttpRequestMessage>? inspect = null)
    {
        var handler = new StubHandler(body, status, inspect);
        return new GitHubService(new HttpClient(handler));
    }

    private static string CalendarJson(params (string Date, int Count)[] days)
    {
        var cells = string.Join(
            ",",
            days.Select(d => "{\"date\":\"" + d.Date + "\",\"contributionCount\":" + d.Count + "}"));

        // Built by concatenation rather than a raw string literal: the closing run of
        // braces here confuses interpolated raw literals.
        return "{\"data\":{\"viewer\":{\"contributionsCollection\":{\"contributionCalendar\":{"
            + "\"totalContributions\":" + days.Sum(d => d.Count) + ","
            + "\"weeks\":[{\"contributionDays\":[" + cells + "]}]"
            + "}}}}}";
    }

    [Fact]
    public async Task Parses_the_contribution_calendar()
    {
        var service = ServiceReturning(CalendarJson(("2026-06-14", 2), ("2026-06-15", 7)));

        var calendar = await service.GetContributionsAsync(Token, DateTimeOffset.Now, DateTimeOffset.Now);

        Assert.Equal(2, calendar.Days.Count);
        Assert.Equal(9, calendar.Total);
        Assert.Equal(7, calendar.CountFor(new DateOnly(2026, 6, 15)));
        Assert.Equal(0, calendar.CountFor(new DateOnly(2026, 6, 16)));
    }

    [Fact]
    public async Task Today_picks_the_cell_matching_the_local_date()
    {
        // 22:00 UTC on the 14th is already the 15th in UTC+3.
        var now = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);
        var service = ServiceReturning(CalendarJson(("2026-06-14", 2), ("2026-06-15", 7)));

        var count = await service.GetTodayContributionsAsync(Token, Moscow, now);

        Assert.Equal(7, count);
    }

    [Fact]
    public async Task Today_sends_local_midnight_boundaries_to_the_api()
    {
        string? capturedBody = null;
        var now = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);

        var service = ServiceReturning(
            CalendarJson(("2026-06-15", 7)),
            inspect: request => capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        await service.GetTodayContributionsAsync(Token, Moscow, now);

        Assert.NotNull(capturedBody);

        // Parsed rather than substring-matched: System.Text.Json escapes '+' as +,
        // which is valid JSON but would defeat a literal comparison.
        using var body = System.Text.Json.JsonDocument.Parse(capturedBody!);
        var variables = body.RootElement.GetProperty("variables");

        // Local midnight on the 15th in UTC+3, not UTC midnight.
        Assert.Equal("2026-06-15T00:00:00.0000000+03:00", variables.GetProperty("from").GetString());
        Assert.Equal("2026-06-15T23:59:59.9999999+03:00", variables.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Missing_day_falls_back_to_the_single_returned_cell()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(3));
        var service = ServiceReturning(CalendarJson(("2026-06-16", 4)));

        Assert.Equal(4, await service.GetTodayContributionsAsync(Token, Moscow, now));
    }

    [Fact]
    public async Task Parses_the_viewer_profile()
    {
        var service = ServiceReturning("""
            {"data":{"viewer":{"login":"octocat","name":"The Octocat","avatarUrl":"https://example.invalid/a.png"}}}
            """);

        var user = await service.GetCurrentUserAsync(Token);

        Assert.Equal("octocat", user.Login);
        Assert.Equal("The Octocat", user.Name);
        Assert.Equal("The Octocat", user.DisplayName);
    }

    [Fact]
    public async Task DisplayName_falls_back_to_login_when_the_profile_has_no_name()
    {
        var service = ServiceReturning("""{"data":{"viewer":{"login":"octocat","name":null,"avatarUrl":null}}}""");

        Assert.Equal("octocat", (await service.GetCurrentUserAsync(Token)).DisplayName);
    }

    [Fact]
    public async Task Sends_a_bearer_token()
    {
        string? auth = null;
        var service = ServiceReturning(
            CalendarJson(("2026-06-15", 1)),
            inspect: request => auth = request.Headers.Authorization?.ToString());

        await service.GetContributionsAsync(Token, DateTimeOffset.Now, DateTimeOffset.Now);

        Assert.Equal($"Bearer {Token}", auth);
    }

    [Fact]
    public async Task Empty_token_is_reported_as_not_configured()
    {
        var service = ServiceReturning("{}");

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(""));

        Assert.Equal(GitHubErrorKind.NotConfigured, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, GitHubErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GitHubErrorKind.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, GitHubErrorKind.ServiceUnavailable)]
    [InlineData((HttpStatusCode)429, GitHubErrorKind.RateLimited)]
    public async Task Http_failures_map_to_friendly_error_kinds(HttpStatusCode status, GitHubErrorKind expected)
    {
        var service = ServiceReturning("{}", status);

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(Token));

        Assert.Equal(expected, ex.Kind);
        Assert.DoesNotContain("Exception", ex.UserMessage);
    }

    [Fact]
    public async Task GraphQl_errors_returned_with_http_200_are_still_errors()
    {
        var service = ServiceReturning("""{"errors":[{"type":"RATE_LIMITED","message":"API rate limit exceeded"}]}""");

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(Token));

        Assert.Equal(GitHubErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task Malformed_json_is_reported_cleanly()
    {
        var service = ServiceReturning("not json at all");

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(Token));

        Assert.Equal(GitHubErrorKind.MalformedResponse, ex.Kind);
    }

    [Fact]
    public async Task A_response_missing_the_data_node_is_malformed()
    {
        var service = ServiceReturning("""{"something":"else"}""");

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(Token));

        Assert.Equal(GitHubErrorKind.MalformedResponse, ex.Kind);
    }

    [Fact]
    public async Task Network_failure_is_reported_as_no_network()
    {
        var service = new GitHubService(new HttpClient(new ThrowingHandler(new HttpRequestException("dns"))));

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.GetCurrentUserAsync(Token));

        Assert.Equal(GitHubErrorKind.NoNetwork, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    private sealed class StubHandler(string body, HttpStatusCode status, Action<HttpRequestMessage>? inspect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
