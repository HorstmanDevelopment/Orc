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

    public StringBuilder Transcript { get; } = new();
    public bool HasChanges { get; set; }
    public int ClaudeExitCode { get; set; }
    public string? PerRepoLogPath { get; set; }
}
