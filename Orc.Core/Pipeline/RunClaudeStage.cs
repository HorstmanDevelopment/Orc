using Orc.Core.Claude;

namespace Orc.Core.Pipeline;

internal sealed class RunClaudeStage : IStage
{
    public string Name => "RunClaude";

    private readonly IClaudeClient _claude;

    public RunClaudeStage(IClaudeClient claude) => _claude = claude;

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var r = await _claude.RunAsync(ctx.Repo.LocalPath, ctx.Task.Prompt, allowedTools: null, ct);
        ctx.Transcript.AppendLine($"--- claude exit={r.ExitCode} ---");
        ctx.Transcript.AppendLine(r.Transcript);
        ctx.ClaudeExitCode = r.ExitCode;
        return r.Success ? StageResult.Ok : new StageResult(false, false, $"claude exit {r.ExitCode}");
    }
}
