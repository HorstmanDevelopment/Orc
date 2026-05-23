using System.Text.Json;
using System.Text.Json.Serialization;
using Orc.Core.Configuration;

namespace Orc.Core.Orchitect;

public interface IOrchitectStateStore
{
    RepoState Load(string repoName);
    void Save(string repoName, RepoState state);
    string AnalysisPath(string repoName);
    string HistoryPath(string repoName);
    IReadOnlyList<string> ListRepos();
    void AppendHistory(string repoName, string message);
}

internal sealed class OrchitectStateStore : IOrchitectStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WorkspaceLayout _layout;
    private readonly object _lock = new();

    public OrchitectStateStore(WorkspaceLayout layout) => _layout = layout;

    public RepoState Load(string repoName)
    {
        var path = StatePath(repoName);
        lock (_lock)
        {
            if (!File.Exists(path)) return new RepoState { RepoName = repoName };
            try
            {
                var s = JsonSerializer.Deserialize<RepoState>(File.ReadAllText(path), JsonOpts)
                        ?? new RepoState { RepoName = repoName };
                if (string.IsNullOrEmpty(s.RepoName)) s.RepoName = repoName;
                return s;
            }
            catch
            {
                return new RepoState { RepoName = repoName };
            }
        }
    }

    public void Save(string repoName, RepoState state)
    {
        var dir = RepoDir(repoName);
        Directory.CreateDirectory(dir);
        lock (_lock)
        {
            File.WriteAllText(StatePath(repoName), JsonSerializer.Serialize(state, JsonOpts));
        }
    }

    public string AnalysisPath(string repoName) => Path.Combine(RepoDir(repoName), "analysis.md");
    public string HistoryPath(string repoName) => Path.Combine(RepoDir(repoName), "history.log");

    public IReadOnlyList<string> ListRepos()
    {
        var root = Path.Combine(_layout.OrchitectDir, "repos");
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root)
            .Select(d => Path.GetFileName(d) ?? "")
            .Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void AppendHistory(string repoName, string message)
    {
        var path = HistoryPath(repoName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private string RepoDir(string repoName) => Path.Combine(_layout.OrchitectDir, "repos", repoName);
    private string StatePath(string repoName) => Path.Combine(RepoDir(repoName), "enhancements.json");
}
