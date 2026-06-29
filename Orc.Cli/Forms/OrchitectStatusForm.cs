using Microsoft.Extensions.Options;
using Orc.Core.Configuration;
using Orc.Core.Orchitect;
using Orc.Core.Repos;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class OrchitectStatusForm
{
    private readonly IOrchitectControl _orchitect;
    private readonly IRepoRegistry _registry;

    public OrchitectStatusForm(IOrchitectControl orchitect, IRepoRegistry registry)
    {
        _orchitect = orchitect;
        _registry = registry;
    }

    public void Run()
    {
        AnsiConsole.Write(new Rule("[yellow]Orchitect Status[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var snap = _orchitect.QuotaSnapshot();
        WriteQuotaPanel(snap);

        var repos = _orchitect.ListRepos();
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No repo state yet. Orchitect will populate this once it analyzes the first repo.[/]");
            return;
        }

        var sorted = repos.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        const string viewAll = "(view all)";
        const string cancel = "(cancel)";

        var picked = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Pick a repo:[/]")
                .PageSize(20)
                .AddChoices(sorted.Append(viewAll).Append(cancel)));

        if (picked == cancel) return;

        var registered = _registry.All();
        var targets = picked == viewAll ? (IReadOnlyList<string>)sorted : [picked];

        foreach (var repoName in targets)
            WriteRepoPanel(repoName, snap, registered);
    }

    private void WriteQuotaPanel(QuotaSnapshot snap)
    {
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Paused:[/] {(_orchitect.IsPaused ? "[red]yes[/]" : "[green]no[/]")}    " +
                $"[cyan]Date (UTC):[/] {Markup.Escape(snap.DateUtc)}    " +
                $"[cyan]Modifications today:[/] {snap.ModificationsToday}"))
            .Border(BoxBorder.Rounded)
            .Header("[yellow]Quota[/]"));
        AnsiConsole.WriteLine();
    }

    private void WriteRepoPanel(string repoName, QuotaSnapshot snap, IReadOnlyList<RepoEntry> registered)
    {
        var state = _orchitect.LoadState(repoName);
        var today = snap.PerRepo.TryGetValue(repoName, out var n) ? n : 0;
        var mission = registered
            .FirstOrDefault(r => string.Equals(r.Name, repoName, StringComparison.OrdinalIgnoreCase))
            ?.Mission;

        var table = new Table()
            .Border(TableBorder.Minimal)
            .Expand()
            .AddColumn("[yellow]ID[/]")
            .AddColumn("[yellow]Status[/]")
            .AddColumn("[yellow]Steps[/]")
            .AddColumn("[yellow]Title[/]");

        if (state.Enhancements.Count == 0)
            table.AddRow("[grey](no enhancements yet)[/]", "", "", "");
        else
        {
            foreach (var e in state.Enhancements.OrderBy(x => x.Id, StringComparer.Ordinal))
                table.AddRow(
                    Markup.Escape(e.Id),
                    Color(e.Status),
                    SummarizeSteps(e),
                    Markup.Escape(Truncate(e.Title, 60)));
        }

        var last = state.LastAnalyzedUtc?.ToString("u") ?? "never";
        var panel = new Panel(table)
            .Border(BoxBorder.Rounded)
            .Header($"[bold yellow]{Markup.Escape(repoName)}[/]  [grey]today {today} · last analyzed {Markup.Escape(last)}[/]", Justify.Left)
            .Expand();
        AnsiConsole.Write(panel);
        if (!string.IsNullOrWhiteSpace(mission))
            AnsiConsole.MarkupLine($"  [grey]Mission:[/] [italic]{Markup.Escape(Truncate(mission, 100))}[/]");
        AnsiConsole.WriteLine();
    }

    private static string Color(EnhancementStatus s) => s switch
    {
        EnhancementStatus.Identified => $"[grey]{s}[/]",
        EnhancementStatus.InProgress => $"[cyan]{s}[/]",
        EnhancementStatus.Completed => $"[green]{s}[/]",
        EnhancementStatus.Abandoned => $"[red]{s}[/]",
        _ => s.ToString(),
    };

    private static string SummarizeSteps(Enhancement e)
    {
        if (e.Steps.Count == 0) return "[grey]0[/]";
        var done = e.Steps.Count(s => s.Status == StepStatus.Completed);
        var failed = e.Steps.Count(s => s.Status == StepStatus.Failed);
        var active = e.Steps.Count(s => s.Status is StepStatus.Pending or StepStatus.Submitted);
        var parts = new List<string> { $"{e.Steps.Count} total" };
        if (done > 0) parts.Add($"[green]{done} ok[/]");
        if (failed > 0) parts.Add($"[red]{failed} fail[/]");
        if (active > 0) parts.Add($"[cyan]{active} active[/]");
        return string.Join(", ", parts);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
