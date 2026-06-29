using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Core.Claude;
using Orc.Core.Configuration;
using Orc.Core.Repos;

namespace Orc.Core.Orchitect;

public sealed class StepPlanResult
{
    public bool IsComplete { get; set; }
    public string Step { get; set; } = "";
}

internal sealed class StepPlanner
{
    private readonly IClaudeClient _claude;
    private readonly IRepoLock _repoLock;
    private readonly OrchitectOptions _options;
    private readonly ILogger<StepPlanner> _logger;

    public StepPlanner(IClaudeClient claude, IRepoLock repoLock, IOptions<OrchitectOptions> options, ILogger<StepPlanner> logger)
    {
        _claude = claude;
        _repoLock = repoLock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StepPlanResult> PlanAsync(string repoName, string repoPath, Enhancement enh, string? mission, CancellationToken ct)
    {
        await using var repoLockHandle = await _repoLock.AcquireAsync(repoName, ct);

        var outDir = Path.Combine(repoPath, AnalysisRunner.OutputDirName);
        ClaudeOutputDir.Reset(outDir);

        var prior = enh.Steps.Count == 0
            ? "(none)"
            : string.Join("\n", enh.Steps.Select(s =>
                $"- Step {s.N} ({s.Status}, hasChanges={s.HasChanges}): {s.Prompt}"));

        var body = $$"""
            You are planning the next incremental step toward an enhancement.

            Title: {{enh.Title}}
            Details:
            {{enh.Rationale}}

            Previously planned/completed steps:
            {{prior}}

            Read the current state of the codebase using Read, Glob, and Grep.

            If this enhancement is already complete in the current code, write NO files to `./{{AnalysisRunner.OutputDirName}}/` and exit. Otherwise, write a SINGLE file under `./{{AnalysisRunner.OutputDirName}}/` whose contents are the prompt for the next focused step. The content is free-form text that will be passed verbatim to a subsequent Claude run as task instructions.

            The step must be:
            - Small enough to complete in one focused pass
            - Concrete (mention specific files and functions where possible)
            - A meaningful step forward, not the whole enhancement at once

            Constraints:
            - Do NOT print structured output to stdout.
            - Do NOT write files anywhere except under `./{{AnalysisRunner.OutputDirName}}/`.
            - Do NOT create subdirectories inside `./{{AnalysisRunner.OutputDirName}}/`.
            """;
        body = body.Replace("\n"," ");

        var prompt = body+" The mission for this repository is :"+mission;//MissionPreamble.BuildStepPrompt(mission, body);
        
        _ = await _claude.RunAsync(repoPath, prompt, _options.AnalysisTools, ct);

        var files = ClaudeOutputDir.ListFiles(outDir);
        var usable = new List<string>();
        foreach (var f in files)
        {
            string content;
            try { content = await File.ReadAllTextAsync(f, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "could not read {File}; skipping", f);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(content)) usable.Add(content.Trim());
        }

        ClaudeOutputDir.Reset(outDir);

        if (usable.Count == 0) return new StepPlanResult { IsComplete = true };

        if (usable.Count > 1)
            _logger.LogWarning("step planner wrote {Count} files; using the first", usable.Count);

        return new StepPlanResult { IsComplete = false, Step = usable[0] };
    }
}
