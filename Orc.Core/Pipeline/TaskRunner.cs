using Microsoft.Extensions.Logging;
using Orc.Core.Configuration;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Pipeline;

public interface ITaskRunner
{
    Task<TaskOutcome> RunAsync(TaskRecord task, CancellationToken ct);
}

internal sealed class TaskRunner : ITaskRunner
{
    private readonly IRepoRegistry _repos;
    private readonly IRepoLock _repoLock;
    private readonly IReadOnlyList<IStage> _stages;
    private readonly WorkspaceLayout _layout;
    private readonly ILogger<TaskRunner> _logger;

    public TaskRunner(
        IRepoRegistry repos,
        IRepoLock repoLock,
        RefreshRepoStage refresh,
        GuardUnmergedBranchStage guard,
        CreateBranchStage branch,
        RunClaudeStage claude,
        CommitStage commit,
        MergeStage merge,
        WorkspaceLayout layout,
        ILogger<TaskRunner> logger)
    {
        _repos = repos;
        _repoLock = repoLock;
        _stages = [refresh, guard, branch, claude, commit, merge];
        _layout = layout;
        _logger = logger;
    }

    public async Task<TaskOutcome> RunAsync(TaskRecord task, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["TaskId"] = task.Id });

        if (!_repos.TryResolve(task.RepoSpec, out var repos, out var err))
            return new TaskOutcome(false, false, [], err);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var branchName = $"{GuardUnmergedBranchStage.BranchPrefix}/{task.Id}-{stamp}";

        var perRepo = await Task.WhenAll(repos.Select(r => RunRepoAsync(task, r, branchName, stamp, ct)));

        var allOk = perRepo.All(r => r.ExitCode == 0);
        var anyChanges = perRepo.Any(r => r.HasChanges);
        var reason = allOk ? null : SummarizeFailures(perRepo);
        return new TaskOutcome(allOk, anyChanges, perRepo, reason);
    }

    private static string SummarizeFailures(IReadOnlyList<RepoResult> perRepo)
    {
        var failed = perRepo.Where(r => r.ExitCode != 0).ToArray();
        if (failed.Length == 0) return "task failed";

        return string.Join("; ", failed.Select(r =>
        {
            var stage = r.FailedStage is { Length: > 0 } s ? $" [{s}]" : "";
            var why = r.FailReason is { Length: > 0 } w ? $": {w}" : $" (exit {r.ExitCode})";
            return $"{r.RepoName}{stage}{why}";
        }));
    }

    private async Task<RepoResult> RunRepoAsync(TaskRecord task, RepoEntry repo, string branchName, string stamp, CancellationToken ct)
    {
        await using var _ = await _repoLock.AcquireAsync(repo.Name, ct);

        var ctx = new PipelineContext
        {
            Task = task,
            Repo = repo,
            BranchName = branchName,
            Stamp = stamp,
        };
        var exit = 0;
        string? failedStage = null;
        string? failReason = null;

        try
        {
            foreach (var stage in _stages)
            {
                try
                {
                    var r = await stage.ExecuteAsync(ctx, ct);
                    if (!r.Success)
                    {
                        _logger.LogWarning("Stage {Stage} aborted on {Repo}: {Reason}", stage.Name, repo.Name, r.Reason);
                        exit = exit == 0 ? -1 : exit;
                        failedStage = stage.Name;
                        failReason = r.Reason ?? $"stage {stage.Name} aborted";
                        break;
                    }
                    if (r.StopPipeline) break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stage {Stage} threw on {Repo}", stage.Name, repo.Name);
                    ctx.Transcript.AppendLine($"--- {stage.Name} EXCEPTION ---");
                    ctx.Transcript.AppendLine(ex.ToString());
                    exit = exit == 0 ? -2 : exit;
                    failedStage = stage.Name;
                    failReason = $"{stage.Name} threw: {ex.Message}";
                    break;
                }
            }

            if (exit == 0) exit = ctx.ClaudeExitCode;
        }
        finally
        {
            ctx.PerRepoLogPath = await PersistTranscriptAsync(task, repo, ctx, ct);
        }

        return new RepoResult(repo.Name, exit, ctx.HasChanges, branchName, ctx.PerRepoLogPath, failedStage, failReason);
    }

    private async Task<string> PersistTranscriptAsync(TaskRecord task, RepoEntry repo, PipelineContext ctx, CancellationToken ct)
    {
        var dir = Path.Combine(_layout.ArtifactsDir, task.Id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{repo.Name}.log");
        await File.WriteAllTextAsync(path, ctx.Transcript.ToString(), ct);
        return path;
    }
}
