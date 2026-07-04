using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orc.Core.Claude;
using Orc.Core.Configuration;
using Orc.Core.Orchitect;
using Orc.Core.Pipeline;
using Orc.Core.Process;
using Orc.Core.Repos;
using Orc.Core.Tasks;

namespace Orc.Core.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrcCore(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<WorkspaceOptions>(config.GetSection(WorkspaceOptions.Section));
        services.Configure<ClaudeOptions>(config.GetSection(ClaudeOptions.Section));
        services.Configure<OrchitectOptions>(config.GetSection(OrchitectOptions.Section));
        services.Configure<ResumeOptions>(config.GetSection(ResumeOptions.Section));

        services.AddSingleton<WorkspaceLayout>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IClaudeClient, ClaudeClient>();
        services.AddSingleton<IGitClient, GitClient>();
        services.AddSingleton<IRepoRegistry, RepoRegistry>();
        services.AddSingleton<IRepoLock, RepoLock>();
        services.AddSingleton<ITaskStore, JsonTaskStore>();
        services.AddSingleton<IRunningTaskRegistry, RunningTaskRegistry>();
        services.AddSingleton<ITaskResumer, TaskResumeService>();

        services.AddSingleton<RefreshRepoStage>();
        services.AddSingleton<GuardUnmergedBranchStage>();
        services.AddSingleton<CreateBranchStage>();
        services.AddSingleton<RunClaudeStage>();
        services.AddSingleton<CommitStage>();
        services.AddSingleton<MergeStage>();
        services.AddSingleton<ITaskRunner, TaskRunner>();

        services.AddHostedService<OrchestratorService>();

        services.AddSingleton<IOrchitectStateStore, OrchitectStateStore>();
        services.AddSingleton<IQuota, Quota>();
        services.AddSingleton<AnalysisRunner>();
        services.AddSingleton<StepPlanner>();
        services.AddSingleton<OrchitectService>();
        services.AddSingleton<IOrchitectControl>(sp => sp.GetRequiredService<OrchitectService>());
        services.AddHostedService(sp => sp.GetRequiredService<OrchitectService>());
        services.AddHostedService<QuotaService>();

        return services;
    }
}
