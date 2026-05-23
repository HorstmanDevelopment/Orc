namespace Orc.Core.Orchitect;

public sealed class RepoState
{
    public string RepoName { get; set; } = "";
    public DateTime? LastAnalyzedUtc { get; set; }
    public List<Enhancement> Enhancements { get; set; } = [];
}

public sealed class Enhancement
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Rationale { get; set; } = "";
    public int Priority { get; set; } = 3;
    public EnhancementStatus Status { get; set; } = EnhancementStatus.Identified;
    public List<EnhancementStep> Steps { get; set; } = [];
}

public sealed class EnhancementStep
{
    public int N { get; set; }
    public string Prompt { get; set; } = "";
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? TaskId { get; set; }
    public bool HasChanges { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
