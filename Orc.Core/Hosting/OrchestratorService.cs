using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Core.Configuration;
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
    private readonly ResumeOptions _resume;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<OrchestratorService> _logger;

    // Serialization within a repo: a repo whose name is here has a task in flight, so no
    // other task targeting it may be claimed. Guarded by _gate along with _inFlight.
    private readonly object _gate = new();
    private readonly HashSet<string> _busyRepos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _inFlight = new(StringComparer.Ordinal);

    public OrchestratorService(
        ITaskStore store,
        ITaskRunner runner,
        IRunningTaskRegistry registry,
        IRepoRegistry repos,
        IGitClient git,
        IRepoLock repoLock,
        IOptions<ResumeOptions> resume,
        IOptions<OrchestratorOptions> options,
        ILogger<OrchestratorService> logger)
    {
        _store = store;
        _runner = runner;
        _registry = registry;
        _repos = repos;
        _git = git;
        _repoLock = repoLock;
        _resume = resume.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken hostCt)
    {
        _logger.LogInformation("Orchestrator started");
        await ReconcileOrphansAsync(hostCt);
        await AutoResumeAsync(hostCt);

        while (!hostCt.IsCancellationRequested)
        {
            // Fill every free slot with a claimable task. Claiming skips any task whose
            // repos are already busy, so distinct repos run in parallel while a single repo
            // never runs two tasks at once.
            var launchedAny = false;
            while (HasFreeSlot())
            {
                TaskRecord? task;
                try { task = await _store.ClaimNextAsync(CanClaim, hostCt); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogError(ex, "claim failed"); break; }

                if (task is null) break; // nothing claimable right now

                var repoNames = ReposFor(task.RepoSpec);
                lock (_gate)
                {
                    foreach (var r in repoNames) _busyRepos.Add(r);
                }
                // RunTaskAsync is async: it suspends at its first await and returns here
                // before its finally (which frees the repos) can run, so registering it in
                // _inFlight now cannot race the removal.
                var run = RunTaskAsync(task, repoNames, hostCt);
                lock (_gate) { _inFlight[task.Id] = run; }
                launchedAny = true;
            }

            if (hostCt.IsCancellationRequested) break;
            if (!launchedAny)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(500), hostCt); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Let in-flight runs finish their cleanup/checkpointing before the host stops.
        Task[] draining;
        lock (_gate) draining = _inFlight.Values.ToArray();
        if (draining.Length > 0)
        {
            try { await Task.WhenAll(draining); } catch { /* per-task failures already handled */ }
        }
        _logger.LogInformation("Orchestrator stopped");
    }

    private bool HasFreeSlot()
    {
        var max = _options.MaxConcurrentTasks;
        if (max <= 0) return true; // no extra cap; one-per-repo bounds it naturally
        lock (_gate) return _inFlight.Count < max;
    }

    // Predicate handed to the store: a task is claimable only if none of its repos are busy.
    private bool CanClaim(string repoSpec)
    {
        var names = ReposFor(repoSpec);
        if (names.Count == 0) return true; // unresolvable spec: claim so the runner fails it cleanly
        lock (_gate) return !names.Any(_busyRepos.Contains);
    }

    private IReadOnlyList<string> ReposFor(string repoSpec) =>
        _repos.TryResolve(repoSpec, out var repos, out _)
            ? repos.Select(r => r.Name).ToArray()
            : [];

    private async Task RunTaskAsync(TaskRecord task, IReadOnlyList<string> repoNames, CancellationToken hostCt)
    {
        using var perTaskCts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
        _registry.Register(task.Id, perTaskCts);
        try
        {
            // A resumed task carries a checkpoint; a fresh one returns null here.
            var checkpoint = await _store.GetCheckpointAsync(task.Id, hostCt);
            var outcome = await _runner.RunAsync(task, perTaskCts.Token, checkpoint);
            await _store.CompleteAsync(task.Id, outcome, hostCt);
        }
        catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
        {
            _logger.LogWarning("Task {Id} cancelled by user", task.Id);
            await CancelAndCleanupAsync(task.Id, task.RepoSpec, "cancelled by user");
        }
        catch (OperationCanceledException)
        {
            // Host shutting down: leave the doc in running/ to be reconciled on next start.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task {Id} threw", task.Id);
            try { await _store.FailAsync(task.Id, ex.ToString(), CancellationToken.None); } catch { }
        }
        finally
        {
            _registry.Unregister(task.Id);
            lock (_gate)
            {
                foreach (var r in repoNames) _busyRepos.Remove(r);
                _inFlight.Remove(task.Id);
            }
        }
    }

    // Any task sitting in running/ at startup is an orphan: the process that owned it is
    // gone, so the in-memory registry has no CancellationTokenSource for it. A task with
    // meaningful progress (a captured Claude session, or a created branch) is preserved as
    // Interrupted for resume; anything else is wiped clean and moved to failed/.
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

            // Analysis/planning are tracked-only: they never branch, so there is nothing to
            // resume and no git state to clean up. Just fail the stale doc.
            if (o.Source.IsTrackedOnly)
            {
                try { await _store.FailAsync(o.Id, "orphaned by restart (tracked-only run)", hostCt); } catch { }
                continue;
            }

            var cp = await _store.GetCheckpointAsync(o.Id, hostCt);
            if (IsResumable(cp))
            {
                await _store.MarkInterruptedAsync(o.Id, "interrupted (process exited mid-run)", hostCt);
                _logger.LogInformation("Task {Id} preserved as interrupted for resume", o.Id);
            }
            else
            {
                await CancelAndCleanupAsync(o.Id, o.RepoSpec, "orphaned by restart");
            }
        }
    }

    // Re-queue interrupted tasks (under the attempt cap) so the claim loop resumes them.
    private async Task AutoResumeAsync(CancellationToken hostCt)
    {
        if (!_resume.AutoResume) return;

        IReadOnlyList<TaskHeader> interrupted;
        try { interrupted = await _store.ListAsync(TaskState.Interrupted, hostCt); }
        catch (Exception ex) { _logger.LogError(ex, "auto-resume: list failed"); return; }

        foreach (var t in interrupted)
        {
            if (hostCt.IsCancellationRequested) return;

            var cp = await _store.GetCheckpointAsync(t.Id, hostCt);
            var attempts = cp?.ResumeAttempts ?? 0;
            if (attempts >= _resume.MaxAttempts)
            {
                _logger.LogWarning("Task {Id} hit resume cap ({Max}); left for manual handling", t.Id, _resume.MaxAttempts);
                continue;
            }

            if (cp is not null)
                await _store.SaveCheckpointAsync(t.Id, cp with { ResumeAttempts = attempts + 1 }, hostCt);
            await _store.RequeueInterruptedAsync(t.Id, hostCt);
            _logger.LogInformation("Auto-resuming task {Id} (attempt {N})", t.Id, attempts + 1);
        }
    }

    private static bool IsResumable(TaskCheckpoint? cp) =>
        cp is not null && cp.Repos.Any(r =>
            r.ClaudeSessionId is not null ||
            r.LastCompletedStage is "CreateBranch" or "RunClaude" or "Commit");

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
