using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class CancelRunningTaskForm
{
    internal static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(30);

    private readonly ITaskStore _tasks;
    private readonly IRunningTaskRegistry _registry;

    public CancelRunningTaskForm(ITaskStore tasks, IRunningTaskRegistry registry)
    {
        _tasks = tasks;
        _registry = registry;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Cancel Running Task[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var running = await _tasks.ListAsync(TaskState.Running, ct);
        if (running.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No tasks are running.[/]");
            return;
        }

        var now = DateTime.UtcNow;
        var items = running
            .Select(t => new Row(t, now - t.CreatedUtc))
            .OrderByDescending(r => r.Elapsed)
            .ToArray();

        var stuckCount = items.Count(i => i.Elapsed >= StuckThreshold);
        AnsiConsole.MarkupLine(
            $"[grey]{items.Length} running task(s).[/]" +
            (stuckCount > 0
                ? $"  [red]{stuckCount} past the {(int)StuckThreshold.TotalMinutes}m threshold.[/]"
                : ""));
        AnsiConsole.WriteLine();

        var back = new Row(null, TimeSpan.Zero);
        var pick = AnsiConsole.Prompt(
            new SelectionPrompt<Row>()
                .Title("[cyan]Pick a task to cancel:[/]")
                .PageSize(20)
                .UseConverter(r => r.Render())
                .AddChoices(items)
                .AddChoices(back));

        if (pick.Header is null) return;
        var header = pick.Header;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Task:[/]    {Markup.Escape(header.Id)}\n" +
                $"[cyan]Source:[/]  {Markup.Escape(header.Source.ToString())}\n" +
                $"[cyan]Repos:[/]   {Markup.Escape(header.RepoSpec)}\n" +
                $"[cyan]Elapsed:[/] {FormatElapsed(pick.Elapsed)}"))
            .Header("[yellow]Selected[/]")
            .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[red]This will:[/]");
        AnsiConsole.MarkupLine("  [red]·[/] kill the running claude process and its pipeline");
        AnsiConsole.MarkupLine("  [red]·[/] force-checkout each repo back to its base branch (discards uncommitted edits)");
        AnsiConsole.MarkupLine("  [red]·[/] delete the partial [yellow]orc-task/...[/] branch(es)");
        AnsiConsole.MarkupLine("  [red]·[/] move the task to [yellow]failed/[/] with reason \"cancelled by user\"");
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm($"Cancel task [yellow]{Markup.Escape(header.Id)}[/]?", false))
        {
            AnsiConsole.MarkupLine("[grey]No action taken.[/]");
            return;
        }

        if (!_registry.TryCancel(header.Id))
        {
            AnsiConsole.MarkupLine("[yellow]Could not signal cancel — the task may have already completed.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[grey]Cancel signal sent. Waiting for cleanup to finish...[/]");

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Awaiting cleanup...", async _ =>
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    var cur = await _tasks.GetAsync(header.Id, ct);
                    if (cur is null || cur.State != TaskState.Running) return;
                    await Task.Delay(500, ct);
                }
            });

        var final = await _tasks.GetAsync(header.Id, ct);
        if (final is null)
            AnsiConsole.MarkupLine($"[green]Task {Markup.Escape(header.Id)} is no longer present.[/]");
        else if (final.State == TaskState.Running)
            AnsiConsole.MarkupLine(
                "[yellow]Task is still winding down. It will move to failed/ once cleanup completes; check task history.[/]");
        else
            AnsiConsole.MarkupLine(
                $"[green]Task {Markup.Escape(header.Id)} -> {final.State}.[/]  [grey]See task history for the cleanup transcript.[/]");
    }

    private static string FormatElapsed(TimeSpan e) =>
        e.TotalHours >= 1
            ? $"{(int)e.TotalHours}h{e.Minutes:D2}m"
            : $"{e.Minutes:D2}m{e.Seconds:D2}s";

    private sealed record Row(TaskHeader? Header, TimeSpan Elapsed)
    {
        public string Render()
        {
            if (Header is null) return "(back)";

            var stuck = Elapsed >= StuckThreshold;
            var prefix = stuck ? "[red][[STUCK]][/] " : "        ";
            var elapsed = FormatElapsed(Elapsed);
            var elapsedColor = stuck ? "red" : "cyan";

            return $"{prefix}[{elapsedColor}]{elapsed,-8}[/]  {Markup.Escape(Header.Id)}  " +
                   $"[grey]{Markup.Escape(Header.RepoSpec)}[/]";
        }
    }
}
