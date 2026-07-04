using System.Reflection;
using System.Text;
using System.Text.Json;
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
        CancellationToken ct,
        string? resumeSessionId = null,
        Action<string>? onSessionId = null)
    {
        EnsureSettingsFile(repoPath);
        var tools = allowedTools ?? _options.DefaultAllowedTools;
        var (exe, args) = BuildCommand(tools, resumeSessionId);

        // stream-json emits an init line carrying the session_id at the very start, so we
        // can capture (and persist) it the instant it arrives — before any later kill.
        var parser = new StreamJsonParser(id =>
        {
            if (id is { Length: > 0 }) onSessionId?.Invoke(id);
        });

        // The prompt goes over stdin, not the command line: orchitect prompts routinely
        // exceed the Windows ~8KB command-line limit ("The command line is too long").
        var pr = await _runner.RunAsync(exe, args, repoPath, _options.Timeout, ct,
            stdin: prompt, onStdoutLine: parser.Feed);

        var text = parser.ResultText ?? pr.StdOut;
        _logger.LogInformation(
            "Claude in {Repo} exit={Exit} session={Session} resume={Resume} out={Out}B err={Err}B",
            repoPath, pr.ExitCode, parser.SessionId ?? "-", resumeSessionId ?? "-", text.Length, pr.StdErr.Length);
        return new ClaudeRunResult(pr.ExitCode, text, pr.StdErr, parser.SessionId);
    }

    private (string Exe, List<string> Args) BuildCommand(IReadOnlyList<string> tools, string? resumeSessionId)
    {
        // -p with no inline prompt makes claude read the prompt from stdin (see RunAsync).
        // stream-json + --verbose is required for streamed JSON events in print mode.
        var claudeArgs = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--permission-mode", _options.PermissionMode,
            "--allowedTools", string.Join(" ", tools),
        };
        if (resumeSessionId is { Length: > 0 })
        {
            claudeArgs.Add("--resume");
            claudeArgs.Add(resumeSessionId);
        }

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

    /// <summary>
    /// Parses Claude's <c>--output-format stream-json</c> NDJSON stream line-by-line:
    /// captures the session id (fired via callback the first time it's seen) and the
    /// final result text. Falls back to accumulated assistant text when the run is
    /// killed before the terminal <c>result</c> event. Tolerant of non-JSON lines.
    /// Invoked sequentially from the process's stdout pump — no internal locking needed.
    /// </summary>
    private sealed class StreamJsonParser
    {
        private readonly Action<string?> _onSessionId;
        private readonly StringBuilder _assistant = new();
        private string? _resultText;

        public StreamJsonParser(Action<string?> onSessionId) => _onSessionId = onSessionId;

        public string? SessionId { get; private set; }
        public string? ResultText =>
            _resultText ?? (_assistant.Length > 0 ? _assistant.ToString() : null);

        public void Feed(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { return; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;

                if (SessionId is null &&
                    root.TryGetProperty("session_id", out var sid) &&
                    sid.ValueKind == JsonValueKind.String)
                {
                    SessionId = sid.GetString();
                    _onSessionId(SessionId);
                }

                var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                if (type == "result" &&
                    root.TryGetProperty("result", out var res) &&
                    res.ValueKind == JsonValueKind.String)
                {
                    _resultText = res.GetString();
                }
                else if (type == "assistant" &&
                         root.TryGetProperty("message", out var msg) &&
                         msg.TryGetProperty("content", out var content) &&
                         content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                            _assistant.AppendLine(txt.GetString());
                    }
                }
            }
        }
    }
}
