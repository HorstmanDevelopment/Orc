namespace Orc.Core.Repos;

public sealed record GitOpResult(bool Success, string Output);
public sealed record GitUnmergedResult(bool Ok, IReadOnlyList<string> Branches, string Output);
public sealed record GitCommitResult(bool Success, bool HasChanges, string Output);

public interface IGitClient
{
    Task<GitOpResult> RefreshAsync(RepoEntry repo, string reposDir, CancellationToken ct);
    Task<GitUnmergedResult> FindUnmergedBranchesAsync(RepoEntry repo, string branchPrefix, CancellationToken ct);
    Task<GitOpResult> CreateBranchAsync(RepoEntry repo, string branchName, CancellationToken ct);
    Task<GitCommitResult> CommitAllAsync(RepoEntry repo, string message, CancellationToken ct);
}
