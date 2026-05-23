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
        Root = ResolveRoot(options.Value.Root);
    }

    private static string ResolveRoot(string configured)
    {
        if (Path.IsPathFullyQualified(configured))
            return Path.GetFullPath(configured);

        var anchor = FindProjectRoot() ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(anchor, configured));
    }

    private static string? FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
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
