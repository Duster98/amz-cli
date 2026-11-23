using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Reads the page count Amazon exposes in the search pagination strip.
/// Amazon caps result pagination at 20 pages; when the strip is absent the
/// caller should treat the result set as a single page.
/// </summary>
public static partial class SearchPaginationParser
{
    private const int MaxPages = 20;

    public static int? ParseTotalPages(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var strip = html.IndexOf(
            "s-pagination-strip",
            StringComparison.OrdinalIgnoreCase);
        if (strip < 0)
        {
            return null;
        }

        var end = html.IndexOf(
            "</div>",
            strip,
            StringComparison.OrdinalIgnoreCase);
        var length = end > strip
            ? end - strip
            : Math.Min(15_000, html.Length - strip);
        var block = html.AsSpan(strip, length);

        var maximum = 0;
        foreach (Match match in PaginationNumber().Matches(block.ToString()))
        {
            if (int.TryParse(match.Groups["page"].Value, out var page)
                && page > maximum)
            {
                maximum = page;
            }
        }

        return maximum == 0
            ? null
            : Math.Clamp(maximum, 1, MaxPages);
    }

    [GeneratedRegex(
        @"class=""[^""]*\bs-pagination-item\b[^""]*""[^>]*>\s*(?<page>\d+)\s*<",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PaginationNumber();
}
