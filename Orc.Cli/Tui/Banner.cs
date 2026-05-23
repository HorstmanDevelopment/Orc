using Spectre.Console;

namespace Orc.Cli.Tui;

public static class Banner
{
        private const string OrcArt = @"
        ,      ,
       /(.-""-.)\         ________ ___________________
       \/      \/         \_____  \\______   \_   ___ \
   __  / =.  .= \  __      /   |   \|       _/    \  \/
   \( \   o\/o   / )/     /    |    \    |   \     \____
    \_, '-/  \-' ,_/      \_______  /____|_  /\______  /
      /   \__/   \                \/       \/        \/
      \ \______/ /
       \        /
        `------`      
                        
";

    public static void Render()
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(OrcArt)}[/]");
        AnsiConsole.WriteLine();
    }
}
