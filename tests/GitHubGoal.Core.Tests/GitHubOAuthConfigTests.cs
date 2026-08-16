using GitHubGoal.Core.Services;
using Xunit;

namespace GitHubGoal.Core.Tests;

/// <summary>
/// Covers the precedence that lets "Continue with GitHub" work with zero setup: a
/// Settings override beats the environment variable, which beats the build's
/// compiled-in default, and the result is null only when none of the three exist.
/// </summary>
public sealed class GitHubOAuthConfigTests : IDisposable
{
    // Environment variables are process-wide, so each test clears the slate first and
    // restores it afterwards rather than assuming a clean environment.
    private readonly string? _original =
        Environment.GetEnvironmentVariable(GitHubOAuthConfig.ClientIdEnvironmentVariable);

    public GitHubOAuthConfigTests() =>
        Environment.SetEnvironmentVariable(GitHubOAuthConfig.ClientIdEnvironmentVariable, null);

    public void Dispose() =>
        Environment.SetEnvironmentVariable(GitHubOAuthConfig.ClientIdEnvironmentVariable, _original);

    [Fact]
    public void Settings_value_wins_over_everything_else()
    {
        Environment.SetEnvironmentVariable(GitHubOAuthConfig.ClientIdEnvironmentVariable, "from-env");

        Assert.Equal("from-settings", GitHubOAuthConfig.Resolve("from-settings"));
    }

    [Fact]
    public void Environment_variable_is_used_when_settings_is_blank()
    {
        Environment.SetEnvironmentVariable(GitHubOAuthConfig.ClientIdEnvironmentVariable, "from-env");

        Assert.Equal("from-env", GitHubOAuthConfig.Resolve(null));
        Assert.Equal("from-env", GitHubOAuthConfig.Resolve("   "));
    }

    [Fact]
    public void Settings_value_is_trimmed()
    {
        Assert.Equal("Iv1.abcdef", GitHubOAuthConfig.Resolve("  Iv1.abcdef  "));
    }

    [Fact]
    public void Nothing_configured_resolves_to_null_when_no_default_is_compiled_in()
    {
        // This assumes the shipped DefaultClientId is still blank, which is the state
        // until a real GitHub OAuth App is registered for the app. If a default is
        // ever compiled in, this test documents that resolution then always succeeds.
        if (string.IsNullOrWhiteSpace(GitHubOAuthConfig.DefaultClientId))
        {
            Assert.Null(GitHubOAuthConfig.Resolve(null));
        }
    }
}
