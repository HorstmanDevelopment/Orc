namespace Orc.Core.Pipeline;

public interface IStage
{
    string Name { get; }
    Task<StageResult> ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}
