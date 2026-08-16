namespace GitHubGoal.Core.Services;

/// <summary>
/// Resolves the GitHub OAuth App Client ID the device flow authenticates against.
///
/// A Client ID is not a secret — GitHub's own device-flow docs publish example ones —
/// so it is fine to embed a default here or pass it through an environment variable.
/// This is what lets "Continue with GitHub" work with zero setup for anyone running a
/// build that already has <see cref="DefaultClientId"/> filled in or the environment
/// variable set, while still letting a single user override it from Settings without
/// a rebuild.
/// </summary>
public static class GitHubOAuthConfig
{
    /// <summary>
    /// Baked into the build. Left blank until a GitHub OAuth App has been registered
    /// for this app (github.com/settings/developers, with "Enable Device Flow"
    /// checked) — see the project README for the one-time steps.
    /// </summary>
    public const string DefaultClientId = "";

    /// <summary>
    /// Overrides <see cref="DefaultClientId"/> without a rebuild — useful for pointing
    /// a dev build at a separate OAuth App, or for setting the production ID once per
    /// machine while it is not yet compiled in.
    /// </summary>
    public const string ClientIdEnvironmentVariable = "GITHUBGOAL_OAUTH_CLIENT_ID";

    /// <summary>
    /// The Client ID to use, or null if none is configured anywhere.
    ///
    /// Precedence: an explicit value in Settings (the per-user override already
    /// exposed in the UI) beats the environment variable, which beats the compiled-in
    /// default. Whichever wins, the caller never needs to know which source it came
    /// from.
    /// </summary>
    public static string? Resolve(string? userConfigured)
    {
        if (!string.IsNullOrWhiteSpace(userConfigured))
        {
            return userConfigured.Trim();
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(ClientIdEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return string.IsNullOrWhiteSpace(DefaultClientId) ? null : DefaultClientId;
    }
}
