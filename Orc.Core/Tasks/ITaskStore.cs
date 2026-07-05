namespace Orc.Core.Tasks;

public interface ITaskStore
{
    Task<string> EnqueueAsync(TaskRecord task, CancellationToken ct);

    /// <summary>
    /// Record a task that is already executing directly into running/ (bypassing the
    /// pending → claim path). Used for tracked-only Claude runs (analysis/planning) that
    /// Orchitect runs itself but wants surfaced in the running-task views. Finalize with
    /// <see cref="CompleteAsync"/> / <see cref="FailAsync"/> like any other running task.
    /// </summary>
    Task TrackAsync(TaskRecord task, CancellationToken ct);

    Task<TaskRecord?> ClaimNextAsync(CancellationToken ct);

    /// <summary>
    /// Claim the oldest pending task whose <c>RepoSpec</c> passes <paramref name="canClaim"/>,
    /// skipping any that fail it. Lets the orchestrator pass over tasks whose target repos are
    /// already busy, so it can run one task per repo in parallel while never running two tasks
    /// against the same repo at once.
    /// </summary>
    Task<TaskRecord?> ClaimNextAsync(Func<string, bool> canClaim, CancellationToken ct);
    Task CompleteAsync(string id, TaskOutcome outcome, CancellationToken ct);
    Task FailAsync(string id, string reason, CancellationToken ct);
    Task<IReadOnlyList<TaskHeader>> ListAsync(TaskState state, CancellationToken ct);
    Task<TaskHeader?> GetAsync(string id, CancellationToken ct);
    Task<int> PurgeAsync(TaskState state, CancellationToken ct);

    /// <summary>Write/replace the resume checkpoint on the (running) task doc in place.</summary>
    Task SaveCheckpointAsync(string id, TaskCheckpoint checkpoint, CancellationToken ct);

    /// <summary>Read the resume checkpoint for a task, if any.</summary>
    Task<TaskCheckpoint?> GetCheckpointAsync(string id, CancellationToken ct);

    /// <summary>Move a running task to interrupted/ (checkpoint preserved) for later resume.</summary>
    Task<bool> MarkInterruptedAsync(string id, string note, CancellationToken ct);

    /// <summary>Move an interrupted task back to pending/ so the orchestrator re-claims it.</summary>
    Task<bool> RequeueInterruptedAsync(string id, CancellationToken ct);

    /// <summary>Move an interrupted task to failed/ (e.g. user discarded it).</summary>
    Task<bool> FailInterruptedAsync(string id, string reason, CancellationToken ct);

    event Action<TaskHeader>? StateChanged;
}
