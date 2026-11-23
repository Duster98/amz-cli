using Spectre.Console;

namespace AmazonScraper.Cli.Rendering;

public static class CliConsole
{
    private static readonly Lazy<IAnsiConsole> ErrorConsole = new(() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(Console.Error),
        }));

    public static IAnsiConsole Stdout(int width = 200)
    {
        var console = AnsiConsole.Console;
        console.Profile.Width = Math.Max(40, width);
        return console;
    }

    public static IAnsiConsole Stderr => ErrorConsole.Value;

    public static void Error(string message)
    {
        Stderr.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }
}
