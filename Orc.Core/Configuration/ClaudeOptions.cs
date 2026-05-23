namespace Orc.Core.Configuration;

public sealed class ClaudeOptions
{
    public const string Section = "Claude";

    public string? BinaryHint { get; set; }
    public string PermissionMode { get; set; } = "acceptEdits";
    public List<string> DefaultAllowedTools { get; set; } =
    [
        "Edit", "Write", "MultiEdit", "Read", "Glob", "Grep",
        "Bash(dotnet build:*)", "Bash(dotnet test:*)",
    ];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
}
