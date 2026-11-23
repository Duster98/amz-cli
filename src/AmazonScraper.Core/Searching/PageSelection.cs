using System.Globalization;

namespace AmazonScraper.Core.Searching;

/// <summary>
/// Immutable set of search pages requested from the CLI. Supports single
/// pages ("2"), comma-separated lists ("1,3,5") and inclusive ranges
/// ("3-5", "1,3-5,8"). Pages are normalized, de-duplicated and sorted.
/// </summary>
public sealed class PageSelection
{
    public const int MinPage = 1;
    public const int MaxPage = 20;

    private PageSelection(IReadOnlyList<int> pages)
    {
        Pages = pages;
    }

    public IReadOnlyList<int> Pages { get; }

    public bool IsMultiPage => Pages.Count > 1;

    public static PageSelection Single(int page)
    {
        ValidatePage(page);
        return new PageSelection([page]);
    }

    /// <summary>Pages 1..<paramref name="count"/>.</summary>
    public static PageSelection First(int count)
    {
        if (count is < MinPage or > MaxPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"The number of pages must be between {MinPage} and {MaxPage}.");
        }

        return new PageSelection(Enumerable.Range(MinPage, count).ToArray());
    }

    public static bool TryParse(string? spec, out PageSelection selection, out string error)
    {
        selection = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "Specify at least one page (e.g. 2, 1,3,5 or 3-5).";
            return false;
        }

        var pages = new SortedSet<int>();
        foreach (var rawToken in spec.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                error = $"Invalid page selection: '{spec}'.";
                return false;
            }

            var dash = token.IndexOf('-');
            if (dash < 0)
            {
                if (!TryParsePage(token, out var page))
                {
                    error = PageError(token);
                    return false;
                }

                pages.Add(page);
                continue;
            }

            if (token.IndexOf('-', dash + 1) >= 0)
            {
                error = PageError(token);
                return false;
            }

            var startText = token[..dash].Trim();
            var endText = token[(dash + 1)..].Trim();
            if (!TryParsePage(startText, out var start) || !TryParsePage(endText, out var end))
            {
                error = PageError(token);
                return false;
            }

            if (start > end)
            {
                error = $"Invalid range: '{token}'. The start page must be less than or equal to the end page.";
                return false;
            }

            for (var page = start; page <= end; page++)
            {
                pages.Add(page);
            }
        }

        if (pages.Count == 0)
        {
            error = "Specify at least one page.";
            return false;
        }

        selection = new PageSelection(pages.ToArray());
        return true;
    }

    private static bool TryParsePage(string text, out int page)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page)
            && page is >= MinPage and <= MaxPage;
    }

    private static void ValidatePage(int page)
    {
        if (page is < MinPage or > MaxPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                $"The page must be between {MinPage} and {MaxPage}.");
        }
    }

    private static string PageError(string token) =>
        $"Invalid page: '{token}'. Use numbers between {MinPage} and {MaxPage}, separated by commas, or a range (e.g. 3-5).";
}
