using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orc.Core.Pipeline;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Hosting;

internal sealed class OrchestratorService : BackgroundService
{
    private readonly ITaskStore _store;
    private readonly ITaskRunner _runner;
    private readonly IRunningTaskRegistry _registry;
    private readonly IRepoRegistry _repos;
    private readonly IGitClient _git;
    private readonly IRepoLock _repoLock;
    private readonly ILogger<OrchestratorService> _logger;

    public OrchestratorService(
        ITaskStore store,
        ITaskRunner runner,
        IRunningTaskRegistry registry,
        IRepoRegistry repos,
        IGitClient git,
        IRepoLock repoLock,
        ILogger<OrchestratorService> logger)
    {
        _store = store;
        _runner = runner;
        _registry = registry;
        _repos = repos;
        _git = git;
        _repoLock = repoLock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken hostCt)
    {
        _logger.LogInformation("Orchestrator started");
        await ReconcileOrphansAsync(hostCt);
        while (!hostCt.IsCancellationRequested)
        {
            TaskRecord? task = null;
            try { task = await _store.ClaimNextAsync(hostCt); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "claim failed"); }

            if (task is null)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(500), hostCt); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            using var perTaskCts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
            _registry.Register(task.Id, perTaskCts);
            try
            {
                var outcome = await _runner.RunAsync(task, perTaskCts.Token);
                await _store.CompleteAsync(task.Id, outcome, hostCt);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                _logger.LogWarning("Task {Id} cancelled by user", task.Id);
                await CancelAndCleanupAsync(task.Id, task.RepoSpec, "cancelled by user");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task {Id} threw", task.Id);
                try { await _store.FailAsync(task.Id, ex.ToString(), CancellationToken.None); } catch { }
            }
            finally
            {
                _registry.Unregister(task.Id);
            }
        }
        _logger.LogInformation("Orchestrator stopped");
    }

    // Any task sitting in running/ at startup is an orphan: the process that owned it
    // is gone, so the in-memory registry has no CancellationTokenSource for it and it
    // would otherwise display as "Running" forever and refuse to cancel. Restore each
    // repo and move the task to failed/ so the workspace returns to a clean state.
    private async Task ReconcileOrphansAsync(CancellationToken hostCt)
    {
        IReadOnlyList<TaskHeader> orphans;
        try { orphans = await _store.ListAsync(TaskState.Running, hostCt); }
        catch (Exception ex) { _logger.LogError(ex, "orphan reconcile: list failed"); return; }

        if (orphans.Count == 0) return;
        _logger.LogWarning("Reconciling {Count} orphaned running task(s) from a previous run", orphans.Count);

        foreach (var o in orphans)
        {
            if (hostCt.IsCancellationRequested) return;
            await CancelAndCleanupAsync(o.Id, o.RepoSpec, "orphaned by restart");
        }
    }

    private async Task CancelAndCleanupAsync(string taskId, string repoSpec, string reason)
    {
        // Use an untainted token: host shutdown shouldn't abort cleanup mid-flight.
        var ct = CancellationToken.None;
        var cleanupLog = new StringBuilder();
        cleanupLog.AppendLine(reason);

        if (_repos.TryResolve(repoSpec, out var repos, out var err))
        {
            foreach (var repo in repos)
            {
                try
                {
                    await using var _ = await _repoLock.AcquireAsync(repo.Name, ct);
                    var r = await _git.AbortTaskAsync(repo, taskId, ct);
                    cleanupLog.AppendLine($"=== cleanup {repo.Name} (success={r.Success}) ===");
                    cleanupLog.AppendLine(r.Output);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cleanup threw for {Repo} on cancelled task {Id}", repo.Name, taskId);
                    cleanupLog.AppendLine($"=== cleanup {repo.Name} threw ===");
                    cleanupLog.AppendLine(ex.ToString());
                }
            }
        }
        else
        {
            cleanupLog.AppendLine($"could not resolve repos for cleanup: {err}");
        }

        try { await _store.FailAsync(taskId, cleanupLog.ToString(), ct); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to mark {Id} cancelled", taskId); }
    }
}
