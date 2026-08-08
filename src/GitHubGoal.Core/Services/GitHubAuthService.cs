using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GitHubGoal.Core.Models;

namespace GitHubGoal.Core.Services;

/// <summary>What to show the user while they authorise the app.</summary>
public sealed record DeviceCodeRequest(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    TimeSpan Interval,
    DateTimeOffset ExpiresAt);

public interface IGitHubAuthService
{
    /// <summary>Starts the device flow and returns the code to display.</summary>
    Task<DeviceCodeRequest> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls until the user approves, declines, or the code expires. Returns the
    /// access token on success.
    /// </summary>
    Task<string> WaitForAccessTokenAsync(string clientId, DeviceCodeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// GitHub OAuth device flow.
///
/// Chosen over the browser-redirect flow because that one requires a client secret to
/// exchange the code, and a secret shipped inside a desktop binary is not a secret.
/// The device flow needs only the (public) client ID.
/// </summary>
public sealed class GitHubAuthService : IGitHubAuthService
{
    /// <summary>Enough to read the profile and the contribution calendar.</summary>
    public const string Scopes = "read:user";

    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";

    private readonly HttpClient _http;

    public GitHubAuthService(HttpClient http)
    {
        _http = http;
    }

    public async Task<DeviceCodeRequest> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new GitHubException(GitHubErrorKind.NotConfigured, "No OAuth client ID has been configured.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = Scopes,
            }),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = await SendAsync<DeviceCodeResponse>(message, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(payload.DeviceCode) || string.IsNullOrEmpty(payload.UserCode))
        {
            throw new GitHubException(
                GitHubErrorKind.MalformedResponse,
                "GitHub did not return a device code. Check that the client ID belongs to an OAuth App with device flow enabled.");
        }

        return new DeviceCodeRequest(
            payload.DeviceCode,
            payload.UserCode,
            string.IsNullOrEmpty(payload.VerificationUri) ? "https://github.com/login/device" : payload.VerificationUri,
            TimeSpan.FromSeconds(Math.Max(5, payload.Interval)),
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn <= 0 ? 900 : payload.ExpiresIn));
    }

    public async Task<string> WaitForAccessTokenAsync(string clientId, DeviceCodeRequest request, CancellationToken cancellationToken = default)
    {
        var interval = request.Interval;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= request.ExpiresAt)
            {
                throw new GitHubException(GitHubErrorKind.AuthorizationExpired, "The device code expired before it was approved.");
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            using var message = new HttpRequestMessage(HttpMethod.Post, AccessTokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = request.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }),
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = await SendAsync<AccessTokenResponse>(message, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(payload.AccessToken))
            {
                return payload.AccessToken;
            }

            switch (payload.Error)
            {
                case "authorization_pending":
                    // Expected: the user has not finished approving yet.
                    break;

                case "slow_down":
                    // GitHub asks us to back off; honour the interval it supplies.
                    interval = TimeSpan.FromSeconds(Math.Max(interval.TotalSeconds + 5, payload.Interval));
                    break;

                case "expired_token":
                    throw new GitHubException(GitHubErrorKind.AuthorizationExpired, "The device code expired before it was approved.");

                case "access_denied":
                    throw new GitHubException(GitHubErrorKind.AuthorizationDeclined, "Authorization was declined on GitHub.");

                default:
                    throw new GitHubException(
                        GitHubErrorKind.Unknown,
                        $"GitHub rejected the authorization request ({payload.Error ?? "unknown error"}).");
            }
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken cancellationToken)
        where T : new()
    {
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
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
            {
                throw new GitHubException(GitHubErrorKind.ServiceUnavailable, "GitHub is temporarily unavailable.");
            }

            try
            {
                var payload = await response.Content
                    .ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return payload ?? new T();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new GitHubException(GitHubErrorKind.MalformedResponse, "GitHub returned an unreadable response.", ex);
            }
        }
    }

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string? DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public string? UserCode { get; set; }
        [JsonPropertyName("verification_uri")] public string? VerificationUri { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private sealed class AccessTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }
}
