using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AmazonScraper.Cli.Rendering;

public static class VariantTableRenderer
{
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);

    public static void Render(IAnsiConsole console, ProductVariants variants)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };

        table.AddColumn(new TableColumn(new Text("ASIN", new Style(Color.Yellow))) { Width = 12, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Sel", HeaderStyle)) { Width = 3, NoWrap = true });
        foreach (var label in variants.Dimensions.Values)
        {
            table.AddColumn(new TableColumn(new Text(TextCleaner.Clean(label, 24), HeaderStyle))
            {
                Width = 24,
                NoWrap = true,
            });
        }
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green))) { Width = 12, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Availability", HeaderStyle)) { Width = 20, NoWrap = true });

        foreach (var item in variants.Items)
        {
            var row = new List<IRenderable>
            {
                new Text(item.Asin, new Style(Color.Yellow)),
                new Text(item.Selected ? "*" : string.Empty, new Style(Color.Cyan)),
            };
            foreach (var label in variants.Dimensions.Values)
            {
                row.Add(new Text(
                        item.Attributes.TryGetValue(label, out var value)
                            ? TextCleaner.Clean(value, 24)
                            : "-")
                    .Overflow(Overflow.Ellipsis));
            }
            row.Add(new Text(string.IsNullOrEmpty(item.Price) ? "-" : item.Price, new Style(Color.Green)));
            row.Add(new Text(
                    string.IsNullOrEmpty(item.Availability) ? "-" : TextCleaner.Clean(item.Availability, 20))
                .Overflow(Overflow.Ellipsis));
            table.AddRow(row.ToArray());
        }

        console.Write(table);
    }
}
