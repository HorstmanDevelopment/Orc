using Orc.Core.Tasks;
using Spectre.Console;

namespace Orc.Cli.Forms;

public sealed class ClearHistoryForm
{
    private readonly ITaskStore _tasks;

    public ClearHistoryForm(ITaskStore tasks) => _tasks = tasks;

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]Clear Task History[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var succeeded = await _tasks.ListAsync(TaskState.Succeeded, ct);
        var failed = await _tasks.ListAsync(TaskState.Failed, ct);
        var total = succeeded.Count + failed.Count;

        if (total == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing to clear.[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[yellow]Found {succeeded.Count} succeeded and {failed.Count} failed task record(s).[/]");
        if (!AnsiConsole.Confirm("[red]Delete all of them?[/]", false)) return;

        var n = 0;
        n += await _tasks.PurgeAsync(TaskState.Succeeded, ct);
        n += await _tasks.PurgeAsync(TaskState.Failed, ct);
        AnsiConsole.MarkupLine($"[green]Deleted {n} record(s).[/]");
    }
}
