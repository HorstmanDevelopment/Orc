using Orc.Core.Repos;

namespace Orc.Core.Pipeline;

/// <summary>
/// Final stage: decide what happens to the task's orc-task branch.
///   · no changes        -> delete the empty branch (avoids littering the repo and
///                          tripping GuardUnmerged on later tasks)
///   · changes, automerge -> merge the branch into the base branch and delete it
///   · changes, review    -> leave the branch un-merged for a human to merge
/// Runs only when every earlier stage succeeded, so Claude ran and the commit landed.
/// </summary>
internal sealed class MergeStage : IStage
{
    public string Name => "Merge";

    private readonly IGitClient _git;

    public MergeStage(IGitClient git) => _git = git;

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        if (!ctx.HasChanges)
        {
            var d = await _git.DeleteBranchAsync(ctx.Repo, ctx.BranchName, ct);
            ctx.Transcript.AppendLine($"--- merge skipped: no changes, deleted empty branch (ok={d.Success}) ---");
            ctx.Transcript.Append(d.Output);
            return StageResult.Ok;
        }

        if (!ctx.Repo.AutoMerge)
        {
            ctx.Transcript.AppendLine($"--- merge skipped: automerge=off, left {ctx.BranchName} for review ---");
            return StageResult.Ok;
        }

        var r = await _git.MergeBranchAsync(ctx.Repo, ctx.BranchName, ct);
        ctx.Transcript.AppendLine($"--- merge success={r.Success} branch={ctx.BranchName} ---");
        ctx.Transcript.Append(r.Output);
        return r.Success
            ? StageResult.Ok
            : StageResult.Abort($"merge of {ctx.BranchName} into {ctx.Repo.BaseBranch} failed (conflict?); branch left for manual resolution");
    }
}
