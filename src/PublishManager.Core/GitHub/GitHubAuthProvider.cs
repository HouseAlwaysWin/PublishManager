using Microsoft.Extensions.Logging;
using PublishManager.Core.Processes;

namespace PublishManager.Core.GitHub;

/// <summary>Default <see cref="IGitHubAuthProvider"/>: manual PAT, else <c>gh auth token</c>.</summary>
public sealed class GitHubAuthProvider(IProcessRunner runner, ILogger<GitHubAuthProvider> logger) : IGitHubAuthProvider
{
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<GitHubAuthProvider> _logger = logger;
    private volatile string? _manualToken;

    public void SetManualToken(string? token) =>
        _manualToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        if (_manualToken is not null)
            return _manualToken;

        try
        {
            var result = await _runner.RunAsync(
                new ProcessRequest { FileName = "gh", Arguments = ["auth", "token"] },
                progress: null,
                ct).ConfigureAwait(false);

            if (result.Success)
            {
                var token = result.StdOutText;
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }

            _logger.LogWarning("`gh auth token` failed (exit {Code}). Run `gh auth login` or set a PAT.", result.ExitCode);
            return null;
        }
        catch (ProcessStartException)
        {
            _logger.LogWarning("gh CLI not found on PATH. Set a PAT to use GitHub features.");
            return null;
        }
    }
}
