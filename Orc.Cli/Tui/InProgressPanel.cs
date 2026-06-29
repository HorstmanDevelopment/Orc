using Orc.Cli.Forms;
using Orc.Core.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Orc.Cli.Tui;

public static class InProgressPanel
{
    public static IRenderable? Build(IReadOnlyCollection<TaskHeader> running)
    {
        if (running.Count == 0) return null;

        var table = new Table()
            .Border(TableBorder.Minimal)
            .Expand()
            .AddColumn("[yellow]Task[/]")
            .AddColumn("[yellow]Source[/]")
            .AddColumn("[yellow]Repos[/]")
            .AddColumn("[yellow]Elapsed[/]");

        var now = DateTime.UtcNow;
        var stuckCount = 0;
        foreach (var t in running.OrderBy(t => t.CreatedUtc))
        {
            var elapsed = now - t.CreatedUtc;
            var isStuck = elapsed >= CancelRunningTaskForm.StuckThreshold;
            if (isStuck) stuckCount++;

            var s = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h{elapsed.Minutes:D2}m"
                : $"{elapsed.Minutes:D2}m{elapsed.Seconds:D2}s";
            var elapsedCell = isStuck ? $"[red]{s} [[STUCK]][/]" : $"[cyan]{s}[/]";

            table.AddRow(
                Markup.Escape(t.Id),
                Markup.Escape(t.Source.ToString()),
                Markup.Escape(t.RepoSpec),
                elapsedCell);
        }

        var headerTitle = stuckCount > 0
            ? $"[bold yellow]In Progress ({running.Count})[/]  [red]{stuckCount} stuck[/]  [grey]· main menu → Cancel running task[/]"
            : $"[bold yellow]In Progress ({running.Count})[/]";

        return new Panel(table)
            .Border(BoxBorder.Rounded)
            .BorderColor(stuckCount > 0 ? Color.Red : Color.Yellow)
            .Header(headerTitle, Justify.Left)
            .Expand();
    }
}
