namespace Orc.Core.Repos;

public interface IRepoRegistry
{
    IReadOnlyList<RepoEntry> All();
    bool Contains(string url);
    bool TryResolve(string spec, out IReadOnlyList<RepoEntry> repos, out string? error);
    Task AddAsync(string url, string branch, CancellationToken ct);
    string SourcePath { get; }
}
