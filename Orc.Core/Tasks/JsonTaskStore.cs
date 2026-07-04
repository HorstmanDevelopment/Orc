using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orc.Core.Configuration;

namespace Orc.Core.Tasks;

internal sealed class JsonTaskStore : ITaskStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WorkspaceLayout _layout;
    private readonly ILogger<JsonTaskStore> _logger;
    private readonly object _claimLock = new();
    private readonly SemaphoreSlim _checkpointLock = new(1, 1);

    public event Action<TaskHeader>? StateChanged;

    public JsonTaskStore(WorkspaceLayout layout, ILogger<JsonTaskStore> logger)
    {
        _layout = layout;
        _logger = logger;
        foreach (var s in Enum.GetValues<TaskState>())
            Directory.CreateDirectory(DirFor(s));
    }

    public async Task<string> EnqueueAsync(TaskRecord task, CancellationToken ct)
    {
        var header = new TaskHeader(task.Id, task.Source, task.RepoSpec, TaskState.Pending, task.CreatedUtc, null, null);
        var doc = new StoredTask(header, task.Prompt);
        var path = PathFor(TaskState.Pending, task.Id);
        await WriteAtomicAsync(path, doc, ct);
        _logger.LogInformation("Enqueued task {Id} src={Source} spec={Spec}", task.Id, task.Source, task.RepoSpec);
        StateChanged?.Invoke(header);
        return task.Id;
    }

    public async Task<TaskRecord?> ClaimNextAsync(CancellationToken ct)
    {
        string? source = null;
        string? dest = null;
        StoredTask? doc = null;

        lock (_claimLock)
        {
            var candidate = Directory.EnumerateFiles(DirFor(TaskState.Pending), "*.json")
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .FirstOrDefault();
            if (candidate is null) return null;

            try
            {
                doc = ReadDoc(candidate);
                if (doc is null) return null;
                source = candidate;
                dest = PathFor(TaskState.Running, doc.Header.Id);
                File.Move(candidate, dest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "claim failed for {Path}", candidate);
                return null;
            }
        }

        var running = doc!.Header with { State = TaskState.Running };
        // Preserve the checkpoint so a requeued (resumed) task keeps its progress.
        var updated = new StoredTask(running, doc.Prompt, doc.Checkpoint);
        await WriteAtomicAsync(dest!, updated, ct);
        StateChanged?.Invoke(running);
        return new TaskRecord(running.Id, running.Source, running.RepoSpec, doc.Prompt, running.CreatedUtc);
    }

    public Task CompleteAsync(string id, TaskOutcome outcome, CancellationToken ct) =>
        TransitionFromRunningAsync(id, outcome.AllOk ? TaskState.Succeeded : TaskState.Failed, outcome, ct);

    public Task FailAsync(string id, string reason, CancellationToken ct) =>
        TransitionFromRunningAsync(id, TaskState.Failed,
            new TaskOutcome(false, false, [], reason), ct);

    private async Task TransitionFromRunningAsync(string id, TaskState target, TaskOutcome outcome, CancellationToken ct)
    {
        var src = PathFor(TaskState.Running, id);
        if (!File.Exists(src))
        {
            // Already finalized or never claimed — write a terminal doc anyway from pending if found
            src = PathFor(TaskState.Pending, id);
            if (!File.Exists(src))
            {
                _logger.LogWarning("transition target {Id} not found in pending/running", id);
                return;
            }
        }

        var doc = ReadDoc(src);
        if (doc is null) return;

        var newHeader = doc.Header with
        {
            State = target,
            CompletedUtc = DateTime.UtcNow,
            Outcome = outcome,
        };
        var dest = PathFor(target, id);
        var newDoc = new StoredTask(newHeader, doc.Prompt);
        await WriteAtomicAsync(dest, newDoc, ct);
        try { if (!string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) File.Delete(src); } catch { }

        _logger.LogInformation("Task {Id} -> {State} (changes={Changes})", id, target, outcome.HasAnyChanges);
        StateChanged?.Invoke(newHeader);
    }

    public Task<IReadOnlyList<TaskHeader>> ListAsync(TaskState state, CancellationToken ct)
    {
        var dir = DirFor(state);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<TaskHeader>>([]);

        var list = new List<TaskHeader>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            var doc = ReadDoc(f);
            if (doc != null) list.Add(doc.Header);
        }
        list.Sort((a, b) => DateTime.Compare(
            b.CompletedUtc ?? b.CreatedUtc,
            a.CompletedUtc ?? a.CreatedUtc));
        return Task.FromResult<IReadOnlyList<TaskHeader>>(list);
    }

    public Task<TaskHeader?> GetAsync(string id, CancellationToken ct)
    {
        foreach (var s in Enum.GetValues<TaskState>())
        {
            var path = PathFor(s, id);
            if (File.Exists(path))
            {
                var doc = ReadDoc(path);
                return Task.FromResult(doc?.Header);
            }
        }
        return Task.FromResult<TaskHeader?>(null);
    }

    public Task<int> PurgeAsync(TaskState state, CancellationToken ct)
    {
        var dir = DirFor(state);
        if (!Directory.Exists(dir)) return Task.FromResult(0);
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try { File.Delete(f); n++; } catch { }
        }
        return Task.FromResult(n);
    }

    public async Task SaveCheckpointAsync(string id, TaskCheckpoint checkpoint, CancellationToken ct)
    {
        await _checkpointLock.WaitAsync(ct);
        try
        {
            if (!TryFindDoc(id, out var state, out var path, out var doc) || doc is null)
            {
                _logger.LogWarning("save checkpoint: task {Id} not found", id);
                return;
            }
            await WriteAtomicAsync(path, doc with { Checkpoint = checkpoint }, ct);
        }
        finally { _checkpointLock.Release(); }
    }

    public Task<TaskCheckpoint?> GetCheckpointAsync(string id, CancellationToken ct)
    {
        TryFindDoc(id, out _, out _, out var doc);
        return Task.FromResult(doc?.Checkpoint);
    }

    public Task<bool> MarkInterruptedAsync(string id, string note, CancellationToken ct) =>
        MoveAsync(id, TaskState.Running, TaskState.Interrupted, h => h with
        {
            State = TaskState.Interrupted,
            CompletedUtc = DateTime.UtcNow,
            Outcome = new TaskOutcome(false, h.Outcome?.HasAnyChanges ?? false, h.Outcome?.PerRepo ?? [], note),
        }, ct);

    public Task<bool> RequeueInterruptedAsync(string id, CancellationToken ct) =>
        MoveAsync(id, TaskState.Interrupted, TaskState.Pending, h => h with
        {
            State = TaskState.Pending,
            CompletedUtc = null,
            Outcome = null,
        }, ct);

    public Task<bool> FailInterruptedAsync(string id, string reason, CancellationToken ct) =>
        MoveAsync(id, TaskState.Interrupted, TaskState.Failed, h => h with
        {
            State = TaskState.Failed,
            CompletedUtc = DateTime.UtcNow,
            Outcome = new TaskOutcome(false, false, h.Outcome?.PerRepo ?? [], reason),
        }, ct);

    private async Task<bool> MoveAsync(string id, TaskState from, TaskState to, Func<TaskHeader, TaskHeader> mutate, CancellationToken ct)
    {
        await _checkpointLock.WaitAsync(ct);
        try
        {
            var src = PathFor(from, id);
            if (!File.Exists(src)) { _logger.LogWarning("move {Id}: not in {From}", id, from); return false; }
            var doc = ReadDoc(src);
            if (doc is null) return false;

            var newDoc = doc with { Header = mutate(doc.Header) };
            var dest = PathFor(to, id);
            await WriteAtomicAsync(dest, newDoc, ct);
            try { if (!string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) File.Delete(src); } catch { }

            _logger.LogInformation("Task {Id} {From} -> {To}", id, from, to);
            StateChanged?.Invoke(newDoc.Header);
            return true;
        }
        finally { _checkpointLock.Release(); }
    }

    private bool TryFindDoc(string id, out TaskState state, out string path, out StoredTask? doc)
    {
        foreach (var s in Enum.GetValues<TaskState>())
        {
            var p = PathFor(s, id);
            if (File.Exists(p))
            {
                state = s;
                path = p;
                doc = ReadDoc(p);
                return doc is not null;
            }
        }
        state = default;
        path = "";
        doc = null;
        return false;
    }

    private string DirFor(TaskState s) => Path.Combine(_layout.TasksDir, s.ToString().ToLowerInvariant());
    private string PathFor(TaskState s, string id) => Path.Combine(DirFor(s), $"{id}.json");

    private static StoredTask? ReadDoc(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<StoredTask>(fs, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteAtomicAsync(string path, StoredTask doc, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, doc, JsonOpts, ct);
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    private sealed record StoredTask(TaskHeader Header, string Prompt, TaskCheckpoint? Checkpoint = null);
}
