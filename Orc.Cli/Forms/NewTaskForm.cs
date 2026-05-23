using Orc.Core.Repos;
using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class NewTaskForm
{
    private const string EndMarker = ".";

    private readonly ITaskStore _tasks;
    private readonly IRepoRegistry _registry;

    public NewTaskForm(ITaskStore tasks, IRepoRegistry registry)
    {
        _tasks = tasks;
        _registry = registry;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Create New Task[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var repos = _registry.All().Select(r => r.Name).ToList();
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No repos in registry. Add a repo first.[/]");
            return;
        }

        var scope = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Run task against:")
                .AddChoices("All repos", "Pick specific repos", "Cancel"));
        if (scope == "Cancel") return;

        string spec;
        if (scope == "All repos") spec = "all";
        else
        {
            var picked = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select one or more repos (space to toggle, enter to confirm):")
                    .Required()
                    .PageSize(10)
                    .AddChoices(repos));
            spec = string.Join(",", picked);
        }

        var prompt = ReadMultiLinePrompt();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            AnsiConsole.MarkupLine("[red]Empty prompt. Aborted.[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Repos:[/] {Markup.Escape(spec)}\n[cyan]Prompt:[/]\n{Markup.Escape(prompt)}"))
            .Header("[yellow]Preview[/]")
            .Border(BoxBorder.Rounded));

        if (!AnsiConsole.Confirm("Submit this task?", true)) return;

        var id = $"task_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Random.Shared.Next(1000, 9999)}";
        var record = new TaskRecord(id, TaskSource.User, spec, prompt, DateTime.UtcNow);
        await _tasks.EnqueueAsync(record, ct);

        AnsiConsole.MarkupLine($"[green]Enqueued[/] {Markup.Escape(id)}");
    }

    private static string ReadMultiLinePrompt()
    {
        AnsiConsole.MarkupLine("[grey]Enter the task prompt. Finish with a single '.' on its own line:[/]");
        var lines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null || line.Trim() == EndMarker) break;
            lines.Add(line);
        }
        return string.Join("\n", lines).Trim();
    }
}
