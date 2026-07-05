namespace Orc.Core.Configuration;

public sealed class OrchestratorOptions
{
    public const string Section = "Orchestrator";

    /// <summary>
    /// Maximum number of tasks to run concurrently across all repos. Tasks are always
    /// serialized per repo (never two at a time in the same repo), so the effective
    /// ceiling is also the number of registered repos with queued work. 0 (the default)
    /// means no extra cap — run as many repos in parallel as have pending tasks.
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 0;
}
