namespace Orc.Core.Pipeline;

public sealed record StageResult(bool Success, bool StopPipeline = false, string? Reason = null)
{
    public static StageResult Ok { get; } = new(true);
    public static StageResult Abort(string reason) => new(false, true, reason);
    public static StageResult Skip(string reason) => new(true, true, reason);
}
