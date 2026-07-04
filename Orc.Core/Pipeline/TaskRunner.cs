using Microsoft.Extensions.Logging;
using Orc.Core.Configuration;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Pipeline;

public interface ITaskRunner
{
    Task<TaskOutcome> RunAsync(TaskRecord task, CancellationToken ct, TaskCheckpoint? checkpoint = null);
}

internal sealed class TaskRunner : ITaskRunner
{
    private readonly IRepoRegistry _repos;
    private readonly IRepoLock _repoLock;
    private readonly ITaskStore _store;
    private readonly IReadOnlyList<IStage> _stages;
    private readonly WorkspaceLayout _layout;
    private readonly ILogger<TaskRunner> _logger;

    public TaskRunner(
        IRepoRegistry repos,
        IRepoLock repoLock,
        ITaskStore store,
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
        _store = store;
        _stages = [refresh, guard, branch, claude, commit, merge];
        _layout = layout;
        _logger = logger;
    }

    public async Task<TaskOutcome> RunAsync(TaskRecord task, CancellationToken ct, TaskCheckpoint? checkpoint = null)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["TaskId"] = task.Id });

        if (!_repos.TryResolve(task.RepoSpec, out var repos, out var err))
            return new TaskOutcome(false, false, [], err);

        if (checkpoint is not null)
            _logger.LogInformation("Resuming task {Id} (attempt {N})", task.Id, checkpoint.ResumeAttempts);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var freshBranch = $"{GuardUnmergedBranchStage.BranchPrefix}/{task.Id}-{stamp}";
        var progress = new CheckpointWriter(_store, task.Id, checkpoint?.ResumeAttempts ?? 0);

        var perRepo = await Task.WhenAll(repos.Select(r =>
            RunRepoAsync(task, r, freshBranch, stamp, ResumeFor(checkpoint, r.Name), progress, ct)));

        var allOk = perRepo.All(r => r.ExitCode == 0);
        var anyChanges = perRepo.Any(r => r.HasChanges);
        var reason = allOk ? null : SummarizeFailures(perRepo);
        return new TaskOutcome(allOk, anyChanges, perRepo, reason);
    }

    private static RepoCheckpoint? ResumeFor(TaskCheckpoint? cp, string repoName) =>
        cp?.Repos.FirstOrDefault(r => string.Equals(r.RepoName, repoName, StringComparison.Ordinal));

    private int StageIndexAfter(string? completedStage)
    {
        if (string.IsNullOrEmpty(completedStage)) return 0;
        for (var i = 0; i < _stages.Count; i++)
            if (_stages[i].Name == completedStage) return i + 1;
        return 0;
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

    private async Task<RepoResult> RunRepoAsync(
        TaskRecord task, RepoEntry repo, string freshBranch, string freshStamp,
        RepoCheckpoint? resume, CheckpointWriter progress, CancellationToken ct)
    {
        await using var _ = await _repoLock.AcquireAsync(repo.Name, ct);

        var branchName = resume?.BranchName ?? freshBranch;
        var stamp = resume?.Stamp ?? freshStamp;
        var ctx = new PipelineContext
        {
            Task = task,
            Repo = repo,
            BranchName = branchName,
            Stamp = stamp,
            IsResuming = resume is not null,
            ResumeSessionId = resume?.ClaudeSessionId,
            HasChanges = resume?.HasChanges ?? false,
            ClaudeSessionId = resume?.ClaudeSessionId,
        };
        var exit = 0;
        string? failedStage = null;
        string? failReason = null;
        var lastCompleted = resume?.LastCompletedStage;
        var startIndex = StageIndexAfter(lastCompleted);
        if (startIndex > 0)
            ctx.Transcript.AppendLine($"--- resuming {repo.Name} after stage '{lastCompleted}' (skipping {startIndex}) ---");

        try
        {
            for (var i = startIndex; i < _stages.Count; i++)
            {
                var stage = _stages[i];
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
                    lastCompleted = stage.Name;
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
            // Persist progress (incl. any captured Claude session id) with an untainted
            // token so a shutdown-driven cancel still records enough to resume later.
            await progress.UpdateAsync(
                new RepoCheckpoint(repo.Name, branchName, stamp, lastCompleted, ctx.ClaudeSessionId, ctx.HasChanges),
                CancellationToken.None);
            ctx.PerRepoLogPath = await PersistTranscriptAsync(task, repo, ctx, CancellationToken.None);
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

    /// <summary>
    /// Accumulates per-repo checkpoints for one task run and flushes the merged
    /// <see cref="TaskCheckpoint"/> to the store. The store serializes the actual writes,
    /// so concurrent repos can update safely.
    /// </summary>
    private sealed class CheckpointWriter
    {
        private readonly ITaskStore _store;
        private readonly string _taskId;
        private readonly int _attempts;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RepoCheckpoint> _repos = new(StringComparer.Ordinal);

        public CheckpointWriter(ITaskStore store, string taskId, int attempts)
        {
            _store = store;
            _taskId = taskId;
            _attempts = attempts;
        }

        public Task UpdateAsync(RepoCheckpoint repo, CancellationToken ct)
        {
            _repos[repo.RepoName] = repo;
            var snapshot = new TaskCheckpoint(_attempts, _repos.Values.ToArray());
            return _store.SaveCheckpointAsync(_taskId, snapshot, ct);
        }
    }
}
