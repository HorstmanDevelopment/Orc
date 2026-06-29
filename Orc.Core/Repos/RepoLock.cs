using System.Collections.Concurrent;

namespace Orc.Core.Repos;

public interface IRepoLock
{
    Task<IAsyncDisposable> AcquireAsync(string repoName, CancellationToken ct);
}

internal sealed class RepoLock : IRepoLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IAsyncDisposable> AcquireAsync(string repoName, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(repoName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                sem.Release();
            return ValueTask.CompletedTask;
        }
    }
}
