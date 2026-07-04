namespace Orc.Core.Configuration;

public sealed class ResumeOptions
{
    public const string Section = "Resume";

    /// <summary>Re-queue interrupted tasks for resume automatically on startup.</summary>
    public bool AutoResume { get; set; } = true;

    /// <summary>Give up resuming (leave for manual handling) after this many attempts.</summary>
    public int MaxAttempts { get; set; } = 2;
}
