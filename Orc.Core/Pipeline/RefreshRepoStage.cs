using Orc.Core.Configuration;
using Orc.Core.Repos;

namespace Orc.Core.Pipeline;

internal sealed class RefreshRepoStage : IStage
{
    public string Name => "Refresh";

    private readonly IGitClient _git;
    private readonly WorkspaceLayout _layout;

    public RefreshRepoStage(IGitClient git, WorkspaceLayout layout)
    {
        _git = git;
        _layout = layout;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var r = await _git.RefreshAsync(ctx.Repo, _layout.ReposDir, ct);
        ctx.Transcript.AppendLine($"--- refresh success={r.Success} ---");
        ctx.Transcript.Append(r.Output);
        return r.Success ? StageResult.Ok : StageResult.Abort("refresh failed");
    }
}
