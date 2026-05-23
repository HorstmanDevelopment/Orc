using Orc.Cli.Forms;
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

    public Dashboard(ITaskStore tasks, IRepoRegistry registry, IOrchitectControl orchitect)
    {
        _tasks = tasks;
        _registry = registry;
        _orchitect = orchitect;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        Banner.Render();
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            var choice = PanelMenu.Show(
                "ORC",
                "",
                ["Create new task", "Maintenance and System info", "Help", "Quit"],
                () => InProgressPanel.Build(GetRunningSync()));

            switch (choice)
            {
                case "Create new task":
                    await RunPageAsync(() => new NewTaskForm(_tasks, _registry).RunAsync(ct));
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
                    "View task history",
                    "View installed repos",
                    "Orchitect status",
                    pauseLabel,
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
                case "View task history":
                    await RunPageAsync(() => new TaskHistoryForm(_tasks).RunAsync(ct));
                    break;
                case "View installed repos":
                    RunPage(() => new RepoListForm(_registry).Run());
                    break;
                case "Orchitect status":
                    RunPage(() => new OrchitectStatusForm(_orchitect).Run());
                    break;
                case "Pause Orchitect":
                    _orchitect.Pause();
                    RunPage(() => AnsiConsole.MarkupLine("[yellow]Orchitect paused.[/]"));
                    break;
                case "Resume Orchitect":
                    _orchitect.Resume();
                    RunPage(() => AnsiConsole.MarkupLine("[green]Orchitect resumed.[/]"));
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
