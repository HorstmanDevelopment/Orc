using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Core.Claude;
using Orc.Core.Configuration;

namespace Orc.Core.Orchitect;

internal sealed class AnalysisRunner
{
    public const string OutputDirName = "claude_output";
    private const string PromptBody = $"You are an automated code development plan generator. Scan the code base in this directory and identify next steps for development. For each step, create a separate file in the `./{OutputDirName}/` directory with a short kebab-case name describing the step like `short-description-of-step.txt`. The content should be a few sentences describing a high level summary of the step. Examples of steps could be basic building blocks to get the app running, new features, new content etc. Each should be relevant to the mission statement. Respond with a list of files created. If you can't write files, describe why.";
    private readonly IClaudeClient _claude;
    private readonly OrchitectOptions _options;
    private readonly ILogger<AnalysisRunner> _logger;

    public AnalysisRunner(IClaudeClient claude, IOptions<OrchitectOptions> options, ILogger<AnalysisRunner> logger)
    {
        _claude = claude;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<Enhancement> Enhancements, string Raw)> RunAsync(string repoPath, string? mission, CancellationToken ct)
    {
        var outDir = Path.Combine(repoPath, OutputDirName);
        ClaudeOutputDir.Reset(outDir);

        var prompt = PromptBody+" The plan for the code base is :"+mission;//MissionPreamble.BuildAnalysisPrompt(mission, PromptBody);
        var r = await _claude.RunAsync(repoPath, prompt, _options.AnalysisTools, ct);

        var files = ClaudeOutputDir.ListFiles(outDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var enhancements = new List<Enhancement>(files.Count);
        var manifest = new StringBuilder();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "could not read {File}; skipping", file);
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("skipping empty enhancement file {File}", file);
                continue;
            }

            var title = SanitizeTitle(Path.GetFileNameWithoutExtension(file));
            if (string.IsNullOrEmpty(title)) continue;

            enhancements.Add(new Enhancement
            {
                Id = $"enh-{stamp}-{enhancements.Count + 1:D2}",
                Title = title,
                Rationale = content.Trim(),
            });

            manifest.AppendLine($"{Path.GetFileName(file)} ({new FileInfo(file).Length}B)");
        }

        var raw = $"--- claude exit={r.ExitCode} files={enhancements.Count} ---\n{r.Transcript}\n--- manifest ---\n{manifest}";

        ClaudeOutputDir.Reset(outDir);
        return (enhancements, raw);
    }

    private static string SanitizeTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsControl(ch)) continue;
            sb.Append(ch);
        }
        var s = sb.ToString().Trim();
        return s.Length > 80 ? s[..80] : s;
    }
}
