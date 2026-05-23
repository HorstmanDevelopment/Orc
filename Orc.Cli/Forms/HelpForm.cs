using Spectre.Console;
using Spectre.Console.Rendering;

namespace Orc.Cli.Forms;

public sealed class HelpForm
{
    public void Run()
    {
        var sections = new (string Choice, string Title, Func<IRenderable> Build)[]
        {
            ("Overview", "Overview", Overview),
            ("Task format", "Task format", TaskFormat),
            ("Lifecycle", "Lifecycle of a task", Lifecycle),
            ("Permissions", "Claude permissions", Permissions),
            ("Orchitect", "Orchitect", Orchitect),
        };
        var choices = sections.Select(s => s.Choice).Append("Back").ToList();

        while (true)
        {
            AnsiConsole.Clear();
            var pick = Tui.PanelMenu.Show("HELP", "[grey]Pick a topic.[/]", choices);
            if (pick == "Back") return;

            var section = sections.First(s => s.Choice == pick);
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[yellow]{section.Title}[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();
            AnsiConsole.Write(section.Build());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static IRenderable Overview() => Wrap(
        "Orc is a file-driven multi-repo Claude orchestrator with an enhancement\n" +
        "agent (Orchitect) running alongside it. Tasks are JSON records stored in\n" +
        "the workspace; the orchestrator claims them one at a time and runs Claude\n" +
        "across the requested repos in parallel.");

    private static IRenderable TaskFormat() => Wrap(
        "Tasks are JSON files under [cyan]workspace/data/tasks/<state>/<id>.json[/].\n\n" +
        "Shape:\n" +
        "  [cyan]Id[/]         — assigned by the system\n" +
        "  [cyan]Source[/]     — User or Orchitect(repo:enhId:stepN)\n" +
        "  [cyan]RepoSpec[/]   — 'all' or comma-separated repo names\n" +
        "  [cyan]Prompt[/]     — the Claude prompt to run\n" +
        "  [cyan]CreatedUtc[/] — submission timestamp");

    private static IRenderable Lifecycle() => Wrap(
        "[bold]1. Enqueue[/]  TaskStore writes [cyan]tasks/pending/<id>.json[/].\n" +
        "[bold]2. Claim[/]    OrchestratorService atomically moves it to running/.\n" +
        "[bold]3. Resolve[/]  RepoRegistry resolves the spec to repos.\n" +
        "[bold]4. Pipeline[/] Per repo, in parallel:\n" +
        "             refresh -> guard unmerged -> branch -> claude -> commit.\n" +
        "[bold]5. Persist[/]  Transcript written to artifacts/<id>/<repo>.log.\n" +
        "[bold]6. Complete[/] Task moved to succeeded/ or failed/ with outcome.\n" +
        "[bold]7. Events[/]   StateChanged fires; Orchitect & TUI react.");

    private static IRenderable Permissions() => Wrap(
        "Before launching the [cyan]claude[/] CLI in a repo, ClaudeClient ensures a\n" +
        "[cyan].claude/settings.json[/] exists (writes a default if missing) and passes\n" +
        "the allow-list and permission mode on the CLI.\n\n" +
        "[bold]Permission mode[/]  [cyan]acceptEdits[/]: edits auto-accepted, no prompts.\n\n" +
        "[bold]Default allow-list[/]  Edit, Write, MultiEdit, Read, Glob, Grep,\n" +
        "                  Bash(dotnet build:*), Bash(dotnet test:*).\n\n" +
        "[bold]Orchitect tools[/]  Read, Glob, Grep only — analysis and step planning\n" +
        "                  never write to the repo.");

    private static IRenderable Orchitect() => Wrap(
        "[bold]Orchitect[/] runs alongside the orchestrator. For each registered repo it:\n\n" +
        "  1. Analyzes the codebase (read-only Claude) to identify enhancements\n" +
        "  2. Picks the highest-priority pending enhancement\n" +
        "  3. Plans ONE focused incremental step (read-only Claude)\n" +
        "  4. Enqueues a task for the orchestrator to execute\n" +
        "  5. Waits for completion, records the outcome\n\n" +
        "[bold]Daily quota[/] caps how many [italic]modifications[/] (committed changes)\n" +
        "Orchitect can do per day, globally and per repo. Failed or no-op runs\n" +
        "do not count.\n\n" +
        "Pause/Resume from the maintenance menu; pause flag is a file under\n" +
        "[cyan]workspace/data/orchitect/paused[/].");

    private static IRenderable Wrap(string markup) =>
        new Panel(new Markup(markup))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();
}
