using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class DoctorCommand : AsyncCommand<DoctorSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        DoctorSettings settings,
        CancellationToken cancellationToken)
    {
        var report = await DoctorService.RunAsync(
                new DoctorOptions
                {
                    Offline = settings.Offline,
                    Channel = (settings.Channel ?? "stable")
                        .Trim()
                        .ToLowerInvariant(),
                    InstallDir = settings.InstallDir,
                    StateRoot = settings.StateRoot,
                    SkillDirectory = settings.SkillDirectory,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (settings.Json)
        {
            Console.Out.WriteLine(
                JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            Render(report);
        }

        return report.HasErrors ? 1 : 0;
    }

    private static void Render(DoctorReport report)
    {
        Console.Out.WriteLine($"AMZ CLI Doctor {report.Version}");
        Console.Out.WriteLine($"Platform: {report.Platform}");
        Console.Out.WriteLine($"Installation: {report.InstallDir}");
        Console.Out.WriteLine($"User state: {report.StateRoot}");
        Console.Out.WriteLine();

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("Check", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Status", new Style(Color.Cyan))));
        table.AddColumn(new TableColumn("Detail"));

        foreach (var check in report.Checks)
        {
            table.AddRow(
                new Text(check.Title),
                new Markup(FormatStatus(check.Status)),
                new Text(check.Detail));
        }

        CliConsole.Stdout().Write(table);
        Console.Out.WriteLine();
        Console.Out.WriteLine(
            report.ErrorCount == 0
                ? report.WarningCount == 0
                    ? "Result: no errors, no warnings."
                    : $"Result: no errors, {report.WarningCount} warnings."
                : $"Result: {report.ErrorCount} errors, " +
                  $"{report.WarningCount} warnings.");
    }

    private static string FormatStatus(DoctorStatus status) =>
        status switch
        {
            DoctorStatus.Ok => "[green]ok[/]",
            DoctorStatus.Info => "[blue]info[/]",
            DoctorStatus.Warning => "[yellow]warning[/]",
            DoctorStatus.Error => "[red]error[/]",
            _ => status.ToString(),
        };
}
