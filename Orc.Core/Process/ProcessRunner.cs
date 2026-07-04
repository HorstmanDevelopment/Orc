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
        CancellationToken ct,
        string? stdin = null,
        Action<string>? onStdoutLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdOut.AppendLine(e.Data);
            // Live line callback: lets callers capture streamed data (e.g. a Claude
            // session id) the instant it arrives, so it survives a later kill.
            try { onStdoutLine?.Invoke(e.Data); } catch { }
        };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        _logger.LogDebug("Run {File} {Args} (cwd={Cwd})", fileName, string.Join(' ', args), workingDir);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin is not null)
        {
            // Feed input (e.g. a large prompt that would overflow the command line) then
            // close the stream so the child sees EOF and proceeds.
            try
            {
                await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
                await proc.StandardInput.FlushAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stdErr.AppendLine($"[stdin write failed: {ex.Message}]");
            }
            finally
            {
                try { proc.StandardInput.Close(); } catch { }
            }
        }

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
