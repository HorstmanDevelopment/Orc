namespace Orc.Core.Tasks;

public enum TaskState { Pending, Running, Succeeded, Failed }

public sealed record TaskSource(string Kind, string? Details = null)
{
    public static TaskSource User { get; } = new("user");
    public static TaskSource Orchitect(string repo, string enhId, int stepN) =>
        new("orchitect", $"{repo}:{enhId}:s{stepN}");

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
    string? PerRepoLogPath);

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
