using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Core.Configuration;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Orchitect;

internal sealed class OrchitectService : BackgroundService, IOrchitectControl
{
    private readonly IRepoRegistry _registry;
    private readonly IOrchitectStateStore _state;
    private readonly IQuota _quota;
    private readonly AnalysisRunner _analysis;
    private readonly StepPlanner _planner;
    private readonly ITaskStore _tasks;
    private readonly IRunningTaskRegistry _running;
    private readonly WorkspaceLayout _layout;
    private readonly OrchitectOptions _options;
    private readonly ILogger<OrchitectService> _logger;

    private readonly string _pauseFlagPath;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TaskHeader>> _waiters = new();

    public OrchitectService(
        IRepoRegistry registry,
        IOrchitectStateStore state,
        IQuota quota,
        AnalysisRunner analysis,
        StepPlanner planner,
        ITaskStore tasks,
        IRunningTaskRegistry running,
        WorkspaceLayout layout,
        IOptions<OrchitectOptions> options,
        ILogger<OrchitectService> logger)
    {
        _registry = registry;
        _state = state;
        _quota = quota;
        _analysis = analysis;
        _planner = planner;
        _tasks = tasks;
        _running = running;
        _layout = layout;
        _options = options.Value;
        _logger = logger;
        _pauseFlagPath = Path.Combine(_layout.OrchitectDir, "paused");
    }

    public bool IsPaused => File.Exists(_pauseFlagPath);

    public void Pause()
    {
        try { File.WriteAllText(_pauseFlagPath, DateTime.UtcNow.ToString("o")); } catch { }
        _logger.LogInformation("Orchitect paused");
    }

    public void Resume()
    {
        try { File.Delete(_pauseFlagPath); } catch { }
        _logger.LogInformation("Orchitect resumed");
    }

    public void ForceReanalyze(string repoName)
    {
        var s = _state.Load(repoName);
        s.LastAnalyzedUtc = null;
        foreach (var e in s.Enhancements)
            if (e.Status == EnhancementStatus.Identified) e.Status = EnhancementStatus.Abandoned;
        _state.Save(repoName, s);
    }

