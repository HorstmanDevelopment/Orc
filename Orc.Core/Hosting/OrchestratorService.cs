using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orc.Core.Pipeline;
using Orc.Core.Tasks;

namespace Orc.Core.Hosting;

internal sealed class OrchestratorService : BackgroundService
{
    private readonly ITaskStore _store;
    private readonly ITaskRunner _runner;
    private readonly ILogger<OrchestratorService> _logger;

    public OrchestratorService(ITaskStore store, ITaskRunner runner, ILogger<OrchestratorService> logger)
    {
        _store = store;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Orchestrator started");
        while (!ct.IsCancellationRequested)
        {
            TaskRecord? task = null;
            try { task = await _store.ClaimNextAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "claim failed"); }

            if (task is null)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                var outcome = await _runner.RunAsync(task, ct);
                await _store.CompleteAsync(task.Id, outcome, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task {Id} threw", task.Id);
                try { await _store.FailAsync(task.Id, ex.ToString(), CancellationToken.None); } catch { }
            }
        }
        _logger.LogInformation("Orchestrator stopped");
    }
}
