using System.Text.Json;
using Orc.Core.Configuration;

namespace Orc.Core.Orchitect;

public sealed record QuotaSnapshot(string DateUtc, int ModificationsToday, IReadOnlyDictionary<string, int> PerRepo);

public interface IQuota
{
    bool CanModify(string repoName);
    void IncrementModification(string repoName);
    QuotaSnapshot Snapshot();
}

internal sealed class Quota : IQuota
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly OrchitectOptions _options;
    private readonly object _lock = new();
    private State _state;

    public Quota(WorkspaceLayout layout, Microsoft.Extensions.Options.IOptions<OrchitectOptions> options)
    {
        _path = Path.Combine(layout.OrchitectDir, "quota.json");
        _options = options.Value;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _state = LoadFromDisk();
    }

    public bool CanModify(string repoName)
    {
        lock (_lock)
        {
            Rollover();
            if (_state.ModificationsToday >= _options.MaxModificationsPerDay) return false;
            if (_state.PerRepo.TryGetValue(repoName, out var n) && n >= _options.MaxModificationsPerRepoPerDay) return false;
            return true;
        }
    }

    public void IncrementModification(string repoName)
    {
        lock (_lock)
        {
            Rollover();
            _state.ModificationsToday++;
            _state.PerRepo[repoName] = _state.PerRepo.GetValueOrDefault(repoName) + 1;
            Save();
        }
    }

    public QuotaSnapshot Snapshot()
    {
        lock (_lock)
        {
            Rollover();
            return new QuotaSnapshot(_state.DateUtc, _state.ModificationsToday, new Dictionary<string, int>(_state.PerRepo));
        }
    }

    private void Rollover()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (_state.DateUtc != today)
        {
            _state = new State { DateUtc = today };
            Save();
        }
    }

    private State LoadFromDisk()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!File.Exists(_path)) return new State { DateUtc = today };
        try
        {
            var s = JsonSerializer.Deserialize<State>(File.ReadAllText(_path), JsonOpts) ?? new State { DateUtc = today };
            return s.DateUtc == today ? s : new State { DateUtc = today };
        }
        catch
        {
            return new State { DateUtc = today };
        }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_state, JsonOpts)); } catch { }
    }

    private sealed class State
    {
        public string DateUtc { get; set; } = "";
        public int ModificationsToday { get; set; }
        public Dictionary<string, int> PerRepo { get; set; } = new();
    }
}