    public void ResetState(string repoName)
    {
        _state.Reset(repoName);

        var repo = _registry.All().FirstOrDefault(r => string.Equals(r.Name, repoName, StringComparison.OrdinalIgnoreCase));
        if (repo is not null)
        {
            var outDir = Path.Combine(repo.LocalPath, AnalysisRunner.OutputDirName);
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "could not clear {Dir} during reset", outDir); }
        }

        _logger.LogInformation("[{Repo}] Orchitect state reset", repoName);
    }

    public QuotaSnapshot QuotaSnapshot() => _quota.Snapshot();
    public IReadOnlyList<string> ListRepos() => _state.ListRepos();
    public RepoState LoadState(string repoName) => _state.Load(repoName);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _tasks.StateChanged += OnTaskState;
        _logger.LogInformation("Orchitect started");

        var repos = _registry.All();
        if (repos.Count == 0)
        {
            _logger.LogInformation("No repos in registry; idling");
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return;
        }

        try
        {
            var loops = repos.Select(r => RunRepoLoopAsync(r, ct)).ToArray();
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _tasks.StateChanged -= OnTaskState;
        }
    }

    private void OnTaskState(TaskHeader header)
    {
        if (header.State != TaskState.Succeeded && header.State != TaskState.Failed) return;
        if (_waiters.TryRemove(header.Id, out var tcs))
            tcs.TrySetResult(header);
    }

    private async Task RunRepoLoopAsync(RepoEntry repo, CancellationToken ct)
    {
        _logger.LogInformation("[{Repo}] loop start", repo.Name);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (IsPaused) { await Task.Delay(TimeSpan.FromSeconds(10), ct); continue; }

                if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), ct);
                    continue;
                }

                var state = _state.Load(repo.Name);
                state.RepoName = repo.Name;

                if (!state.Enhancements.Any(e => e.Status is EnhancementStatus.Identified or EnhancementStatus.InProgress))
                {
                    if (!_quota.CanModify(repo.Name)) { await DelayUntilNextDayAsync(ct); continue; }

                    _logger.LogInformation("[{Repo}] analyzing", repo.Name);
                    var (enhancements, raw) = await TrackedRunAsync(
                        NewTrackedId("analysis", repo.Name),
                        TaskSource.Analysis(repo.Name),
                        repo.Name,
                        "Orchitect: analyze repository for enhancement candidates",
                        token => _analysis.RunAsync(repo.Name, repo.LocalPath, repo.Mission, token),
                        ct);
                    try { await File.WriteAllTextAsync(_state.AnalysisPath(repo.Name), raw, ct); } catch { }
                    state.LastAnalyzedUtc = DateTime.UtcNow;

                    var added = 0;
                    foreach (var e in enhancements)
                    {
                        if (state.Enhancements.Any(x => string.Equals(x.Title, e.Title, StringComparison.OrdinalIgnoreCase))) continue;
                        state.Enhancements.Add(e);
                        added++;
                    }
                    _state.Save(repo.Name, state);
                    _state.AppendHistory(repo.Name, $"analysis: parsed={enhancements.Count} added={added}");

                    if (!state.Enhancements.Any(e => e.Status is EnhancementStatus.Identified or EnhancementStatus.InProgress))
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), ct);
                        continue;
                    }
                }

                if (!_quota.CanModify(repo.Name)) { await DelayUntilNextDayAsync(ct); continue; }

                var enh = PickEnhancement(state);
                if (enh is null) { await Task.Delay(TimeSpan.FromMinutes(5), ct); continue; }

                _logger.LogInformation("[{Repo}] planning step for {Id} '{Title}'", repo.Name, enh.Id, enh.Title);
                var plan = await TrackedRunAsync(
                    NewTrackedId($"planning_{enh.Id}", repo.Name),
                    TaskSource.Planning(repo.Name, enh.Id),
                    repo.Name,
                    $"Orchitect: plan next step for {enh.Id} '{enh.Title}'",
                    token => _planner.PlanAsync(repo.Name, repo.LocalPath, enh, repo.Mission, token),
                    ct);
                if (plan.IsComplete)
                {
                    enh.Status = EnhancementStatus.Completed;
                    _state.Save(repo.Name, state);
                    _state.AppendHistory(repo.Name, $"{enh.Id} marked Completed by planner");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(plan.Step))
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    continue;
                }

                var step = new EnhancementStep
                {
                    N = enh.Steps.Count + 1,
                    Prompt = plan.Step.Trim(),
                    Status = StepStatus.Pending,
                };
                enh.Steps.Add(step);
                enh.Status = EnhancementStatus.InProgress;
                _state.Save(repo.Name, state);

                var taskId = NewId(repo.Name, enh.Id, step.N);
                var record = new TaskRecord(
                    taskId,
                    TaskSource.Orchitect(repo.Name, enh.Id, step.N),
                    repo.Name,
                    step.Prompt,
                    DateTime.UtcNow);

                var waiter = new TaskCompletionSource<TaskHeader>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters[taskId] = waiter;
                await _tasks.EnqueueAsync(record, ct);
                step.TaskId = taskId;
                step.SubmittedUtc = DateTime.UtcNow;
                step.Status = StepStatus.Submitted;
                _state.Save(repo.Name, state);
                _state.AppendHistory(repo.Name, $"{enh.Id} step {step.N} submitted: {taskId}");

                var finalHeader = await waiter.Task.WaitAsync(ct);
                step.FinishedUtc = DateTime.UtcNow;
                step.Status = finalHeader.State == TaskState.Succeeded ? StepStatus.Completed : StepStatus.Failed;
                step.HasChanges = finalHeader.Outcome?.PerRepo
                    .FirstOrDefault(p => string.Equals(p.RepoName, repo.Name, StringComparison.OrdinalIgnoreCase))
                    ?.HasChanges ?? false;
                _state.Save(repo.Name, state);
                _state.AppendHistory(repo.Name,
                    $"{enh.Id} step {step.N} {step.Status} hasChanges={step.HasChanges}");

                if (step.Status == StepStatus.Failed)
                {
                    var fails = 0;
                    for (var i = enh.Steps.Count - 1; i >= 0; i--)
                    {
                        if (enh.Steps[i].Status == StepStatus.Failed) fails++;
                        else break;
                    }
                    if (fails >= _options.ConsecutiveFailureLimit)
                    {
                        enh.Status = EnhancementStatus.Abandoned;
                        _state.Save(repo.Name, state);
                        _state.AppendHistory(repo.Name, $"{enh.Id} Abandoned after {fails} consecutive failures");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (TrackedRunCanceledException)
            {
                // A user cancelled the current analysis/planning run (not host shutdown).
                // Skip this cycle and re-evaluate from the top of the loop.
                _logger.LogInformation("[{Repo}] tracked run cancelled by user; continuing", repo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Repo}] loop error", repo.Name);
                try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { throw; }
            }
        }
    }

    private static Enhancement? PickEnhancement(RepoState state) =>
        state.Enhancements.Where(e => e.Status == EnhancementStatus.InProgress).OrderBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault()
        ?? state.Enhancements.Where(e => e.Status == EnhancementStatus.Identified).OrderBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();

    private static async Task DelayUntilNextDayAsync(CancellationToken ct)
    {
        var span = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
        if (span < TimeSpan.FromMinutes(1)) span = TimeSpan.FromMinutes(1);
        if (span > TimeSpan.FromHours(1)) span = TimeSpan.FromHours(1);
        await Task.Delay(span, ct);
    }

    private static string NewId(string repo, string enhId, int stepN)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"orchitect_{repo}_{enhId}_s{stepN}_{stamp}";
    }

    private static string NewTrackedId(string kind, string repo)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"orchitect_{repo}_{kind}_{stamp}";
    }

    /// <summary>
    /// Records a non-pipeline Claude run (analysis/planning) in the task store so it shows
    /// up in the running-task views, running it under a per-run cancellation source so the
    /// "Cancel running task" UI can stop it. A user cancel (as opposed to host shutdown)
    /// surfaces as <see cref="TrackedRunCanceledException"/> so the repo loop can continue
    /// rather than tear down.
    /// </summary>
    private async Task<T> TrackedRunAsync<T>(
        string taskId, TaskSource source, string repoName, string prompt,
        Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _running.Register(taskId, cts);
        try
        {
            var record = new TaskRecord(taskId, source, repoName, prompt, DateTime.UtcNow);
            await _tasks.TrackAsync(record, ct);
            try
            {
                var result = await work(cts.Token);
                await _tasks.CompleteAsync(
                    taskId,
                    new TaskOutcome(true, false, [new RepoResult(repoName, 0, false, null, null)], null),
                    CancellationToken.None);
                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await _tasks.FailAsync(taskId, "cancelled by user", CancellationToken.None);
                throw new TrackedRunCanceledException();
            }
            catch (OperationCanceledException)
            {
                await _tasks.FailAsync(taskId, "cancelled (host shutdown)", CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                await _tasks.FailAsync(taskId, ex.ToString(), CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _running.Unregister(taskId);
        }
    }

    /// <summary>Signals that a tracked-only run was cancelled by the user (not host shutdown).</summary>
    private sealed class TrackedRunCanceledException : Exception;
}
