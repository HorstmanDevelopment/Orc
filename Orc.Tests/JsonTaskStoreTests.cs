using Microsoft.Extensions.Logging.Abstractions;
using Orc.Core.Tasks;
using Xunit;

namespace Orc.Tests;

public class JsonTaskStoreTests
{
    private static JsonTaskStore Build(TempWorkspace ws) =>
        new(ws.Layout, NullLogger<JsonTaskStore>.Instance);

    private static TaskRecord NewRecord(string id = "task_001", string spec = "all", string prompt = "hello") =>
        new(id, TaskSource.User, spec, prompt, DateTime.UtcNow);

    [Fact]
    public async Task Enqueue_then_ClaimNext_yields_task_in_running()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);

        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal("task_001", claimed!.Id);

        var pending = await store.ListAsync(TaskState.Pending, CancellationToken.None);
        var running = await store.ListAsync(TaskState.Running, CancellationToken.None);
        Assert.Empty(pending);
        Assert.Single(running);
    }

    [Fact]
    public async Task ClaimNext_returns_null_when_empty()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        Assert.Null(await store.ClaimNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Complete_moves_to_succeeded_with_outcome()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);

        var outcome = new TaskOutcome(true, true,
            [new RepoResult("foo", 0, true, "orc-task/x", "log")], null);
        await store.CompleteAsync(claimed!.Id, outcome, CancellationToken.None);

        var succeeded = await store.ListAsync(TaskState.Succeeded, CancellationToken.None);
        Assert.Single(succeeded);
        Assert.True(succeeded[0].Outcome!.HasAnyChanges);

        var running = await store.ListAsync(TaskState.Running, CancellationToken.None);
        Assert.Empty(running);
    }

    [Fact]
    public async Task Fail_moves_to_failed_with_reason()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);
        await store.FailAsync(claimed!.Id, "boom", CancellationToken.None);

        var failed = await store.ListAsync(TaskState.Failed, CancellationToken.None);
        Assert.Single(failed);
        Assert.Equal("boom", failed[0].Outcome!.Reason);
    }

    [Fact]
    public async Task StateChanged_fires_for_each_transition()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        var states = new List<TaskState>();
        store.StateChanged += h => states.Add(h.State);

        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);
        await store.CompleteAsync(claimed!.Id, new TaskOutcome(true, false, [], null), CancellationToken.None);

        Assert.Equal([TaskState.Pending, TaskState.Running, TaskState.Succeeded], states);
    }

    [Fact]
    public async Task Concurrent_claims_only_one_wins_per_task()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        for (var i = 0; i < 5; i++)
            await store.EnqueueAsync(NewRecord($"t_{i:D2}"), CancellationToken.None);

        var claims = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => store.ClaimNextAsync(CancellationToken.None)));

        var ids = claims.Where(c => c is not null).Select(c => c!.Id).ToArray();
        Assert.Equal(5, ids.Length);
        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public async Task Purge_deletes_records_in_state()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord("a"), CancellationToken.None);
        await store.EnqueueAsync(NewRecord("b"), CancellationToken.None);

        var n = await store.PurgeAsync(TaskState.Pending, CancellationToken.None);
        Assert.Equal(2, n);
        Assert.Empty(await store.ListAsync(TaskState.Pending, CancellationToken.None));
    }

    private static TaskCheckpoint Checkpoint(int attempts = 0, string? stage = "RunClaude", string? session = "sess-1") =>
        new(attempts, [new RepoCheckpoint("foo", "orc-task/foo-x", "stamp", stage, session, true)]);

    [Fact]
    public async Task SaveCheckpoint_then_GetCheckpoint_roundtrips()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);

        await store.SaveCheckpointAsync(claimed!.Id, Checkpoint(attempts: 1), CancellationToken.None);

        var cp = await store.GetCheckpointAsync(claimed.Id, CancellationToken.None);
        Assert.NotNull(cp);
        Assert.Equal(1, cp!.ResumeAttempts);
        Assert.Equal("sess-1", cp.Repos.Single().ClaudeSessionId);
        Assert.Equal("RunClaude", cp.Repos.Single().LastCompletedStage);
    }

    [Fact]
    public async Task MarkInterrupted_moves_running_and_preserves_checkpoint()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);
        await store.SaveCheckpointAsync(claimed!.Id, Checkpoint(), CancellationToken.None);

        Assert.True(await store.MarkInterruptedAsync(claimed.Id, "interrupted", CancellationToken.None));

        Assert.Empty(await store.ListAsync(TaskState.Running, CancellationToken.None));
        var interrupted = await store.ListAsync(TaskState.Interrupted, CancellationToken.None);
        Assert.Single(interrupted);
        Assert.Equal("interrupted", interrupted[0].Outcome!.Reason);
        // Checkpoint survives the move so resume can use it.
        var cp = await store.GetCheckpointAsync(claimed.Id, CancellationToken.None);
        Assert.Equal("sess-1", cp!.Repos.Single().ClaudeSessionId);
    }

    [Fact]
    public async Task Requeue_interrupted_returns_to_pending_with_checkpoint()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);
        await store.SaveCheckpointAsync(claimed!.Id, Checkpoint(attempts: 1), CancellationToken.None);
        await store.MarkInterruptedAsync(claimed.Id, "interrupted", CancellationToken.None);

        Assert.True(await store.RequeueInterruptedAsync(claimed.Id, CancellationToken.None));
        Assert.Empty(await store.ListAsync(TaskState.Interrupted, CancellationToken.None));
        Assert.Single(await store.ListAsync(TaskState.Pending, CancellationToken.None));

        // Re-claiming keeps the checkpoint, so the runner resumes rather than restarts.
        var reclaimed = await store.ClaimNextAsync(CancellationToken.None);
        var cp = await store.GetCheckpointAsync(reclaimed!.Id, CancellationToken.None);
        Assert.Equal(1, cp!.ResumeAttempts);
    }

    [Fact]
    public async Task FailInterrupted_moves_to_failed()
    {
        using var ws = new TempWorkspace();
        var store = Build(ws);
        await store.EnqueueAsync(NewRecord(), CancellationToken.None);
        var claimed = await store.ClaimNextAsync(CancellationToken.None);
        await store.SaveCheckpointAsync(claimed!.Id, Checkpoint(), CancellationToken.None);
        await store.MarkInterruptedAsync(claimed.Id, "interrupted", CancellationToken.None);

        Assert.True(await store.FailInterruptedAsync(claimed.Id, "discarded", CancellationToken.None));
        var failed = await store.ListAsync(TaskState.Failed, CancellationToken.None);
        Assert.Single(failed);
        Assert.Equal("discarded", failed[0].Outcome!.Reason);
    }
}
