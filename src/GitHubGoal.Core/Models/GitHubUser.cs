namespace GitHubGoal.Core.Models;

/// <summary>The signed-in account, as shown in the widget header.</summary>
public sealed record GitHubUser(string Login, string? Name, string? AvatarUrl)
{
    /// <summary>Display name if the profile has one, otherwise the login handle.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name!;
}
