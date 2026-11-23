using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AmazonScraper.Cli.Rendering;

public static class SearchTableRenderer
{
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);

    public static void Render(
        IAnsiConsole console,
        IReadOnlyList<SearchResult> results,
        bool includeDelivery = false)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };

        var hasDeals = results.Any(result =>
            !string.IsNullOrWhiteSpace(result.Deal));
        var hasPriceReference = results.Any(result =>
            !string.IsNullOrWhiteSpace(result.OriginalPrice)
            || !string.IsNullOrWhiteSpace(result.Discount));
        var hasChoice = results.Any(result => result.AmazonChoice == true);

        table.AddColumn(new TableColumn(new Text("#", new Style(Color.Grey))) { Width = 3, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("ASIN", new Style(Color.Yellow))) { Width = 12, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Title", HeaderStyle)) { Width = 62, NoWrap = true });
        if (hasChoice)
        {
            table.AddColumn(new TableColumn(new Text("Choice", new Style(Color.Cyan))) { Width = 6, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green))) { Width = 12, NoWrap = true });
        if (hasPriceReference)
        {
            table.AddColumn(new TableColumn(new Text("Was", new Style(Color.Grey))) { Width = 12, NoWrap = true });
            table.AddColumn(new TableColumn(new Text("Disc.", new Style(Color.Red))) { Width = 7, NoWrap = true });
        }
        if (hasDeals)
        {
            table.AddColumn(new TableColumn(new Text("Badge", new Style(Color.Red))) { Width = 24, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Rating", new Style(Color.Yellow))) { Width = 9, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Reviews", HeaderStyle)) { Width = 9, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Coupon", new Style(Color.Magenta))) { Width = 22, NoWrap = true });
        if (includeDelivery)
        {
            table.AddColumn(new TableColumn(new Text("Delivery", HeaderStyle)) { Width = 30, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Ad", new Style(Color.Grey))) { Width = 3, NoWrap = true });

        var index = 0;
        foreach (var result in results)
        {
            index++;
            var row = new List<IRenderable>
            {
                new Text(index.ToString(), new Style(Color.Grey)),
                new Text(result.Asin, new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(result.Title, 60)).Overflow(Overflow.Ellipsis),
            };
            if (hasChoice)
            {
                row.Add(new Text(
                    result.AmazonChoice == true ? "✓" : "-",
                    new Style(Color.Cyan)));
            }
            row.Add(new Text(string.IsNullOrEmpty(result.Price) ? "-" : result.Price, new Style(Color.Green)));
            if (hasPriceReference)
            {
                row.Add(new Text(
                    string.IsNullOrEmpty(result.OriginalPrice) ? "-" : result.OriginalPrice,
                    new Style(Color.Grey)));
                row.Add(new Text(
                    string.IsNullOrEmpty(result.Discount) ? "-" : result.Discount,
                    new Style(Color.Red)));
            }
            if (hasDeals)
            {
                row.Add(new Text(
                        string.IsNullOrEmpty(result.Deal) ? "-" : TextCleaner.Clean(result.Deal, 24),
                        new Style(Color.Red))
                    .Overflow(Overflow.Ellipsis));
            }

            row.Add(new Text(string.IsNullOrEmpty(result.Rating) ? "-" : result.Rating, new Style(Color.Yellow)));
            row.Add(new Text(string.IsNullOrEmpty(result.Reviews) ? "-" : result.Reviews));
            row.Add(new Text(
                    string.IsNullOrEmpty(result.Coupon) ? "-" : TextCleaner.Clean(result.Coupon, 22),
                    new Style(Color.Magenta))
                .Overflow(Overflow.Ellipsis));

            if (includeDelivery)
            {
                var delivery = SearchDeliveryFormatter.Format(result);
                row.Add(new Text(string.IsNullOrEmpty(delivery) ? "-" : delivery));
            }

            row.Add(new Text(result.Sponsored ? "AD" : string.Empty, new Style(Color.Grey)));
            table.AddRow(row.ToArray());
        }

        console.Write(table);
    }
}
