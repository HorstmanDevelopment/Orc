using Microsoft.Extensions.Options;

namespace Orc.Core.Configuration;

public sealed class WorkspaceLayout
{
    public string Root { get; }
    public string ConfigDir => Path.Combine(Root, "config");
    public string DataDir => Path.Combine(Root, "data");
    public string ReposDir => Path.Combine(DataDir, "repos");
    public string ArtifactsDir => Path.Combine(DataDir, "artifacts");
    public string TasksDir => Path.Combine(DataDir, "tasks");
    public string OrchitectDir => Path.Combine(DataDir, "orchitect");
    public string LogsDir => Path.Combine(Root, "logs");
    public string ReposJsonPath => Path.Combine(ConfigDir, "repos.json");

    public WorkspaceLayout(IOptions<WorkspaceOptions> options)
    {
        Root = Path.GetFullPath(options.Value.Root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ReposDir);
        Directory.CreateDirectory(ArtifactsDir);
        Directory.CreateDirectory(TasksDir);
        Directory.CreateDirectory(OrchitectDir);
        Directory.CreateDirectory(LogsDir);
    }
}
