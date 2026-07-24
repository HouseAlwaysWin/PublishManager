namespace PublishManager.Core.Detection;

/// <summary>
/// Infers project settings from a local folder so the user doesn't have to type
/// them: project kind, git remote owner/repo, tag prefix, and the workflow file
/// (plus the release model its triggers imply).
/// </summary>
public interface IProjectDetector
{
    Task<ProjectDetection> DetectAsync(string localPath, CancellationToken ct = default);
}
