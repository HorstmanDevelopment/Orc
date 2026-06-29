namespace Orc.Core.Repos;

public sealed record GitOpResult(bool Success, string Output);
public sealed record GitUnmergedResult(bool Ok, IReadOnlyList<string> Branches, string Output);
public sealed record GitCommitResult(bool Success, bool HasChanges, string Output);

public interface IGitClient
{
    Task<GitOpResult> RefreshAsync(RepoEntry repo, string reposDir, CancellationToken ct);
    Task<GitOpResult> InitLocalAsync(string name, string baseBranch, string reposDir, CancellationToken ct);
    Task<GitUnmergedResult> FindUnmergedBranchesAsync(RepoEntry repo, string branchPrefix, CancellationToken ct);
    Task<GitOpResult> CreateBranchAsync(RepoEntry repo, string branchName, CancellationToken ct);
    Task<GitCommitResult> CommitAllAsync(RepoEntry repo, string message, CancellationToken ct);

    /// <summary>
    /// Integrate a task's feature branch into the base branch: checkout base, merge the
    /// branch (no-ff), and on success delete the branch. On merge conflict the merge is
    /// aborted and the branch is left intact for manual resolution (Success = false).
    /// </summary>
    Task<GitOpResult> MergeBranchAsync(RepoEntry repo, string branchName, CancellationToken ct);

    /// <summary>
    /// Checkout the base branch and force-delete <paramref name="branchName"/>. Used to
    /// discard an empty (no-change) feature branch.
    /// </summary>
    Task<GitOpResult> DeleteBranchAsync(RepoEntry repo, string branchName, CancellationToken ct);

    /// <summary>
    /// Roll a repo back after a cancelled task: force-checkout the base branch,
    /// reset the working tree, and delete any orc-task/{taskId}* feature branches.
    /// </summary>
    Task<GitOpResult> AbortTaskAsync(RepoEntry repo, string taskId, CancellationToken ct);
}
