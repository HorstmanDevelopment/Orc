using System.Collections.Concurrent;

namespace Orc.Core.Tasks;

public interface IRunningTaskRegistry
{
    void Register(string taskId, CancellationTokenSource cts);
    void Unregister(string taskId);
    bool TryCancel(string taskId);
    bool IsRunning(string taskId);
}

internal sealed class RunningTaskRegistry : IRunningTaskRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _live =
        new(StringComparer.Ordinal);

    public void Register(string taskId, CancellationTokenSource cts) => _live[taskId] = cts;

    public void Unregister(string taskId) => _live.TryRemove(taskId, out _);

    public bool IsRunning(string taskId) => _live.ContainsKey(taskId);

    public bool TryCancel(string taskId)
    {
        if (!_live.TryGetValue(taskId, out var cts)) return false;
        try { cts.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }
}
