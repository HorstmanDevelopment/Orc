using Spectre.Console;

namespace Orc.Cli.Tui;

public static class Banner
{
    private const string Art = """
         ██████╗ ██████╗  ██████╗
        ██╔═══██╗██╔══██╗██╔════╝
        ██║   ██║██████╔╝██║
        ██║   ██║██╔══██╗██║
        ╚██████╔╝██║  ██║╚██████╗
         ╚═════╝ ╚═╝  ╚═╝ ╚═════╝
        """;

    public static void Render()
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(Art)}[/]");
        AnsiConsole.MarkupLine("[grey]Multi-repo Claude orchestrator with Orchitect[/]");
        AnsiConsole.WriteLine();
    }
}
