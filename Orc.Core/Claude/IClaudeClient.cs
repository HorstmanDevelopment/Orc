namespace Orc.Core.Claude;

public sealed record ClaudeRunResult(int ExitCode, string StdOut, string StdErr, string? SessionId = null)
{
    public bool Success => ExitCode == 0;
    public string Transcript => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}{Environment.NewLine}{StdErr}";
}

public interface IClaudeClient
{
    /// <param name="resumeSessionId">When set, resume that prior Claude session instead of starting fresh.</param>
    /// <param name="onSessionId">Invoked once, as soon as the session id is known (even mid-run), so it can be persisted before a possible kill.</param>
    Task<ClaudeRunResult> RunAsync(
        string repoPath,
        string prompt,
        IReadOnlyList<string>? allowedTools,
        CancellationToken ct,
        string? resumeSessionId = null,
        Action<string>? onSessionId = null);
}
