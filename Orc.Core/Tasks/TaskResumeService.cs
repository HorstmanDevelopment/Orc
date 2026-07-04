using System.Text;
using Microsoft.Extensions.Logging;
using Orc.Core.Repos;

namespace Orc.Core.Tasks;

public interface ITaskResumer
{
    /// <summary>Re-queue an interrupted task so the orchestrator resumes it.</summary>
    Task<bool> ResumeAsync(string id, CancellationToken ct);

    /// <summary>Discard an interrupted task: clean its repos and move it to failed/.</summary>
    Task<bool> DiscardAsync(string id, CancellationToken ct);
}

internal sealed class TaskResumeService : ITaskResumer
{
    private readonly ITaskStore _store;
    private readonly IRepoRegistry _repos;
    private readonly IGitClient _git;
    private readonly IRepoLock _repoLock;
    private readonly ILogger<TaskResumeService> _logger;

    public TaskResumeService(
        ITaskStore store,
        IRepoRegistry repos,
        IGitClient git,
        IRepoLock repoLock,
        ILogger<TaskResumeService> logger)
    {
        _store = store;
        _repos = repos;
        _git = git;
        _repoLock = repoLock;
        _logger = logger;
    }

    public Task<bool> ResumeAsync(string id, CancellationToken ct) =>
        _store.RequeueInterruptedAsync(id, ct);

    public async Task<bool> DiscardAsync(string id, CancellationToken ct)
    {
        var header = await _store.GetAsync(id, ct);
        if (header is null) return false;

        var log = new StringBuilder();
        log.AppendLine("discarded by user");
        if (_repos.TryResolve(header.RepoSpec, out var repos, out var err))
        {
            foreach (var repo in repos)
            {
                try
                {
                    await using var _ = await _repoLock.AcquireAsync(repo.Name, ct);
                    var r = await _git.AbortTaskAsync(repo, id, ct);
                    log.AppendLine($"=== cleanup {repo.Name} (success={r.Success}) ===");
                    log.AppendLine(r.Output);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Discard cleanup threw for {Repo} on {Id}", repo.Name, id);
                    log.AppendLine($"=== cleanup {repo.Name} threw: {ex.Message} ===");
                }
            }
        }
        else
        {
            log.AppendLine($"could not resolve repos for cleanup: {err}");
        }

        return await _store.FailInterruptedAsync(id, log.ToString(), ct);
    }
}
