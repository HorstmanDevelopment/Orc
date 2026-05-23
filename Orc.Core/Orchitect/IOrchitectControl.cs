namespace Orc.Core.Orchitect;

public interface IOrchitectControl
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
    void ForceReanalyze(string repoName);
    QuotaSnapshot QuotaSnapshot();
    IReadOnlyList<string> ListRepos();
    RepoState LoadState(string repoName);
}
