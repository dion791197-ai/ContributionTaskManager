using GitHubGoal.Core.Models;
using Xunit;

namespace GitHubGoal.Core.Tests;

/// <summary>
/// Every <see cref="GitHubErrorKind"/> the device-flow sign-in can actually throw must
/// have a specific <see cref="GitHubException.UserMessage"/> — falling through to the
/// generic "Unable to update" during sign-in reads as if the whole app broke rather
/// than "you cancelled" or "that code expired".
/// </summary>
public sealed class GitHubExceptionTests
{
    [Theory]
    [InlineData(GitHubErrorKind.AuthorizationPending)]
    [InlineData(GitHubErrorKind.AuthorizationDeclined)]
    [InlineData(GitHubErrorKind.AuthorizationExpired)]
    [InlineData(GitHubErrorKind.NoNetwork)]
    [InlineData(GitHubErrorKind.Timeout)]
    [InlineData(GitHubErrorKind.Unauthorized)]
    [InlineData(GitHubErrorKind.RateLimited)]
    [InlineData(GitHubErrorKind.ServiceUnavailable)]
    [InlineData(GitHubErrorKind.MalformedResponse)]
    [InlineData(GitHubErrorKind.NotConfigured)]
    public void Every_sign_in_relevant_kind_has_its_own_message(GitHubErrorKind kind)
    {
        var message = new GitHubException(kind, "internal detail").UserMessage;

        Assert.NotEqual("Unable to update", message);
        Assert.NotEmpty(message);
    }

    [Fact]
    public void Declined_and_expired_read_differently()
    {
        var declined = new GitHubException(GitHubErrorKind.AuthorizationDeclined, string.Empty).UserMessage;
        var expired = new GitHubException(GitHubErrorKind.AuthorizationExpired, string.Empty).UserMessage;

        Assert.NotEqual(declined, expired);
    }

    [Fact]
    public void Unrecognised_kinds_still_fall_back_to_a_readable_message()
    {
        var message = new GitHubException(GitHubErrorKind.Unknown, "internal detail").UserMessage;

        Assert.Equal("Unable to update", message);
    }
}
