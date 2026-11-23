using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AmazonScraper.Cli.Rendering;

public static class CompareTableRenderer
{
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);

    public static void Render(
        IAnsiConsole console,
        IReadOnlyList<ProductDetail> details,
        IMarketplaceFormat format)
    {
        var prices = details
            .Select(d => PriceParser.TryParse(d.Price, format))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToList();
        decimal? cheapest = prices.Count > 1 ? prices.Min() : null;
        var hasChoice = details.Any(detail => detail.AmazonChoice == true);
        var hasLanded = details.Any(detail => detail.ImportFees is { } fees
            && (!string.IsNullOrEmpty(fees.LandedTotal)
                || !string.IsNullOrEmpty(fees.ImportCharges)
                || fees.AtCheckout));

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };

        var hasCharger = details.Any(detail => detail.Charger is not null);

        table.AddColumn(new TableColumn(new Text("#", new Style(Color.Grey))) { Width = 3, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("ASIN", new Style(Color.Yellow))) { Width = 12, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Title", HeaderStyle)) { Width = 30, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Brand", HeaderStyle)) { Width = 9, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green))) { Width = 11, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Original", new Style(Color.Grey))) { Width = 11, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Disc.", new Style(Color.Magenta))) { Width = 7, NoWrap = true });
        if (hasLanded)
        {
            table.AddColumn(new TableColumn(new Text("Landed", new Style(Color.Green))) { Width = 12, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Coupon", new Style(Color.Magenta))) { Width = 9, NoWrap = true });
        if (hasChoice)
        {
            table.AddColumn(new TableColumn(new Text("Choice", new Style(Color.Cyan))) { Width = 8, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Rating", new Style(Color.Yellow))) { Width = 8, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Reviews", HeaderStyle)) { Width = 9, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Seller", HeaderStyle)) { Width = 12, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Avail", HeaderStyle)) { Width = 14, NoWrap = true });
        if (hasCharger)
        {
            table.AddColumn(new TableColumn(new Text("Charger", HeaderStyle)) { Width = 8, NoWrap = true });
        }
        table.AddColumn(new TableColumn(new Text("Delivery", HeaderStyle)) { Width = 18, NoWrap = true });

        var index = 0;
        foreach (var detail in details)
        {
            index++;
            var price = string.IsNullOrEmpty(detail.Price) ? "-" : detail.Price;
            var parsed = PriceParser.TryParse(detail.Price, format);
            if (cheapest is not null && parsed == cheapest)
            {
                price = $"{price} ★";
            }

            var row = new List<IRenderable>
            {
                new Text(index.ToString(), new Style(Color.Grey)),
                new Text(detail.Asin, new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(detail.Title, 40)).Overflow(Overflow.Ellipsis),
                new Text(string.IsNullOrEmpty(detail.Brand) ? "-" : detail.Brand),
                new Text(price, new Style(Color.Green)),
                new Text(string.IsNullOrEmpty(detail.OriginalPrice) ? "-" : detail.OriginalPrice, new Style(Color.Grey)),
                new Text(string.IsNullOrEmpty(detail.Discount) ? "-" : detail.Discount, new Style(Color.Magenta)),
            };
            if (hasLanded)
            {
                row.Add(new Text(FormatLanded(detail), new Style(Color.Green)));
            }
            row.Add(new Text(string.IsNullOrEmpty(detail.Coupon) ? "-" : detail.Coupon, new Style(Color.Magenta)));
            if (hasChoice)
            {
                row.Add(new Text(
                    detail.AmazonChoice == true ? "✓" : "-",
                    new Style(Color.Cyan)));
            }
            row.Add(new Text(string.IsNullOrEmpty(detail.Rating) ? "-" : detail.Rating, new Style(Color.Yellow)));
            row.Add(new Text(string.IsNullOrEmpty(detail.Reviews) ? "-" : detail.Reviews));
            row.Add(new Text(string.IsNullOrEmpty(detail.Seller) ? "-" : detail.Seller));
            row.Add(new Text(string.IsNullOrEmpty(detail.Availability) ? "-" : detail.Availability));
            if (hasCharger)
            {
                var chargerValue = detail.Charger?.Included switch
                {
                    false => "No",
                    true => "Yes",
                    _ => "-",
                };
                row.Add(new Text(chargerValue));
            }
            row.Add(new Text(FormatDelivery(detail)));
            table.AddRow(row.ToArray());
        }

        console.Write(table);

        if (details.Any(detail =>
                string.Equals(
                    detail.ImportFees?.PriceBasis,
                    "list",
                    StringComparison.OrdinalIgnoreCase)))
        {
            console.MarkupLine(
                "[grey]* Landed total calculated on the Amazon list price.[/]");
        }
    }

    private static string FormatLanded(ProductDetail detail)
    {
        if (detail.ImportFees is not { } fees)
        {
            return "-";
        }

        if (!string.IsNullOrEmpty(fees.LandedTotal))
        {
            return string.Equals(
                    fees.PriceBasis,
                    "list",
                    StringComparison.OrdinalIgnoreCase)
                ? $"{fees.LandedTotal}*"
                : fees.LandedTotal;
        }
        if (fees.AtCheckout)
        {
            return "checkout";
        }
        return string.IsNullOrEmpty(fees.ImportCharges) ? "-" : fees.ImportCharges;
    }

    private static string FormatDelivery(ProductDetail detail)
    {
        if (detail.Delivery is not { } delivery)
        {
            return "-";
        }

        var text = string.Join(
            " · ",
            new[] { delivery.Location, delivery.Cost, delivery.Time }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrEmpty(text) ? "-" : TextCleaner.Clean(text, 18);
    }
}
