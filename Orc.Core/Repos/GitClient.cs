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

        ScrubReservedNames(buf, repo.LocalPath, status.StdOut);

        if (!await StepAsync(buf, repo.LocalPath, ["add", "-A"], "add -A", ct))
            return new GitCommitResult(false, true, buf.ToString());

        if (!await StepAsync(buf, repo.LocalPath, ["commit", "-m", message, "--no-verify"], "commit", ct))
            return new GitCommitResult(false, true, buf.ToString());

        return new GitCommitResult(true, true, buf.ToString());
    }

    public async Task<GitOpResult> MergeBranchAsync(RepoEntry repo, string branchName, CancellationToken ct)
    {
        var buf = new StringBuilder();
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitOpResult(false, $"Not a git repo: {repo.LocalPath}");

        if (!await StepAsync(buf, repo.LocalPath, ["checkout", repo.BaseBranch], $"checkout {repo.BaseBranch}", ct))
            return new GitOpResult(false, buf.ToString());

        var merged = await StepAsync(buf, repo.LocalPath,
            ["merge", "--no-ff", "-m", $"Merge {branchName}", branchName], $"merge {branchName}", ct);
        if (!merged)
        {
            // Leave base clean and the branch intact for manual resolution.
            await StepAsync(buf, repo.LocalPath, ["merge", "--abort"], "merge --abort", ct);
            return new GitOpResult(false, buf.ToString());
        }

        await StepAsync(buf, repo.LocalPath, ["branch", "-D", branchName], $"branch -D {branchName}", ct);
        return new GitOpResult(true, buf.ToString());
    }

    public async Task<GitOpResult> DeleteBranchAsync(RepoEntry repo, string branchName, CancellationToken ct)
    {
        var buf = new StringBuilder();
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitOpResult(false, $"Not a git repo: {repo.LocalPath}");

        // Can't delete the branch we're on; park on base first.
        if (!await StepAsync(buf, repo.LocalPath, ["checkout", repo.BaseBranch], $"checkout {repo.BaseBranch}", ct))
            return new GitOpResult(false, buf.ToString());

        var deleted = await StepAsync(buf, repo.LocalPath, ["branch", "-D", branchName], $"branch -D {branchName}", ct);
        return new GitOpResult(deleted, buf.ToString());
    }

    public async Task<GitOpResult> AbortTaskAsync(RepoEntry repo, string taskId, CancellationToken ct)
    {
        var buf = new StringBuilder();
        if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
            return new GitOpResult(false, $"Not a git repo: {repo.LocalPath}");

        // Force-checkout base: discards any uncommitted edits Claude was mid-writing.
        await StepAsync(buf, repo.LocalPath, ["checkout", "--force", repo.BaseBranch], $"checkout --force {repo.BaseBranch}", ct);
        await StepAsync(buf, repo.LocalPath, ["reset", "--hard"], "reset --hard", ct);
        // reset --hard only restores tracked files; clean removes untracked debris Claude
        // left behind (new src/, tests/, .claude/, claude_output/, etc.).
        await StepAsync(buf, repo.LocalPath, ["clean", "-fd"], "clean -fd", ct);
        // git clean can't delete Windows reserved-name files (e.g. a stray "nul"); sweep them.
        ScrubReservedFiles(buf, repo.LocalPath);

        var listed = await _runner.RunAsync("git",
            ["branch", "--list", $"orc-task/{taskId}*"], repo.LocalPath, null, ct);
        buf.AppendLine($"--- branch --list orc-task/{taskId}* exit={listed.ExitCode} ---");
        buf.Append(listed.Combined);

        var branches = listed.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.TrimStart('*', ' ').Trim())
            .Where(b => b.Length > 0)
            .ToArray();

        foreach (var b in branches)
            await StepAsync(buf, repo.LocalPath, ["branch", "-D", b], $"branch -D {b}", ct);

        return new GitOpResult(true, buf.ToString());
    }

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Force-delete any Windows reserved-name files (CON, NUL, COM1, ...) left in the
    // working tree. git refuses to remove these, so we delete them directly via the
    // extended-length ("\\?\") path which bypasses Win32 reserved-name handling.
    private static void ScrubReservedFiles(StringBuilder buf, string repoPath)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(repoPath)) return;

        IEnumerable<string> files;
        try
        {
            var gitDir = Path.Combine(repoPath, ".git") + Path.DirectorySeparatorChar;
            files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase))
                .Where(f => WindowsReservedNames.Contains(Path.GetFileNameWithoutExtension(f)));
        }
        catch (Exception ex)
        {
            buf.AppendLine($"--- reserved-name sweep failed to enumerate: {ex.Message} ---");
            return;
        }

        foreach (var f in files)
        {
            try
            {
                File.Delete(@"\\?\" + Path.GetFullPath(f));
                buf.AppendLine($"--- scrubbed reserved-name file: {Path.GetRelativePath(repoPath, f)} ---");
            }
            catch (Exception ex)
            {
                buf.AppendLine($"--- could not delete reserved-name file {Path.GetRelativePath(repoPath, f)}: {ex.Message} ---");
            }
        }
    }

    private static void ScrubReservedNames(StringBuilder buf, string repoPath, string porcelainStdOut)
    {
        foreach (var rawLine in porcelainStdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4) continue;
            var path = rawLine[3..].Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            if (string.IsNullOrEmpty(path)) continue;

            var baseName = Path.GetFileNameWithoutExtension(path);
            if (!WindowsReservedNames.Contains(baseName)) continue;

            var fullPath = Path.Combine(repoPath, path);
            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                buf.AppendLine($"--- scrubbed reserved-name file: {path} ---");
            }
            catch (Exception ex)
            {
                buf.AppendLine($"--- could not delete reserved-name file {path}: {ex.Message} ---");
            }
        }
    }

    private async Task<bool> StepAsync(StringBuilder buf, string cwd, IReadOnlyList<string> args, string label, CancellationToken ct)
    {
        var pr = await _runner.RunAsync("git", args, cwd, null, ct);
        buf.AppendLine($"--- {label} exit={pr.ExitCode} ---");
        buf.Append(pr.Combined);
        return pr.Success;
    }
}
