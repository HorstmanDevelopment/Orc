using System.Text;
using Orc.Core.Process;

namespace Orc.Core.Repos;

internal sealed class GitClient : IGitClient
{
    private readonly IProcessRunner _runner;

    public GitClient(IProcessRunner runner) => _runner = runner;

    public async Task<GitOpResult> RefreshAsync(RepoEntry repo, string reposDir, CancellationToken ct)
    {
        var buf = new StringBuilder();
        var isClone = !Directory.Exists(Path.Combine(repo.LocalPath, ".git"));

        if (isClone)
        {
            if (string.IsNullOrWhiteSpace(repo.Url))
            {
                buf.AppendLine($"--- refresh failed: local-only repo missing at {repo.LocalPath} (re-create it) ---");
                return new GitOpResult(false, buf.ToString());
            }
            var ok = await StepAsync(buf, reposDir,
                ["clone", "--branch", repo.BaseBranch, repo.Url, repo.Name],
                $"clone {repo.Url}#{repo.BaseBranch}", ct);
            return new GitOpResult(ok, buf.ToString());
        }

        var remotes = await _runner.RunAsync("git", ["remote"], repo.LocalPath, null, ct);
        buf.AppendLine($"--- remote exit={remotes.ExitCode} ---");
        buf.Append(remotes.Combined);
        if (!remotes.Success) return new GitOpResult(false, buf.ToString());

        if (string.IsNullOrWhiteSpace(remotes.StdOut))
        {
            if (!await StepAsync(buf, repo.LocalPath, ["checkout", repo.BaseBranch], $"checkout {repo.BaseBranch}", ct))
                return new GitOpResult(false, buf.ToString());
            buf.AppendLine("--- fetch/pull skipped: no remotes configured ---");
            return new GitOpResult(true, buf.ToString());
        }

        if (!await StepAsync(buf, repo.LocalPath, ["fetch", "--all", "--prune"], "fetch", ct))
            return new GitOpResult(false, buf.ToString());
        if (!await StepAsync(buf, repo.LocalPath, ["checkout", repo.BaseBranch], $"checkout {repo.BaseBranch}", ct))
            return new GitOpResult(false, buf.ToString());

        var pulled = await StepAsync(buf, repo.LocalPath, ["pull", "--ff-only"], "pull", ct);
        return new GitOpResult(pulled, buf.ToString());
    }

    public async Task<GitOpResult> InitLocalAsync(string name, string baseBranch, string reposDir, CancellationToken ct)
    {
        var buf = new StringBuilder();
        var path = Path.Combine(reposDir, name);

        if (Directory.Exists(path))
        {
            buf.AppendLine($"--- init aborted: {path} already exists ---");
            return new GitOpResult(false, buf.ToString());
        }

        Directory.CreateDirectory(path);
        buf.AppendLine($"--- created {path} ---");

        if (!await StepAsync(buf, path, ["init", "-b", baseBranch], $"init -b {baseBranch}", ct))
            return new GitOpResult(false, buf.ToString());

        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), $"# {name}\n", ct);
        buf.AppendLine("--- seeded README.md ---");

        if (!await StepAsync(buf, path, ["add", "README.md"], "add README.md", ct))
            return new GitOpResult(false, buf.ToString());

        if (!await StepAsync(buf, path, ["commit", "-m", "Initial commit", "--no-verify"], "commit", ct))
            return new GitOpResult(false, buf.ToString());

        return new GitOpResult(true, buf.ToString());
    }

    public async Task<GitUnmergedResult> FindUnmergedBranchesAsync(RepoEntry repo, string branchPrefix, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitUnmergedResult(false, [], $"Not a git repo: {repo.LocalPath}");

        var pr = await _runner.RunAsync("git",
            ["branch", "--no-merged", repo.BaseBranch], repo.LocalPath, null, ct);
        if (!pr.Success) return new GitUnmergedResult(false, [], pr.Combined);

        var branches = pr.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimStart('*', ' ').Trim())
            .Where(l => l.Length > 0 && l.Contains(branchPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new GitUnmergedResult(true, branches, pr.Combined);
    }

    public async Task<GitOpResult> CreateBranchAsync(RepoEntry repo, string branchName, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitOpResult(false, $"Not a git repo: {repo.LocalPath}");

        var pr = await _runner.RunAsync("git",
            ["checkout", "-b", branchName, repo.BaseBranch], repo.LocalPath, null, ct);
        return new GitOpResult(pr.Success, pr.Combined);
    }

    public async Task<GitCommitResult> CommitAllAsync(RepoEntry repo, string message, CancellationToken ct)
    {
        var buf = new StringBuilder();
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitCommitResult(false, false, $"Not a git repo: {repo.LocalPath}");

        var status = await _runner.RunAsync("git", ["status", "--porcelain"], repo.LocalPath, null, ct);
        buf.AppendLine($"--- status exit={status.ExitCode} ---");
        buf.Append(status.Combined);
        if (!status.Success) return new GitCommitResult(false, false, buf.ToString());

        if (string.IsNullOrWhiteSpace(status.StdOut))
        {
            buf.AppendLine("--- commit skipped: no changes ---");
            return new GitCommitResult(true, false, buf.ToString());
        }

        if (!await StepAsync(buf, repo.LocalPath, ["add", "-A"], "add -A", ct))
            return new GitCommitResult(false, true, buf.ToString());

        if (!await StepAsync(buf, repo.LocalPath, ["commit", "-m", message, "--no-verify"], "commit", ct))
            return new GitCommitResult(false, true, buf.ToString());

        return new GitCommitResult(true, true, buf.ToString());
    }

    private async Task<bool> StepAsync(StringBuilder buf, string cwd, IReadOnlyList<string> args, string label, CancellationToken ct)
    {
        var pr = await _runner.RunAsync("git", args, cwd, null, ct);
        buf.AppendLine($"--- {label} exit={pr.ExitCode} ---");
        buf.Append(pr.Combined);
        return pr.Success;
    }
}
