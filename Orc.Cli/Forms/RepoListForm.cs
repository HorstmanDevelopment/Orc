using Orc.Core.Repos;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class RepoListForm
{
    private readonly IRepoRegistry _registry;

    public RepoListForm(IRepoRegistry registry) => _registry = registry;

    public void Run()
    {
        AnsiConsole.Write(new Rule("[yellow]Registered Repositories[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var repos = _registry.All();
        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No repositories registered.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn("[yellow]Name[/]")
            .AddColumn("[yellow]Branch[/]")
            .AddColumn("[yellow]URL[/]")
            .AddColumn("[yellow]Local path[/]");

        foreach (var r in repos.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var cloned = Directory.Exists(Path.Combine(r.LocalPath, ".git"));
            var nameColor = cloned ? "green" : "grey";
            table.AddRow(
                $"[{nameColor}]{Markup.Escape(r.Name)}[/]",
                Markup.Escape(r.BaseBranch),
                $"[grey]{Markup.Escape(r.Url)}[/]",
                $"[grey]{Markup.Escape(r.LocalPath)}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Source: {Markup.Escape(_registry.SourcePath)}[/]");
    }
}
