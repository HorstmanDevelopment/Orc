using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orc.Core.Tasks;

namespace Orc.Core.Orchitect;

internal sealed class QuotaService : IHostedService
{
    private readonly ITaskStore _store;
    private readonly IQuota _quota;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(ITaskStore store, IQuota quota, ILogger<QuotaService> logger)
    {
        _store = store;
        _quota = quota;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _store.StateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _store.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private void OnStateChanged(TaskHeader header)
    {
        if (header.State != TaskState.Succeeded) return;
        if (header.Source.Kind != "orchitect") return;
        if (header.Outcome is not { HasAnyChanges: true } outcome) return;

        foreach (var r in outcome.PerRepo.Where(p => p.HasChanges))
        {
            _quota.IncrementModification(r.RepoName);
            _logger.LogInformation("Quota +1 for {Repo} (task {Id})", r.RepoName, header.Id);
        }
    }
}
