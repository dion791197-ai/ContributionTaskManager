namespace GitHubGoal.Core.Models;

public enum GitHubErrorKind
{
    Unknown,
    NoNetwork,
    Timeout,
    Unauthorized,
    RateLimited,
    ServiceUnavailable,
    MalformedResponse,
    NotConfigured,
    AuthorizationPending,
    AuthorizationDeclined,
    AuthorizationExpired,
}

/// <summary>
/// Transport and API failures, translated into something the UI can phrase for a
/// human. Never carries the access token or raw response bodies.
/// </summary>
public sealed class GitHubException : Exception
{
    public GitHubException(GitHubErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public GitHubErrorKind Kind { get; }

    /// <summary>Short line for the widget footer, e.g. "GitHub connection lost".</summary>
    public string UserMessage => Kind switch
    {
        GitHubErrorKind.NoNetwork => "No internet connection",
        GitHubErrorKind.Timeout => "GitHub is not responding",
        GitHubErrorKind.Unauthorized => "Sign-in expired",
        GitHubErrorKind.RateLimited => "Rate limit reached",
        GitHubErrorKind.ServiceUnavailable => "GitHub is unavailable",
        GitHubErrorKind.MalformedResponse => "Unexpected response from GitHub",
        GitHubErrorKind.NotConfigured => "Not signed in",
        GitHubErrorKind.AuthorizationPending => "Waiting for approval on GitHub",
        GitHubErrorKind.AuthorizationDeclined => "Sign-in was cancelled",
        GitHubErrorKind.AuthorizationExpired => "That code expired — try again",
        _ => "Unable to update",
    };

    /// <summary>Whether retrying on the normal schedule could plausibly succeed.</summary>
    public bool IsTransient => Kind is GitHubErrorKind.NoNetwork
        or GitHubErrorKind.Timeout
        or GitHubErrorKind.ServiceUnavailable
        or GitHubErrorKind.RateLimited;
}
