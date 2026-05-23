using System.Text.Json;
using Microsoft.Extensions.Options;
using Orc.Core.Claude;
using Orc.Core.Configuration;

namespace Orc.Core.Orchitect;

internal sealed class AnalysisRunner
{
    private const string PromptTemplate = """
        Analyze this codebase to identify enhancements. Read the project structure and key files using Read, Glob, and Grep.

        Output ONLY a JSON array (no commentary, no markdown fences) of 3-7 distinct enhancement suggestions in this exact format:

        [
          {
            "title": "short title",
            "rationale": "1-2 sentences explaining why this is valuable",
            "priority": 3
          }
        ]

        Each enhancement should be:
        - A meaningful improvement (code quality, missing features, testing, performance, developer experience)
        - Achievable through code changes only (no external infrastructure)
        - Distinct from the others

        Priority: 1 = highest, 5 = lowest. Do not include any text outside the JSON array.
        """;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IClaudeClient _claude;
    private readonly OrchitectOptions _options;

    public AnalysisRunner(IClaudeClient claude, IOptions<OrchitectOptions> options)
    {
        _claude = claude;
        _options = options.Value;
    }

    public async Task<(IReadOnlyList<Enhancement> Enhancements, string Raw)> RunAsync(string repoPath, CancellationToken ct)
    {
        var r = await _claude.RunAsync(repoPath, PromptTemplate, _options.ReadOnlyTools, ct);
        var raw = $"--- claude exit={r.ExitCode} ---\n{r.Transcript}";
        var json = JsonExtractor.ExtractFirstObjectOrArray(r.StdOut);
        if (json is null) return ([], raw);

        try
        {
            var items = JsonSerializer.Deserialize<List<Item>>(json, JsonOpts) ?? [];
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var result = new List<Enhancement>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (string.IsNullOrWhiteSpace(it.Title)) continue;
                result.Add(new Enhancement
                {
                    Id = $"enh-{stamp}-{i + 1:D2}",
                    Title = it.Title!.Trim(),
                    Rationale = (it.Rationale ?? "").Trim(),
                    Priority = it.Priority == 0 ? 3 : it.Priority,
                });
            }
            return (result, raw);
        }
        catch
        {
            return ([], raw);
        }
    }

    private sealed class Item
    {
        public string? Title { get; set; }
        public string? Rationale { get; set; }
        public int Priority { get; set; }
    }
}
