using Orc.Cli.Forms;
using Orc.Core.Configuration;
using Orc.Core.Orchitect;
using Orc.Core.Repos;
using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Tui;

public sealed class Dashboard
{
    private readonly ITaskStore _tasks;
    private readonly IRepoRegistry _registry;
    private readonly IOrchitectControl _orchitect;
    private readonly IGitClient _git;
    private readonly WorkspaceLayout _layout;
    private readonly IRunningTaskRegistry _runningTasks;

    public Dashboard(
        ITaskStore tasks,
        IRepoRegistry registry,
        IOrchitectControl orchitect,
        IGitClient git,
        WorkspaceLayout layout,
        IRunningTaskRegistry runningTasks)
    {
        _tasks = tasks;
        _registry = registry;
        _orchitect = orchitect;
        _git = git;
        _layout = layout;
        _runningTasks = runningTasks;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        Banner.Render();
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            var cancelLabel = BuildCancelLabel(GetRunningSync());

            var choice = PanelMenu.Show(
                "ORC",
                "",
                ["Create new task", cancelLabel, "Orchitect status", "Maintenance and System info", "Help", "Quit"],
                () => InProgressPanel.Build(GetRunningSync()));

            switch (choice)
            {
                case "Create new task":
                    await RunPageAsync(() => new NewTaskForm(_tasks, _registry).RunAsync(ct));
                    break;
                case var c when c == cancelLabel:
                    await RunPageAsync(() => new CancelRunningTaskForm(_tasks, _runningTasks).RunAsync(ct));
                    break;
                case "Orchitect status":
                    RunPage(() => new OrchitectStatusForm(_orchitect, _registry).Run());
                    break;
                case "Maintenance and System info":
                    await RunMaintenanceMenuAsync(ct);
                    break;
                case "Help":
                    RunPage(new HelpForm().Run);
                    break;
                case "Quit":
                    AnsiConsole.Clear();
                    return;
            }
        }
    }

    private async Task RunMaintenanceMenuAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            var pauseLabel = _orchitect.IsPaused ? "Resume Orchitect" : "Pause Orchitect";
            var choice = PanelMenu.Show(
                "MAINTENANCE & SYSTEM INFO",
                "",
                [
                    "Add repository",
                    "Create new repo",
                    "Edit repo mission",
                    "View task history",
                    "View failures",
                    "View installed repos",
                    pauseLabel,
                    "Reset Orchitect state for repo",
                    "Clear task history",
                    "Back",
                ],
                () => InProgressPanel.Build(GetRunningSync()));

            if (choice == "Back") return;

            switch (choice)
            {
                case "Add repository":
                    await RunPageAsync(() => new AddRepoForm(_registry).RunAsync(ct));
                    break;
                case "Create new repo":
                    await RunPageAsync(() => new CreateRepoForm(_registry, _git, _layout).RunAsync(ct));
                    break;
                case "Edit repo mission":
                    await RunPageAsync(() => new EditMissionForm(_registry).RunAsync(ct));
                    break;
                case "View task history":
                    await RunPageAsync(() => new TaskHistoryForm(_tasks).RunAsync(ct));
                    break;
                case "View failures":
                    await new FailuresForm(_tasks).RunAsync(ct);
                    break;
                case "View installed repos":
                    RunPage(() => new RepoListForm(_registry).Run());
                    break;
                case "Pause Orchitect":
                    _orchitect.Pause();
                    RunPage(() => AnsiConsole.MarkupLine("[yellow]Orchitect paused.[/]"));
                    break;
                case "Resume Orchitect":
                    _orchitect.Resume();
                    RunPage(() => AnsiConsole.MarkupLine("[green]Orchitect resumed.[/]"));
                    break;
                case "Reset Orchitect state for repo":
                    RunPage(() => new ResetOrchitectStateForm(_orchitect).Run());
                    break;
                case "Clear task history":
                    await RunPageAsync(() => new ClearHistoryForm(_tasks).RunAsync(ct));
                    break;
            }
        }
    }

    private IReadOnlyCollection<TaskHeader> GetRunningSync()
    {
        try { return _tasks.ListAsync(TaskState.Running, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return []; }
    }

    private static string BuildCancelLabel(IReadOnlyCollection<TaskHeader> running)
    {
        if (running.Count == 0) return "Cancel running task";
        var now = DateTime.UtcNow;
        var stuck = running.Count(t => now - t.CreatedUtc >= CancelRunningTaskForm.StuckThreshold);
        return stuck > 0
            ? $"Cancel running task  ({running.Count} running, {stuck} stuck)"
            : $"Cancel running task  ({running.Count} running)";
    }

    private static void RunPage(Action action)
    {
        AnsiConsole.Clear();
        action();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
        Console.ReadKey(true);
    }

    private static async Task RunPageAsync(Func<Task> action)
    {
        AnsiConsole.Clear();
        await action();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
        Console.ReadKey(true);
    }
}
