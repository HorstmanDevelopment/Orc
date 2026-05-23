namespace Orc.Core.Tasks;

public interface ITaskStore
{
    Task<string> EnqueueAsync(TaskRecord task, CancellationToken ct);
    Task<TaskRecord?> ClaimNextAsync(CancellationToken ct);
    Task CompleteAsync(string id, TaskOutcome outcome, CancellationToken ct);
    Task FailAsync(string id, string reason, CancellationToken ct);
    Task<IReadOnlyList<TaskHeader>> ListAsync(TaskState state, CancellationToken ct);
    Task<TaskHeader?> GetAsync(string id, CancellationToken ct);
    Task<int> PurgeAsync(TaskState state, CancellationToken ct);

    event Action<TaskHeader>? StateChanged;
}
