using System.Text.Json;
using Orc.Core.Configuration;

namespace Orc.Core.Repos;

internal sealed class RepoRegistry : IRepoRegistry
{
    private const string AllToken = "all";

    private readonly WorkspaceLayout _layout;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public RepoRegistry(WorkspaceLayout layout)
    {
        _layout = layout;
    }

    public string SourcePath => _layout.ReposJsonPath;

    public IReadOnlyList<RepoEntry> All()
    {
        var raw = LoadRaw();
        return raw.Select(r =>
        {
            var name = ResolveName(r);
            return new RepoEntry(
                name,
                r.Url ?? "",
                string.IsNullOrWhiteSpace(r.Branch) ? "main" : r.Branch.Trim(),
                Path.Combine(_layout.ReposDir, name),
                string.IsNullOrWhiteSpace(r.Mission) ? null : r.Mission.Trim(),
                r.AutoMerge ?? true);
        }).ToArray();
    }

    public bool Contains(string url) =>
        !string.IsNullOrWhiteSpace(url) &&
        LoadRaw().Any(r => !string.IsNullOrWhiteSpace(r.Url) &&
                           string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase));

    public bool ContainsName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        LoadRaw().Any(r => string.Equals(ResolveName(r), name.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool TryResolve(string spec, out IReadOnlyList<RepoEntry> repos, out string? error)
    {
        var all = All();

        if (string.Equals(spec, AllToken, StringComparison.OrdinalIgnoreCase))
        {
            if (all.Count == 0)
            {
                repos = [];
                error = $"'all' specified but {Path.GetFileName(SourcePath)} is empty.";
                return false;
            }
            repos = all;
            error = null;
            return true;
        }

        var names = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resolved = new List<RepoEntry>(names.Length);
        var missing = new List<string>();
        foreach (var n in names)
        {
            var match = all.FirstOrDefault(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase));
            if (match is null) missing.Add(n);
            else resolved.Add(match);
        }
        if (missing.Count > 0)
        {
            repos = [];
            error = $"Repo(s) not in registry: {string.Join(", ", missing)}";
            return false;
        }

        repos = resolved;
        error = null;
        return true;
    }

    public Task AddAsync(string url, string branch, CancellationToken ct, string? mission = null)
    {
        lock (_lock)
        {
            var list = LoadRaw();
            if (list.Any(r => !string.IsNullOrWhiteSpace(r.Url) &&
                              string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase))) return Task.CompletedTask;
            list.Add(new RawEntry { Url = url, Branch = branch, Mission = NormalizeMission(mission) });
            Save(list);
        }
        return Task.CompletedTask;
    }

    public Task AddLocalAsync(string name, string branch, CancellationToken ct, string? mission = null)
    {
        var trimmed = name.Trim();
        lock (_lock)
        {
            var list = LoadRaw();
            if (list.Any(r => string.Equals(ResolveName(r), trimmed, StringComparison.OrdinalIgnoreCase)))
                return Task.CompletedTask;
            list.Add(new RawEntry { Url = "", Branch = branch, Name = trimmed, Mission = NormalizeMission(mission) });
            Save(list);
        }
        return Task.CompletedTask;
    }

    public Task UpdateMissionAsync(string name, string? mission, CancellationToken ct)
    {
        var trimmedName = name.Trim();
        lock (_lock)
        {
            var list = LoadRaw();
            var entry = list.FirstOrDefault(r =>
                string.Equals(ResolveName(r), trimmedName, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return Task.CompletedTask;
            entry.Mission = NormalizeMission(mission);
            Save(list);
        }
        return Task.CompletedTask;
    }

    private static string? NormalizeMission(string? mission) =>
        string.IsNullOrWhiteSpace(mission) ? null : mission.Trim();

    private List<RawEntry> LoadRaw()
    {
        if (!File.Exists(SourcePath)) return [];
        try
        {
            var text = File.ReadAllText(SourcePath);
            if (string.IsNullOrWhiteSpace(text)) return [];
            return JsonSerializer.Deserialize<List<RawEntry>>(text, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save(List<RawEntry> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePath)!);
        File.WriteAllText(SourcePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    private static string ResolveName(RawEntry r) =>
        !string.IsNullOrWhiteSpace(r.Name) ? r.Name.Trim() : DeriveName(r.Url ?? "");

    private static string DeriveName(string url)
    {
        var trimmed = url.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    private sealed class RawEntry
    {
        public string Url { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string? Name { get; set; }
        public string? Mission { get; set; }

        // Null in legacy/hand-written repos.json means "use the default" (true):
        // on success the orc-task branch is merged into the base branch and deleted.
        // Set false to leave the branch un-merged for human review.
        public bool? AutoMerge { get; set; }
    }
}
