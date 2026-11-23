using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class DealCategoryTests
{
    private const string Html = """
        <a href="/s?rh=n%3A412609031%2Cp_n_deal_type%3A26901107031&ref=x">
          <span>Elettronica</span>
        </a>
        <a href="/s?rh=n%3A524015031%2Cp_n_deal_type%3A26901107031&ref=x">
          Casa e cucina
        </a>
        <a href="/s?rh=n%3A425916031%2Cp_n_deal_type%3A26901107031&ref=x">
          Informatica
        </a>
        """;

    [Fact]
    public void Parses_categories_and_node_ids()
    {
        var categories = DealCategoryCatalog.Parse(Html);

        Assert.Equal(3, categories.Count);
        Assert.Contains(categories, item => item is { Label: "Elettronica", NodeId: "412609031" });
        Assert.Contains(categories, item => item is { Label: "Casa e cucina", NodeId: "524015031" });
    }

    [Fact]
    public void Resolves_exact_and_partial_names()
    {
        var categories = DealCategoryCatalog.Parse(Html);

        Assert.True(DealCategoryCatalog.TryResolve(categories, "Elettronica", out var exact));
        Assert.Equal("412609031", exact.NodeId);

        Assert.True(DealCategoryCatalog.TryResolve(categories, "Casa", out var partial));
        Assert.Equal("524015031", partial.NodeId);

        Assert.False(DealCategoryCatalog.TryResolve(categories, "Non esiste", out _));
    }

    [Fact]
    public void Deals_url_contains_category_and_deal_refinements()
    {
        var url = MarketplaceUrlBuilder.Deals(
            ItalyMarketplace.Instance,
            "26901107031",
            page: 1,
            categoryNodeId: "412609031");

        Assert.Contains("rh=n%3A412609031%2Cp_n_deal_type%3A26901107031", url);
    }
}
