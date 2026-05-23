namespace Orc.Core.Configuration;

public sealed class OrchitectOptions
{
    public const string Section = "Orchitect";

    public int MaxModificationsPerDay { get; set; } = 5;
    public int MaxModificationsPerRepoPerDay { get; set; } = 2;
    public int ConsecutiveFailureLimit { get; set; } = 2;
    public List<string> ReadOnlyTools { get; set; } = ["Read", "Glob", "Grep"];
}
