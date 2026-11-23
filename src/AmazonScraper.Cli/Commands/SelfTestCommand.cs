using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class SelfTestCommand : Command<SelfTestSettings>
{
    public override int Execute(CommandContext context, SelfTestSettings settings, CancellationToken cancellationToken)
    {
        var result = RuntimeDiagnostics.Run(VersionInfo.Informational);

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("Item", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Value", new Style(Color.Cyan))));
        table.AddRow("Version", result.AppVersion);
        table.AddRow("OS", result.OperatingSystem);
        table.AddRow("Runtime RID", result.RuntimeIdentifier);
        table.AddRow(".NET", result.DotNetVersion);
        table.AddRow("Transport", result.Transport);
        table.AddRow("curl mode", result.CurlMode);
        table.AddRow("curl", result.CurlVersion);
        table.AddRow("curl RID/path", result.CurlRuntimeIdentifier);
        table.AddRow("Marketplace", string.Join(", ", MarketplaceRegistry.AvailableIds));
        var cooldowns = TransportCooldownStore.Default.ListActive();
        table.AddRow(
            "HTTP cooldown",
            cooldowns.Count == 0
                ? "none"
                : string.Join(
                    "; ",
                    cooldowns.Select(item =>
                        $"{item.Domain} until " +
                        $"{item.Until.ToLocalTime():yyyy-MM-dd HH:mm}")));
        if (!string.IsNullOrEmpty(result.CurlError))
        {
            table.AddRow("curl error", result.CurlError);
        }
        if (result.CurlFiles.Count > 0)
        {
            table.AddRow(
                "curl files",
                string.Join(
                    ", ",
                    result.CurlFiles.Select(file =>
                        $"{file.Name}: {(file.Exists ? "OK" : "missing")}")));
        }

        AnsiConsole.Write(table);

        if (!result.CurlAvailable)
        {
            CliConsole.Error(
                "curl-impersonate is not available. On Linux, install the " +
                "curl-impersonate package (e.g. 'pacman -S curl-impersonate') or set " +
                "AMZ_CURL_IMPERSONATE (AMZ_UK_CURL is a legacy alias). " +
                "On Windows, verify the native libraries in the publish output.");
            return settings.AllowHttp ? 0 : 1;
        }

        return 0;
    }
}
