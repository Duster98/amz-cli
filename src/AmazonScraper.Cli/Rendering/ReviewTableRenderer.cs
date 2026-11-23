using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Models;
using Spectre.Console;

namespace AmazonScraper.Cli.Rendering;

public static class ReviewTableRenderer
{
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);

    public static void Render(IAnsiConsole console, TopReviews reviews)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };

        table.AddColumn(new TableColumn(new Text("#", HeaderStyle)) { Width = 3, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Author", HeaderStyle)) { Width = 20, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Stars", HeaderStyle)) { Width = 7, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Date", HeaderStyle)) { Width = 34 });
        table.AddColumn(new TableColumn(new Text("Review", HeaderStyle))
        {
            Width = Math.Clamp(console.Profile.Width - 78, 30, 140),
        });

        var index = 1;
        foreach (var review in reviews.Items)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(review.Title))
            {
                lines.Add(TextCleaner.Clean(review.Title, 100));
            }
            if (!string.IsNullOrEmpty(review.Body))
            {
                lines.Add(TextCleaner.Clean(review.Body, 240));
            }

            var meta = new List<string>();
            if (review.VerifiedPurchase)
            {
                meta.Add("✓ verified");
            }
            if (!string.IsNullOrEmpty(review.Helpful))
            {
                meta.Add(TextCleaner.Clean(review.Helpful, 60));
            }
            if (!string.IsNullOrEmpty(review.Variant))
            {
                meta.Add(TextCleaner.Clean(review.Variant, 90));
            }
            if (meta.Count > 0)
            {
                lines.Add(string.Join(" · ", meta));
            }

            table.AddRow(
                new Text(index++.ToString(), new Style(Color.Cyan)),
                new Text(TextCleaner.Clean(review.Author, 20)).Overflow(Overflow.Ellipsis),
                new Text(
                    string.IsNullOrEmpty(review.Rating) ? "-" : review.Rating,
                    new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(review.Date)).Overflow(Overflow.Ellipsis),
                new Text(lines.Count == 0 ? "-" : string.Join('\n', lines)));
        }

        console.Write(table);
    }
}
