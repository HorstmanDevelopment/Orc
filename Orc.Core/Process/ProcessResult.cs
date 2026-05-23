namespace Orc.Core.Process;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}{Environment.NewLine}{StdErr}";
}
