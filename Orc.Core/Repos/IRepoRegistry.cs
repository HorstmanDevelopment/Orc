namespace Orc.Core.Repos;

public interface IRepoRegistry
{
    IReadOnlyList<RepoEntry> All();
    bool Contains(string url);
    bool ContainsName(string name);
    bool TryResolve(string spec, out IReadOnlyList<RepoEntry> repos, out string? error);
    Task AddAsync(string url, string branch, CancellationToken ct, string? mission = null);
    Task AddLocalAsync(string name, string branch, CancellationToken ct, string? mission = null);
    Task UpdateMissionAsync(string name, string? mission, CancellationToken ct);
    string SourcePath { get; }
}
