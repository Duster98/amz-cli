using AmazonScraper.Core.Models;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class SearchResultFiltersTests
{
    [Fact]
    public void WithoutSponsored_keeps_order_and_removes_only_ads()
    {
        var results = new[]
        {
            new SearchResult { Asin = "A", Title = "organic 1" },
            new SearchResult { Asin = "B", Title = "ad", Sponsored = true },
            new SearchResult { Asin = "C", Title = "organic 2" },
        };

        var filtered = SearchResultFilters.WithoutSponsored(results);

        Assert.Equal(["A", "C"], filtered.Select(result => result.Asin));
    }

    [Fact]
    public void WithoutSponsored_returns_empty_when_only_ads_exist()
    {
        var results = new[]
        {
            new SearchResult { Asin = "A", Sponsored = true },
            new SearchResult { Asin = "B", Sponsored = true },
        };

        Assert.Empty(SearchResultFilters.WithoutSponsored(results));
    }
}
