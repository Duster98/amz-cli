using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class SearchDeliveryTests
{
    [Fact]
    public void Italy_search_delivery_is_opt_in()
    {
        var html = Fixtures.Load("search.html.gz");

        var without = new SearchParser(ItalyMarketplace.Instance).Parse(html);
        var with = new SearchParser(ItalyMarketplace.Instance).Parse(html, includeDelivery: true);

        Assert.Null(without[0].DeliveryText);
        Assert.NotNull(with[0].DeliveryText);
        Assert.Contains("Consegna", with[0].DeliveryText);

        Assert.DoesNotContain("delivery_text", JsonFormatter.Search(without));
        Assert.Contains("delivery_text", JsonFormatter.Search(with));

        Assert.Contains(with, result => result.FreeDelivery == true);
        var free = with.First(result => result.FreeDelivery == true);
        Assert.Contains(
            "GRATUITA",
            free.DeliveryText,
            StringComparison.OrdinalIgnoreCase);
        Assert.All(without, result => Assert.Null(result.FreeDelivery));

        Assert.DoesNotContain("Delivery", TsvFormatter.Search(with).Split('\n')[0]);
        Assert.Contains("Delivery", TsvFormatter.Search(with, includeDelivery: true).Split('\n')[0]);
    }

    [Fact]
    public void Uk_search_delivery_country_is_cleaned()
    {
        var html = Fixtures.Load("uk-search.html.gz");

        var results = new SearchParser(UnitedKingdomMarketplace.Instance)
            .Parse(html, includeDelivery: true);

        var result = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.DeliveryCountry));
        Assert.NotNull(result);
        Assert.Equal("United States", result!.DeliveryCountry);
        Assert.Contains("delivery", result.DeliveryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Japan_search_delivery_country_suffix_is_removed()
    {
        var html = Fixtures.Load("jp-search.html.gz");

        var results = new SearchParser(JapanMarketplace.Instance)
            .Parse(html, includeDelivery: true);

        var result = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.DeliveryCountry));
        Assert.NotNull(result);
        Assert.Equal("スイス", result!.DeliveryCountry);
        Assert.Contains("配送", result.DeliveryText);
    }
}
