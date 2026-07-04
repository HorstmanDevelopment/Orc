using System.Text;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Pipeline;

public sealed class PipelineContext
{
    public required TaskRecord Task { get; init; }
    public required RepoEntry Repo { get; init; }
    public required string BranchName { get; init; }
    public required string Stamp { get; init; }

    /// <summary>True when this run is resuming a previously-interrupted task on this repo.</summary>
    public bool IsResuming { get; init; }

    /// <summary>Prior Claude session to resume (from the checkpoint), if any.</summary>
    public string? ResumeSessionId { get; init; }

    public StringBuilder Transcript { get; } = new();
    public bool HasChanges { get; set; }
    public int ClaudeExitCode { get; set; }
    public string? PerRepoLogPath { get; set; }

    /// <summary>Claude session id captured during the RunClaude stage (persisted for resume).</summary>
    public string? ClaudeSessionId { get; set; }
}
