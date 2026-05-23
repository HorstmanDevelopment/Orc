namespace Orc.Core.Claude;

public sealed record ClaudeRunResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Transcript => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}{Environment.NewLine}{StdErr}";
}

public interface IClaudeClient
{
    Task<ClaudeRunResult> RunAsync(
        string repoPath,
        string prompt,
        IReadOnlyList<string>? allowedTools,
        CancellationToken ct);
}
