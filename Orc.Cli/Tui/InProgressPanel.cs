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
        foreach (var t in running.OrderBy(t => t.CreatedUtc))
        {
            var elapsed = now - t.CreatedUtc;
            var s = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h{elapsed.Minutes:D2}m"
                : $"{elapsed.Minutes:D2}m{elapsed.Seconds:D2}s";
            table.AddRow(
                Markup.Escape(t.Id),
                Markup.Escape(t.Source.ToString()),
                Markup.Escape(t.RepoSpec),
                $"[cyan]{s}[/]");
        }

        return new Panel(table)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Header($"[bold yellow]In Progress ({running.Count})[/]", Justify.Left)
            .Expand();
    }
}
