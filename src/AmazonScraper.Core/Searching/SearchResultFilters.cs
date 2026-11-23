using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Searching;

public static class SearchResultFilters
{
    /// <summary>
    /// Removes sponsored placements while preserving the original result order.
    /// </summary>
    public static List<SearchResult> WithoutSponsored(IEnumerable<SearchResult> results) =>
        results.Where(result => !result.Sponsored).ToList();
}
