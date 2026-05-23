using Orc.Core.Repos;

namespace Orc.Core.Pipeline;

internal sealed class CreateBranchStage : IStage
{
    public string Name => "CreateBranch";

    private readonly IGitClient _git;

    public CreateBranchStage(IGitClient git) => _git = git;

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var r = await _git.CreateBranchAsync(ctx.Repo, ctx.BranchName, ct);
        ctx.Transcript.AppendLine($"--- branch {ctx.BranchName} success={r.Success} ---");
        ctx.Transcript.Append(r.Output);
        return r.Success ? StageResult.Ok : StageResult.Abort("create-branch failed");
    }
}
