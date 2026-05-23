using Orc.Core.Repos;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class AddRepoForm
{
    private readonly IRepoRegistry _registry;

    public AddRepoForm(IRepoRegistry registry) => _registry = registry;

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Add Repository[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var url = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Git URL:[/]")
                .Validate(v => string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Error("[red]URL is required[/]")
                    : ValidationResult.Success())).Trim();

        var branch = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Base branch[/] [grey](e.g. main, master, develop)[/]:")
                .DefaultValue("main")).Trim();

        var missionInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Mission statement[/] [grey](optional, guides Orchitect enhancements — blank to skip)[/]:")
                .AllowEmpty()).Trim();
        var mission = string.IsNullOrWhiteSpace(missionInput) ? null : missionInput;

        if (_registry.Contains(url))
        {
            AnsiConsole.MarkupLine($"[yellow]Already registered:[/] {Markup.Escape(url)}");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]URL:[/]     {Markup.Escape(url)}\n" +
                $"[cyan]Branch:[/]  {Markup.Escape(branch)}\n" +
                $"[cyan]Mission:[/] {(mission is null ? "[grey](none)[/]" : Markup.Escape(mission))}"))
            .Header("[yellow]Preview[/]")
            .Border(BoxBorder.Rounded));

        if (!AnsiConsole.Confirm("Add this repo?", true)) return;

        await _registry.AddAsync(url, branch, ct, mission);
        AnsiConsole.MarkupLine($"[green]Added[/] {Markup.Escape($"{url} {branch}")}");
        AnsiConsole.MarkupLine("[grey]The orchestrator will clone/refresh it on the next task run.[/]");
    }
}
