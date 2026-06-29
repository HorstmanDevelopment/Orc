using Orc.Core.Orchitect;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class ResetOrchitectStateForm
{
    private readonly IOrchitectControl _orchitect;

    public ResetOrchitectStateForm(IOrchitectControl orchitect) => _orchitect = orchitect;

    public void Run()
    {
        AnsiConsole.Write(new Rule("[yellow]Reset Orchitect State[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var repos = _orchitect.ListRepos();
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No Orchitect state to reset. Nothing has been analyzed yet.[/]");
            return;
        }

        var sorted = repos.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        const string cancel = "(cancel)";
        var picked = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Pick a repo to reset:[/]")
                .PageSize(20)
                .AddChoices(sorted.Append(cancel)));

        if (picked == cancel) return;

        var state = _orchitect.LoadState(picked);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Repo:[/] {Markup.Escape(picked)}\n" +
                $"[cyan]Enhancements:[/] {state.Enhancements.Count}\n" +
                $"[cyan]Last analyzed:[/] {(state.LastAnalyzedUtc?.ToString("u") ?? "never")}"))
            .Header("[yellow]Current state[/]")
            .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[red]This will delete enhancements.json, analysis.md, history.log, and any [bold]claude_output/[/] residue in the repo. The next loop iteration will start a fresh analysis.[/]");

        if (!AnsiConsole.Confirm($"Reset Orchitect state for [yellow]{Markup.Escape(picked)}[/]?", false))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return;
        }

        try
        {
            _orchitect.ResetState(picked);
            AnsiConsole.MarkupLine($"[green]Reset done.[/] [grey]Orchitect will re-analyze on its next loop iteration.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Reset failed:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
