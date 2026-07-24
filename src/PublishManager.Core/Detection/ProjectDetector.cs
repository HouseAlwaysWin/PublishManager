using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Git;
using PublishManager.Core.Models;

namespace PublishManager.Core.Detection;

/// <summary>Default <see cref="IProjectDetector"/>: filesystem probing + git queries.</summary>
public sealed partial class ProjectDetector(IGitService git, ILogger<ProjectDetector> logger) : IProjectDetector
{
    private static readonly string[] DotNetPatterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj"];
    private static readonly string[] SkipDirectories = ["bin", "obj", ".git", "node_modules", ".vs", ".idea"];

    private readonly IGitService _git = git;
    private readonly ILogger<ProjectDetector> _logger = logger;

    public async Task<ProjectDetection> DetectAsync(string localPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            return new ProjectDetection();

        var root = localPath.Trim();
        var kind = ContainsFile(root, DotNetPatterns) ? ProjectKind.DotNet : ProjectKind.Script;
        var workflows = ReadWorkflows(root);
        var suggested = PickWorkflow(workflows);

        var isRepo = false;
        RepoSlug? slug = null;
        string? branch = null;
        string? tagPrefix = null;

        try
        {
            isRepo = await _git.IsGitRepositoryAsync(root, ct).ConfigureAwait(false);
            if (isRepo)
            {
                slug = await _git.GetRemoteSlugAsync(root, ct: ct).ConfigureAwait(false);
                branch = await _git.GetCurrentBranchAsync(root, ct).ConfigureAwait(false);
                tagPrefix = InferTagPrefix(await _git.ListTagsAsync(root, ct).ConfigureAwait(false));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Git-based detection failed for {Path}.", root);
        }

        return new ProjectDetection
        {
            IsGitRepository = isRepo,
            Kind = kind,
            Slug = slug,
            CurrentBranch = branch,
            TagPrefix = tagPrefix,
            Workflows = workflows,
            SuggestedWorkflowFile = suggested?.FileName,
            SuggestedReleaseModel = suggested is null
                ? null
                : suggested.TriggersOnTagPush ? ReleaseModel.TagPush
                : suggested.TriggersOnDispatch ? ReleaseModel.WorkflowDispatch
                : null,
        };
    }

    /// <summary>Reads <c>.github/workflows/*.yml</c> and infers each file's triggers.</summary>
    internal static List<WorkflowFileInfo> ReadWorkflows(string root)
    {
        var result = new List<WorkflowFileInfo>();
        var dir = Path.Combine(root, ".github", "workflows");
        if (!Directory.Exists(dir))
            return result;

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (!file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            var dispatch = text.Contains("workflow_dispatch", StringComparison.OrdinalIgnoreCase);
            var tagPush = text.Contains("push:", StringComparison.OrdinalIgnoreCase) && TagsLine().IsMatch(text);

            result.Add(new WorkflowFileInfo(Path.GetFileName(file), tagPush, dispatch));
        }

        return result;
    }

    /// <summary>Prefers a tag-triggered workflow, then a dispatchable one, else the first.</summary>
    internal static WorkflowFileInfo? PickWorkflow(IReadOnlyList<WorkflowFileInfo> workflows) =>
        workflows.FirstOrDefault(w => w.TriggersOnTagPush)
        ?? workflows.FirstOrDefault(w => w.TriggersOnDispatch)
        ?? workflows.FirstOrDefault();

    /// <summary>Infers the tag prefix by taking the most common non-digit lead-in across tags.</summary>
    internal static string? InferTagPrefix(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
            return null;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var match = LeadingPrefix().Match(tag);
            if (!match.Success)
                continue;
            var prefix = match.Groups[1].Value;
            counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
        }

        return counts.Count == 0
            ? null
            : counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>Breadth-first bounded search for any matching file (skips build/vendor dirs).</summary>
    private static bool ContainsFile(string root, string[] patterns, int maxDepth = 3)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            try
            {
                foreach (var pattern in patterns)
                {
                    if (Directory.EnumerateFiles(dir, pattern).Any())
                        return true;
                }

                if (depth >= maxDepth)
                    continue;

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (SkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch
            {
                // unreadable directory — ignore
            }
        }

        return false;
    }

    [GeneratedRegex(@"^\s*tags:", RegexOptions.Multiline)]
    private static partial Regex TagsLine();

    [GeneratedRegex(@"^([^\d]*)\d")]
    private static partial Regex LeadingPrefix();
}
