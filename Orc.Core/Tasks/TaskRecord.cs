namespace Orc.Core.Tasks;

public enum TaskState { Pending, Running, Succeeded, Failed, Interrupted }

/// <summary>
/// Per-repo progress recorded as a task runs, so an interrupted task can be resumed
/// on the same branch (and, when available, the same Claude session) instead of
/// restarting from scratch. <see cref="LastCompletedStage"/> is the highest stage that
/// finished; resume re-runs from the next one.
/// </summary>
public sealed record RepoCheckpoint(
    string RepoName,
    string BranchName,
    string Stamp,
    string? LastCompletedStage,
    string? ClaudeSessionId,
    bool HasChanges);

public sealed record TaskCheckpoint(
    int ResumeAttempts,
    IReadOnlyList<RepoCheckpoint> Repos);

public sealed record TaskSource(string Kind, string? Details = null)
{
    public static TaskSource User { get; } = new("user");

    /// <summary>A code-modifying step run through the full pipeline (branch/commit/merge).</summary>
    public static TaskSource Orchitect(string repo, string enhId, int stepN) =>
        new("orchitect", $"{repo}:{enhId}:s{stepN}");

    /// <summary>Orchitect's repo-analysis Claude run (writes enhancement files, no git).</summary>
    public static TaskSource Analysis(string repo) => new("analysis", repo);

    /// <summary>Orchitect's step-planning Claude run for one enhancement (no git).</summary>
    public static TaskSource Planning(string repo, string enhId) =>
        new("planning", $"{repo}:{enhId}");

    /// <summary>Whether this source is a non-pipeline, tracked-only Claude run (analysis/planning).</summary>
    public bool IsTrackedOnly => Kind is "analysis" or "planning";

    public override string ToString() =>
        Details is null ? Kind : $"{Kind}({Details})";
}

public sealed record TaskRecord(
    string Id,
    TaskSource Source,
    string RepoSpec,
    string Prompt,
    DateTime CreatedUtc);

public sealed record RepoResult(
    string RepoName,
    int ExitCode,
    bool HasChanges,
    string? Branch,
    string? PerRepoLogPath,
    string? FailedStage = null,
    string? FailReason = null);

public sealed record TaskOutcome(
    bool AllOk,
    bool HasAnyChanges,
    IReadOnlyList<RepoResult> PerRepo,
    string? Reason);

public sealed record TaskHeader(
    string Id,
    TaskSource Source,
    string RepoSpec,
    TaskState State,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    TaskOutcome? Outcome);
