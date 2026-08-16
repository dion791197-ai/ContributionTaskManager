using GitHubGoal.Core.Models;

namespace GitHubGoal.Core.Services;

/// <summary>A license as it was last resolved.</summary>
/// <param name="Plan">The tier the license grants.</param>
/// <param name="Key">The raw license key, or null when running unlicensed.</param>
/// <param name="ExpiresAt">When the entitlement lapses; null means it does not.</param>
public sealed record License(SubscriptionPlan Plan, string? Key, DateTimeOffset? ExpiresAt)
{
    public static License Free { get; } = new(SubscriptionPlan.Free, null, null);

    /// <summary>Whether the license is still in force at <paramref name="now"/>.</summary>
    public bool IsValidAt(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}

public interface ILicenseStore
{
    /// <summary>The stored license key, or null if the app has never been activated.</summary>
    string? ReadKey();

    void SaveKey(string key);

    void Clear();
}

/// <summary>
/// Keeps the license key in Windows Credential Manager, next to the GitHub token.
///
/// A license key is a secret in the same sense an access token is — it is transferable
/// and worth money — so it gets the same treatment rather than sitting in settings.json.
/// </summary>
public sealed class CredentialLicenseStore : ILicenseStore
{
    /// <summary>Credential Manager key for the license.</summary>
    public const string LicenseTarget = "GitHubGoal:LicenseKey";

    private readonly ICredentialService _credentials;

    public CredentialLicenseStore(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public string? ReadKey()
    {
        try
        {
            return _credentials.Read(LicenseTarget);
        }
        catch (InvalidOperationException)
        {
            // An unreadable credential store should leave the app running on Free, not
            // crash it on startup.
            return null;
        }
    }

    public void SaveKey(string key)
    {
        _credentials.Write(LicenseTarget, "license", key);
    }

    public void Clear()
    {
        try
        {
            _credentials.Delete(LicenseTarget);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
