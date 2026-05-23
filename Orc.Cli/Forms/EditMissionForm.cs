using Orc.Core.Repos;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class EditMissionForm
{
    private readonly IRepoRegistry _registry;

    public EditMissionForm(IRepoRegistry registry) => _registry = registry;

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Edit Repo Mission[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var repos = _registry.All();
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No repositories registered.[/]");
            return;
        }

        var names = repos.Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        const string cancel = "(cancel)";
        var picked = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Pick a repo:[/]")
                .PageSize(20)
                .AddChoices(names.Append(cancel)));

        if (picked == cancel) return;

        var repo = repos.First(r => string.Equals(r.Name, picked, StringComparison.OrdinalIgnoreCase));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Name:[/]    {Markup.Escape(repo.Name)}\n" +
                $"[cyan]Current:[/] {(repo.Mission is null ? "[grey](none)[/]" : Markup.Escape(repo.Mission))}"))
            .Header("[yellow]Current Mission[/]")
            .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Enter the new mission, or leave blank to clear it.[/]");

        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Mission:[/]").AllowEmpty()).Trim();
        var newMission = string.IsNullOrWhiteSpace(input) ? null : input;

        if (string.Equals(newMission, repo.Mission, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("[grey]No change.[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]New:[/] {(newMission is null ? "[grey](cleared)[/]" : Markup.Escape(newMission))}"))
            .Header("[yellow]Preview[/]")
            .Border(BoxBorder.Rounded));

        if (!AnsiConsole.Confirm("Save mission?", true)) return;

        await _registry.UpdateMissionAsync(repo.Name, newMission, ct);
        AnsiConsole.MarkupLine($"[green]Saved.[/] [grey]Restart the app for Orchitect to pick it up.[/]");
    }
}
