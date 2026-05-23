using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Orc.Core.Process;

internal sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger) => _logger = logger;

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDir,
        TimeSpan? timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        _logger.LogDebug("Run {File} {Args} (cwd={Cwd})", fileName, string.Join(' ', args), workingDir);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) linked.CancelAfter(t);

        try
        {
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            stdErr.AppendLine($"[killed: timeout {timeout}]");
        }

        return new ProcessResult(proc.HasExited ? proc.ExitCode : -1, stdOut.ToString(), stdErr.ToString());
    }
}
