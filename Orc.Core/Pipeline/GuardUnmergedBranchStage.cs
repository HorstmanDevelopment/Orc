using Orc.Core.Repos;

namespace Orc.Core.Pipeline;

internal sealed class GuardUnmergedBranchStage : IStage
{
    public const string BranchPrefix = "orc-task";
    public string Name => "GuardUnmerged";

    private readonly IGitClient _git;

    public GuardUnmergedBranchStage(IGitClient git) => _git = git;

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        // When resuming, the task's own preserved branch is legitimately un-merged — exclude it.
        var excludeBranch = ctx.IsResuming ? ctx.BranchName : null;
        var r = await _git.FindUnmergedBranchesAsync(ctx.Repo, BranchPrefix, ct, excludeBranch);
        ctx.Transcript.AppendLine($"--- guard ok={r.Ok} found={r.Branches.Count} ---");
        ctx.Transcript.Append(r.Output);
        if (!r.Ok) return StageResult.Abort("unmerged-branch check failed");
        if (r.Branches.Count > 0)
            return StageResult.Abort(
                $"un-merged orc-task branch(es) in {ctx.Repo.Name}: {string.Join(", ", r.Branches)}");
        return StageResult.Ok;
    }
}
