namespace Orc.Core.Configuration;

public sealed class WorkspaceOptions
{
    public const string Section = "Workspace";

    public string Root { get; set; } = "./workspace";
}
