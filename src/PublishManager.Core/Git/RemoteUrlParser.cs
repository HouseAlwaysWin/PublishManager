using System.Diagnostics.CodeAnalysis;

namespace PublishManager.Core.Git;

/// <summary>
/// Parses the owner/repo out of a git remote URL. Handles the common GitHub
/// forms: HTTPS, SCP-like SSH (<c>git@host:owner/repo.git</c>), and
/// <c>ssh://</c> URLs, with or without a trailing <c>.git</c>.
/// Pure and side-effect free — unit-tested in isolation.
/// </summary>
public static class RemoteUrlParser
{
    public static bool TryParse(string? url, out RepoSlug slug)
    {
        slug = default;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var trimmed = url.Trim();

        // SCP-like syntax: git@github.com:owner/repo(.git)
        // Distinguished from ssh:// URLs by the "host:path" colon with no scheme.
        if (!trimmed.Contains("://") && trimmed.Contains(':'))
        {
            var colon = trimmed.IndexOf(':');
            var path = trimmed[(colon + 1)..];
            return TryParseOwnerRepo(path, out slug);
        }

        // Scheme-based: https://host/owner/repo(.git), ssh://git@host/owner/repo(.git)
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return TryParseOwnerRepo(uri.AbsolutePath, out slug);

        return false;
    }

    private static bool TryParseOwnerRepo(string path, out RepoSlug slug)
    {
        slug = default;

        var segments = path
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 2)
            return false;

        // owner is second-to-last, repo is last (handles nested paths defensively).
        var owner = segments[^2];
        var repo = StripGitSuffix(segments[^1]);

        if (owner.Length == 0 || repo.Length == 0)
            return false;

        slug = new RepoSlug(owner, repo);
        return true;
    }

    private static string StripGitSuffix(string repo) =>
        repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repo[..^4]
            : repo;
}
