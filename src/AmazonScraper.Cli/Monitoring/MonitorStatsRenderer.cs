using System.Text.Encodings.Web;
using System.Text.Json;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Core.Monitoring;
using Spectre.Console;

namespace AmazonScraper.Cli.Monitoring;

public static class MonitorStatsRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Render(MonitorStatsReport report)
    {
        Console.Out.WriteLine($"State: {report.StatePath}");
        if (report.Targets == 0 && report.Groups == 0)
        {
            Console.Out.WriteLine("No monitor data available.");
            return;
        }

        if (report.TargetRows.Count > 0)
        {
            var table = new Table
            {
                Border = TableBorder.HeavyHead,
                BorderStyle = new Style(Color.Cyan),
            };
            table.AddColumn(new TableColumn(new Text("Domain", new Style(Color.Yellow))));
            table.AddColumn(new TableColumn("ASIN"));
            table.AddColumn(new TableColumn("First") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Min") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Max") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Last") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Last cycle"));
            table.AddColumn(new TableColumn("Requests") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Alert") { Alignment = Justify.Right });

            foreach (var target in report.TargetRows)
            {
                table.AddRow(
                    new Text(target.Domain),
                    new Text(target.Asin),
                    new Text(FormatPrice(target.FirstPrice, target.Currency)),
                    new Text(FormatPrice(target.MinPrice, target.Currency)),
                    new Text(FormatPrice(target.MaxPrice, target.Currency)),
                    new Text(FormatPrice(target.LastPrice, target.Currency)),
                    new Text(target.LastSeenAt ?? "—"),
                    new Text(target.Observations.ToString()),
                    new Text(target.AlertedRules.ToString()));
            }

            CliConsole.Stdout().Write(table);
        }

        if (report.GroupRows.Count > 0)
        {
            Console.Out.WriteLine();
            var table = new Table
            {
                Border = TableBorder.HeavyHead,
                BorderStyle = new Style(Color.Cyan),
            };
            table.AddColumn(new TableColumn(new Text("Group", new Style(Color.Yellow))));
            table.AddColumn(new TableColumn("Name"));
            table.AddColumn(new TableColumn("Items") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Total") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Min") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Max") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Last cycle"));
            table.AddColumn(new TableColumn("Requests") { Alignment = Justify.Right });
            table.AddColumn(new TableColumn("Alert") { Alignment = Justify.Right });

            foreach (var group in report.GroupRows)
            {
                table.AddRow(
                    new Text(group.Id),
                    new Text(group.Name ?? "—"),
                    new Text(group.Items.ToString()),
                    new Text(FormatPrice(group.LastTotal, group.Currency)),
                    new Text(FormatPrice(group.MinTotal, group.Currency)),
                    new Text(FormatPrice(group.MaxTotal, group.Currency)),
                    new Text(group.LastSeenAt ?? "—"),
                    new Text(group.Observations.ToString()),
                    new Text(group.AlertedRules.ToString()));
            }

            CliConsole.Stdout().Write(table);
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"Targets: {report.Targets}; groups: {report.Groups}; " +
            $"rules with alerts: {report.AlertedRules}; " +
            $"last cycle: {report.LastSeenAt ?? "—"}");
    }

    public static string ToJson(MonitorStatsReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    private static string FormatPrice(decimal? value, string? currency)
    {
        if (value is null)
        {
            return "—";
        }

        var amount = MonitorObservationFactory.FormatAmount(value.Value);
        return string.IsNullOrWhiteSpace(currency)
            ? amount
            : $"{amount} {currency}";
    }
}
