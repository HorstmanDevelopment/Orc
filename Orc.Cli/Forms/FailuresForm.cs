using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

/// <summary>
/// Browse failed tasks and drill into why each failed: the per-repo failing stage and
/// reason, plus the full pipeline transcript (claude output, git steps) from the
/// artifacts log. Built because failures were previously opaque in the UI.
/// </summary>
public sealed class FailuresForm
{
    private const int TranscriptTailLines = 300;

    private readonly ITaskStore _tasks;

    public FailuresForm(ITaskStore tasks) => _tasks = tasks;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[red]Failures[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            var failed = await _tasks.ListAsync(TaskState.Failed, ct);
            if (failed.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No failed tasks. \U0001f389[/]");
                return;
            }

            var items = failed
                .OrderByDescending(h => h.CompletedUtc ?? h.CreatedUtc)
                .Select(h => new Row(h))
                .ToList();
            var back = new Row(null);

            AnsiConsole.MarkupLine($"[grey]{items.Count} failed task(s). Select one to inspect.[/]");
            AnsiConsole.WriteLine();

            var pick = AnsiConsole.Prompt(
                new SelectionPrompt<Row>()
                    .PageSize(20)
                    .Title("[cyan]Pick a failed task:[/]")
                    .UseConverter(r => r.Render())
                    .AddChoices(items)
                    .AddChoices(back));

            if (pick.Header is null) return;

            ShowDetail(pick.Header);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return to the list...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ShowDetail(TaskHeader h)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[red]Failure: {Markup.Escape(h.Id)}[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var reason = h.Outcome?.Reason;
        AnsiConsole.Write(new Panel(new Markup(
                $"[cyan]Source:[/]    {Markup.Escape(h.Source.ToString())}\n" +
                $"[cyan]Repos:[/]     {Markup.Escape(h.RepoSpec)}\n" +
                $"[cyan]Created:[/]   {h.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC\n" +
                $"[cyan]Completed:[/] {(h.CompletedUtc is { } c ? c.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "-")}\n" +
                $"[cyan]Reason:[/]    {(string.IsNullOrWhiteSpace(reason) ? "[grey](none recorded)[/]" : $"[red]{Markup.Escape(reason)}[/]")}"))
            .Header("[yellow]Summary[/]")
            .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();

        var perRepo = h.Outcome?.PerRepo ?? [];
        if (perRepo.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Minimal)
                .Expand()
                .AddColumn("[yellow]Repo[/]")
                .AddColumn("[yellow]Exit[/]")
                .AddColumn("[yellow]Stage[/]")
                .AddColumn("[yellow]Reason[/]")
                .AddColumn("[yellow]Changes[/]");

            foreach (var r in perRepo)
            {
                var ok = r.ExitCode == 0;
                var exitColor = ok ? "green" : "red";
                table.AddRow(
                    Markup.Escape(r.RepoName),
                    $"[{exitColor}]{r.ExitCode}[/]",
                    Markup.Escape(r.FailedStage ?? (ok ? "-" : "?")),
                    Markup.Escape(r.FailReason ?? "-"),
                    r.HasChanges ? "[green]yes[/]" : "[grey]no[/]");
            }
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            foreach (var r in perRepo)
                ShowTranscript(r);
        }
    }

    private static void ShowTranscript(RepoResult r)
    {
        if (string.IsNullOrWhiteSpace(r.PerRepoLogPath))
        {
            AnsiConsole.MarkupLine($"[grey]No transcript recorded for {Markup.Escape(r.RepoName)}.[/]");
            return;
        }
        if (!File.Exists(r.PerRepoLogPath))
        {
            AnsiConsole.MarkupLine($"[grey]Transcript missing on disk: {Markup.Escape(r.PerRepoLogPath)}[/]");
            return;
        }

        string[] lines;
        try { lines = File.ReadAllLines(r.PerRepoLogPath); }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not read transcript: {Markup.Escape(ex.Message)}[/]");
            return;
        }

        var truncated = lines.Length > TranscriptTailLines;
        var shown = truncated ? lines[^TranscriptTailLines..] : lines;
        var body = Markup.Escape(string.Join("\n", shown));

        var header = truncated
            ? $"[yellow]Transcript: {Markup.Escape(r.RepoName)}[/]  [grey](last {TranscriptTailLines} of {lines.Length} lines · full: {Markup.Escape(r.PerRepoLogPath)})[/]"
            : $"[yellow]Transcript: {Markup.Escape(r.RepoName)}[/]  [grey]({Markup.Escape(r.PerRepoLogPath)})[/]";

        AnsiConsole.Write(new Panel(new Markup(body.Length == 0 ? "[grey](empty)[/]" : body))
            .Header(header, Justify.Left)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private sealed record Row(TaskHeader? Header)
    {
        public string Render()
        {
            if (Header is null) return "(back)";

            var when = (Header.CompletedUtc ?? Header.CreatedUtc).ToString("MM-dd HH:mm");
            var reason = Header.Outcome?.Reason;
            var shortReason = string.IsNullOrWhiteSpace(reason)
                ? "[grey]no reason recorded[/]"
                : $"[red]{Markup.Escape(Truncate(reason, 70))}[/]";
            return $"[grey]{when}[/]  {Markup.Escape(Header.Id)}\n        {shortReason}";
        }

        private static string Truncate(string s, int max)
        {
            s = s.ReplaceLineEndings(" ");
            return s.Length <= max ? s : s[..(max - 1)] + "…";
        }
    }
}
