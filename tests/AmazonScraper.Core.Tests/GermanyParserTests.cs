using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class GermanyParserTests
{
    [Fact]
    public void Germany_search_parses_eur_titles_ratings_and_delivery()
    {
        var html = Fixtures.Load("de-search.html.gz");
        var results = new SearchParser(GermanyMarketplace.Instance).Parse(html, includeDelivery: true);

        Assert.True(results.Count > 20);

        var priced = results.First(r => !string.IsNullOrEmpty(r.Price));
        Assert.EndsWith("€", priced.Price);
        Assert.DoesNotContain("Sponsored Ad", priced.Title);
        Assert.False(string.IsNullOrWhiteSpace(priced.Title));

        var rated = results.First(r => !string.IsNullOrEmpty(r.Rating));
        Assert.EndsWith("/ 5", rated.Rating);

        Assert.Contains(results, r => !string.IsNullOrEmpty(r.Reviews));
        Assert.Contains(results, r => r.Sponsored);
        Assert.Contains(results, r => !string.IsNullOrEmpty(r.DeliveryText));
        Assert.Contains(results, r => r.DeliveryCountry == "Italy");
    }

    [Fact]
    public void Germany_detail_parses_price_specs_and_delivery()
    {
        var html = Fixtures.Load("de-detail.html.gz");
        var detail = new DetailParser(GermanyMarketplace.Instance).Parse(html, "B0FX39Q2TN");

        Assert.StartsWith("Samsung Odyssey G50F", detail.Title);
        Assert.Equal("Samsung", detail.Brand);
        Assert.Equal("129,95 €", detail.Price);
        Assert.Equal("4,4 / 5", detail.Rating);
        Assert.Equal("141", detail.Reviews);
        Assert.Equal("In stock", detail.Availability);
        Assert.NotEmpty(detail.Specs);
        Assert.NotEmpty(detail.Images);
        Assert.Equal("https://www.amazon.de/dp/B0FX39Q2TN", detail.Url);

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Contains("11.59", delivery.Cost);
        Assert.Equal("Monday, 23 November", delivery.Time);
        Assert.Equal("Italy", delivery.Location);
    }

    [Fact]
    public void Germany_format_paths_and_cookie_use_eur()
    {
        Assert.Equal("129,95 €", GermanyFormat.Instance.Price("129", "95"));
        Assert.Equal("1.234,56 €", GermanyFormat.Instance.SinglePrice("€1,234.56"));
        Assert.True(GermanyFormat.Instance.TryParsePrice("129,95 €", out var amount));
        Assert.Equal(129.95m, amount);

        Assert.Equal("https://www.amazon.de/s?k=monitor", GermanyMarketplace.Instance.SearchUrl("monitor"));
        Assert.Equal("https://www.amazon.de/s?k=monitor&page=2", GermanyMarketplace.Instance.SearchUrl("monitor", 2));
        Assert.Equal("https://www.amazon.de/dp/B0FX39Q2TN", GermanyMarketplace.Instance.ProductUrl("B0FX39Q2TN"));
        Assert.Equal("DE", GermanyMarketplace.Instance.CountryCode);
        Assert.Contains("i18n-prefs=EUR", GermanyMarketplace.Instance.DefaultCookieHeader);
        Assert.False(GermanyMarketplace.Instance.WarmUpEnabled);
    }
}
