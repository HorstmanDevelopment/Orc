using System.Text.Json;
using Microsoft.Extensions.Options;
using Orc.Core.Claude;
using Orc.Core.Configuration;

namespace Orc.Core.Orchitect;

public sealed class StepPlanResult
{
    public bool IsComplete { get; set; }
    public string Step { get; set; } = "";
}

internal sealed class StepPlanner
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IClaudeClient _claude;
    private readonly OrchitectOptions _options;

    public StepPlanner(IClaudeClient claude, IOptions<OrchitectOptions> options)
    {
        _claude = claude;
        _options = options.Value;
    }

    public async Task<StepPlanResult> PlanAsync(string repoPath, Enhancement enh, CancellationToken ct)
    {
        var prior = enh.Steps.Count == 0
            ? "(none)"
            : string.Join("\n", enh.Steps.Select(s =>
                $"- Step {s.N} ({s.Status}, hasChanges={s.HasChanges}): {s.Prompt}"));

        var prompt = $$"""
            You are planning the next incremental step toward this enhancement.

            Title: {{enh.Title}}
            Rationale: {{enh.Rationale}}

            Previously planned/completed steps:
            {{prior}}

            Read the current state of the codebase using Read, Glob, and Grep. Output ONLY JSON (no commentary, no markdown fences) in this exact format:

            {
              "isComplete": false,
              "step": "concrete instructions for ONE focused incremental change. Be specific about which files to touch and what changes to make. Keep the scope small."
            }

            Set isComplete=true only if the enhancement is already implemented in the current code. Otherwise, the step must be:
            - Small enough to complete in one focused pass
            - Concrete (mention specific files and functions where possible)
            - A meaningful step forward, not the whole enhancement at once

            Do not include any text outside the JSON object.
            """;

        var r = await _claude.RunAsync(repoPath, prompt, _options.ReadOnlyTools, ct);
        var json = JsonExtractor.ExtractFirstObjectOrArray(r.StdOut);
        if (json is null) return new StepPlanResult();

        try
        {
            return JsonSerializer.Deserialize<StepPlanResult>(json, JsonOpts) ?? new StepPlanResult();
        }
        catch
        {
            return new StepPlanResult();
        }
    }
}
