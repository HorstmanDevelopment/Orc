using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Core.Configuration;
using Orc.Core.Process;

namespace Orc.Core.Claude;

internal sealed class ClaudeClient : IClaudeClient
{
    private readonly IProcessRunner _runner;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeClient> _logger;
    private readonly string _resolvedBinary;

    public ClaudeClient(
        IProcessRunner runner,
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeClient> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
        _resolvedBinary = _options.BinaryHint?.Trim() is { Length: > 0 } hint ? hint : "claude";
    }

    public async Task<ClaudeRunResult> RunAsync(
        string repoPath,
        string prompt,
        IReadOnlyList<string>? allowedTools,
        CancellationToken ct)
    {
        EnsureSettingsFile(repoPath);
        var tools = allowedTools ?? _options.DefaultAllowedTools;
        var (exe, args) = BuildCommand(prompt, tools);
        var pr = await _runner.RunAsync(exe, args, repoPath, _options.Timeout, ct);
        _logger.LogInformation(
            "Claude in {Repo} exit={Exit} stdOut={StdOut}B stdErr={StdErr}B",
            repoPath, pr.ExitCode, pr.StdOut.Length, pr.StdErr.Length);
        return new ClaudeRunResult(pr.ExitCode, pr.StdOut, pr.StdErr);
    }

    private (string Exe, List<string> Args) BuildCommand(string prompt, IReadOnlyList<string> tools)
    {
        var claudeArgs = new List<string>
        {
            "-p", prompt,
            "--permission-mode", _options.PermissionMode,
            "--allowedTools", string.Join(" ", tools),
        };

        if (OperatingSystem.IsWindows() && !Path.IsPathRooted(_resolvedBinary))
        {
            var winArgs = new List<string> { "/c", _resolvedBinary };
            winArgs.AddRange(claudeArgs);
            return ("cmd.exe", winArgs);
        }
        return (_resolvedBinary, claudeArgs);
    }

    private static void EnsureSettingsFile(string repoPath)
    {
        var path = Path.Combine(repoPath, ".claude", "settings.json");
        if (File.Exists(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, LoadEmbeddedSettings());
    }

    private static string LoadEmbeddedSettings()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("claude-settings.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var sr = new StreamReader(s);
        return sr.ReadToEnd();
    }
}
