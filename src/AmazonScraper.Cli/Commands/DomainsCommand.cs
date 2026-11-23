using AmazonScraper.Core.Marketplaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class DomainsCommand : Command
{
    public override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("ID", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Marketplace", new Style(Color.Cyan))));
        table.AddColumn(new TableColumn(new Text("Status", new Style(Color.Cyan))));

        foreach (var marketplace in MarketplaceRegistry.All)
        {
            table.AddRow(
                new Text(marketplace.Id, new Style(Color.Yellow)),
                new Text(marketplace.DisplayName),
                new Text(
                    marketplace.Available ? "available" : "not yet available",
                    new Style(marketplace.Available ? Color.Green : Color.Grey)));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
