namespace PublishManager.Core.GitHub;

/// <summary>
/// Supplies a GitHub token. By default it reuses the authenticated <c>gh</c> CLI
/// (<c>gh auth token</c>); a manual PAT can be set as a fallback when gh is absent
/// or logged out. The token is held in memory only — never persisted or logged.
/// </summary>
public interface IGitHubAuthProvider
{
    /// <summary>Returns a token (manual PAT if set, else from <c>gh auth token</c>), or null if none available.</summary>
    Task<string?> GetTokenAsync(CancellationToken ct = default);

    /// <summary>Sets (or clears, with null) a manual PAT fallback for this session.</summary>
    void SetManualToken(string? token);
}
