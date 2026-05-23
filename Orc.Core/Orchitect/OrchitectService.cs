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
                    var (enhancements, raw) = await _analysis.RunAsync(repo.LocalPath, repo.Mission, ct);
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
                var plan = await _planner.PlanAsync(repo.LocalPath, enh, repo.Mission, ct);
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
}
