using Orc.Core.Configuration;
using Orc.Core.Repos;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class CreateRepoForm
{
    private readonly IRepoRegistry _registry;
    private readonly IGitClient _git;
    private readonly WorkspaceLayout _layout;

    public CreateRepoForm(IRepoRegistry registry, IGitClient git, WorkspaceLayout layout)
    {
        _registry = registry;
        _git = git;
        _layout = layout;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Create New Repo[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Repo name:[/]")
                .Validate(ValidateName)).Trim();

        var branch = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Base branch[/] [grey](e.g. main, master)[/]:")
                .DefaultValue("main")).Trim();

        var missionInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Mission statement[/] [grey](optional, guides Orchitect enhancements — blank to skip)[/]:")
                .AllowEmpty()).Trim();
        var mission = string.IsNullOrWhiteSpace(missionInput) ? null : missionInput;

        var targetPath = Path.Combine(_layout.ReposDir, name);

        if (_registry.ContainsName(name))
        {
            AnsiConsole.MarkupLine($"[yellow]Name already registered:[/] {Markup.Escape(name)}");
            return;
        }
        if (Directory.Exists(targetPath))
        {
            AnsiConsole.MarkupLine($"[red]Path already exists:[/] {Markup.Escape(targetPath)}");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Name:[/]    {Markup.Escape(name)}\n" +
                $"[cyan]Branch:[/]  {Markup.Escape(branch)}\n" +
                $"[cyan]Path:[/]    {Markup.Escape(targetPath)}\n" +
                $"[cyan]Mission:[/] {(mission is null ? "[grey](none)[/]" : Markup.Escape(mission))}\n" +
                $"[grey]Local-only — no remote will be configured.[/]"))
            .Header("[yellow]Preview[/]")
            .Border(BoxBorder.Rounded));

        if (!AnsiConsole.Confirm("Create this repo?", true)) return;

        var init = await _git.InitLocalAsync(name, branch, _layout.ReposDir, ct);
        if (!init.Success)
        {
            AnsiConsole.MarkupLine("[red]git init failed:[/]");
            AnsiConsole.WriteLine(init.Output);
            return;
        }

        await _registry.AddLocalAsync(name, branch, ct, mission);
        AnsiConsole.MarkupLine($"[green]Created[/] {Markup.Escape(name)} at {Markup.Escape(targetPath)}");
        AnsiConsole.MarkupLine("[grey]Add a remote later with[/] [cyan]git remote add origin <url>[/] [grey]if you want to push.[/]");
    }

    private static ValidationResult ValidateName(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return ValidationResult.Error("[red]Name is required[/]");
        var trimmed = v.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return ValidationResult.Error("[red]Name contains invalid filename characters[/]");
        if (trimmed.Contains(' '))
            return ValidationResult.Error("[red]Name must not contain spaces[/]");
        return ValidationResult.Success();
    }
}
