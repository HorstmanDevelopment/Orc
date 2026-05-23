using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class TaskHistoryForm
{
    private readonly ITaskStore _tasks;

    public TaskHistoryForm(ITaskStore tasks) => _tasks = tasks;

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Task History[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var succeeded = await _tasks.ListAsync(TaskState.Succeeded, ct);
        var failed = await _tasks.ListAsync(TaskState.Failed, ct);
        var all = succeeded.Concat(failed)
            .OrderByDescending(h => h.CompletedUtc ?? h.CreatedUtc)
            .ToList();

        if (all.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No tasks have completed yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn("[yellow]Status[/]")
            .AddColumn("[yellow]Task[/]")
            .AddColumn("[yellow]Source[/]")
            .AddColumn("[yellow]Repos[/]")
            .AddColumn("[yellow]Completed (UTC)[/]");

        foreach (var h in all)
        {
            var ok = h.State == TaskState.Succeeded;
            var color = ok ? "green" : "red";
            var status = ok ? "OK" : "FAIL";
            table.AddRow(
                $"[{color}]{status}[/]",
                $"[{color}]{Markup.Escape(h.Id)}[/]",
                Markup.Escape(h.Source.ToString()),
                Markup.Escape(h.RepoSpec),
                $"[{color}]{(h.CompletedUtc ?? h.CreatedUtc):yyyy-MM-dd HH:mm:ss}[/]");
        }

        AnsiConsole.Write(table);
    }
}
