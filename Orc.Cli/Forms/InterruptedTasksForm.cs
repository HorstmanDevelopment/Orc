using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

/// <summary>
/// Lists interrupted tasks (left mid-run by a shutdown/crash) and lets the user resume
/// them — re-queued so the orchestrator continues on the same branch and Claude session —
/// or discard them (clean the repo and mark failed).
/// </summary>
public sealed class InterruptedTasksForm
{
    private readonly ITaskStore _tasks;
    private readonly ITaskResumer _resumer;

    public InterruptedTasksForm(ITaskStore tasks, ITaskResumer resumer)
    {
        _tasks = tasks;
        _resumer = resumer;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[yellow]Interrupted Tasks[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            var interrupted = await _tasks.ListAsync(TaskState.Interrupted, ct);
            if (interrupted.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No interrupted tasks.[/]");
                return;
            }

            var items = interrupted
                .OrderByDescending(h => h.CompletedUtc ?? h.CreatedUtc)
                .Select(h => new Row(h))
                .ToList();
            var back = new Row(null);

            AnsiConsole.MarkupLine($"[grey]{items.Count} interrupted task(s).[/]");
            AnsiConsole.WriteLine();

            var pick = AnsiConsole.Prompt(
                new SelectionPrompt<Row>()
                    .PageSize(20)
                    .Title("[cyan]Pick a task:[/]")
                    .UseConverter(r => r.Render())
                    .AddChoices(items)
                    .AddChoices(back));

            if (pick.Header is null) return;
            await HandleAsync(pick.Header, ct);
        }
    }

    private async Task HandleAsync(TaskHeader h, CancellationToken ct)
    {
        AnsiConsole.WriteLine();
        var cp = await _tasks.GetCheckpointAsync(h.Id, ct);
        var stages = cp is null
            ? "[grey](no checkpoint)[/]"
            : Markup.Escape(string.Join(", ", cp.Repos.Select(r => $"{r.RepoName}@{r.LastCompletedStage ?? "start"}{(r.ClaudeSessionId is not null ? "+session" : "")}")));

        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Task:[/]     {Markup.Escape(h.Id)}\n" +
                $"[cyan]Repos:[/]    {Markup.Escape(h.RepoSpec)}\n" +
                $"[cyan]Progress:[/] {stages}\n" +
                $"[cyan]Attempts:[/] {cp?.ResumeAttempts ?? 0}"))
            .Header("[yellow]Selected[/]")
            .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Action:[/]")
                .AddChoices("Resume", "Discard (clean repo, mark failed)", "Cancel"));

        switch (action)
        {
            case "Resume":
                if (await _resumer.ResumeAsync(h.Id, ct))
                    AnsiConsole.MarkupLine("[green]Re-queued. The orchestrator will resume it shortly.[/]");
                else
                    AnsiConsole.MarkupLine("[yellow]Could not resume — it may no longer be interrupted.[/]");
                break;
            case "Discard (clean repo, mark failed)":
                if (!AnsiConsole.Confirm($"Discard [yellow]{Markup.Escape(h.Id)}[/] and clean its repos?", false))
                {
                    AnsiConsole.MarkupLine("[grey]No action taken.[/]");
                    break;
                }
                AnsiConsole.MarkupLine(await _resumer.DiscardAsync(h.Id, ct)
                    ? "[green]Discarded and cleaned up.[/]"
                    : "[yellow]Could not discard — it may no longer be interrupted.[/]");
                break;
            default:
                return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private sealed record Row(TaskHeader? Header)
    {
        public string Render()
        {
            if (Header is null) return "(back)";
            var when = (Header.CompletedUtc ?? Header.CreatedUtc).ToString("MM-dd HH:mm");
            return $"[grey]{when}[/]  {Markup.Escape(Header.Id)}  [grey]{Markup.Escape(Header.RepoSpec)}[/]";
        }
    }
}
