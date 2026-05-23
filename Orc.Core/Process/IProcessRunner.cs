namespace Orc.Core.Process;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDir,
        TimeSpan? timeout,
        CancellationToken ct);
}
