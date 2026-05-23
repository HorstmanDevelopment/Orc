using Orc.Core.Repos;

namespace Orc.Core.Pipeline;

internal sealed class CommitStage : IStage
{
    public string Name => "Commit";

    private readonly IGitClient _git;

    public CommitStage(IGitClient git) => _git = git;

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var msg = $"Orc task {ctx.Task.Id}";
        var r = await _git.CommitAllAsync(ctx.Repo, msg, ct);
        ctx.Transcript.AppendLine($"--- commit success={r.Success} hasChanges={r.HasChanges} ---");
        ctx.Transcript.Append(r.Output);
        ctx.HasChanges = r.HasChanges;
        return r.Success ? StageResult.Ok : StageResult.Abort("commit failed");
    }
}
