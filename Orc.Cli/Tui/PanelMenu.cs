using Spectre.Console;
using Spectre.Console.Rendering;

namespace Orc.Cli.Tui;

public static class PanelMenu
{
    public static string Show(
        string header,
        string statusMarkup,
        IReadOnlyList<string> choices,
        Func<IRenderable?>? belowProvider = null)
    {
        var index = 0;
        string? result = null;

        AnsiConsole.Live(BuildLayout(header, statusMarkup, choices, index, belowProvider?.Invoke()))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Start(ctx =>
            {
                ctx.Refresh();
                while (result is null)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        switch (key.Key)
                        {
                            case ConsoleKey.UpArrow:
                                index = (index - 1 + choices.Count) % choices.Count;
                                break;
                            case ConsoleKey.DownArrow:
                                index = (index + 1) % choices.Count;
                                break;
                            case ConsoleKey.Enter:
                                result = choices[index];
                                break;
                        }
                    }
                    else
                    {
                        Thread.Sleep(150);
                    }
                    ctx.UpdateTarget(BuildLayout(header, statusMarkup, choices, index, belowProvider?.Invoke()));
                }
            });

        return result!;
    }

    private static IRenderable BuildLayout(string header, string statusMarkup, IReadOnlyList<string> choices, int selected, IRenderable? below)
    {
        var menu = BuildPanel(header, statusMarkup, choices, selected);
        return below is null ? menu : new Rows(menu, below);
    }

    private static Panel BuildPanel(string header, string statusMarkup, IReadOnlyList<string> choices, int selected)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(statusMarkup)) lines.Add(statusMarkup);
        lines.Add("");
        for (var i = 0; i < choices.Count; i++)
        {
            var prefix = i == selected ? "[green]>[/] " : "  ";
            var label = i == selected ? $"[bold green]{Markup.Escape(choices[i])}[/]" : Markup.Escape(choices[i]);
            lines.Add($"{prefix}{label}");
        }
        lines.Add("");

        return new Panel(new Markup(string.Join("\n", lines)))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Header($"[bold green]{header}[/]", Justify.Left)
            .Expand();
    }
}
