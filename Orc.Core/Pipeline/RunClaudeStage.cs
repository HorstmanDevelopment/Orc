using Orc.Core.Claude;

namespace Orc.Core.Pipeline;

internal sealed class RunClaudeStage : IStage
{
    public string Name => "RunClaude";

    private readonly IClaudeClient _claude;

    public RunClaudeStage(IClaudeClient claude) => _claude = claude;

    private const string ResumeNudge =
        "Continue and complete the task described earlier in this session. " +
        "If it is already complete, make no further changes and stop.";

    public async Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        // Capture the session id the moment claude reports it, so an interrupted run can
        // be resumed even if it is killed before finishing.
        void OnSession(string id) => ctx.ClaudeSessionId = id;

        ClaudeRunResult r;
        if (ctx.ResumeSessionId is { Length: > 0 } sid)
        {
            ctx.Transcript.AppendLine($"--- claude resume session={sid} ---");
            r = await _claude.RunAsync(ctx.Repo.LocalPath, ResumeNudge, allowedTools: null, ct,
                resumeSessionId: sid, onSessionId: OnSession);

            // The session may be gone/expired; fall back to a fresh run of the original prompt.
            if (!r.Success)
            {
                ctx.Transcript.AppendLine($"--- claude resume failed (exit {r.ExitCode}); retrying fresh ---");
                ctx.Transcript.AppendLine(r.Transcript);
                r = await _claude.RunAsync(ctx.Repo.LocalPath, ctx.Task.Prompt, allowedTools: null, ct,
                    onSessionId: OnSession);
            }
        }
        else
        {
            r = await _claude.RunAsync(ctx.Repo.LocalPath, ctx.Task.Prompt, allowedTools: null, ct,
                onSessionId: OnSession);
        }

        ctx.ClaudeSessionId ??= r.SessionId;
        ctx.Transcript.AppendLine($"--- claude exit={r.ExitCode} session={ctx.ClaudeSessionId ?? "-"} ---");
        ctx.Transcript.AppendLine(r.Transcript);
        ctx.ClaudeExitCode = r.ExitCode;
        return r.Success ? StageResult.Ok : new StageResult(false, false, $"claude exit {r.ExitCode}");
    }
}
