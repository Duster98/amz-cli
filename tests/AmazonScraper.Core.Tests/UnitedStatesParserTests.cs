using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class UnitedStatesParserTests
{
    [Fact]
    public void Us_search_uses_dollar_prices_and_aria_titles()
    {
        var html = Fixtures.Load("us-search.html.gz");
        var results = new SearchParser(UnitedStatesMarketplace.Instance).Parse(html);

        Assert.Equal(18, results.Count);
        Assert.Equal(15, results.Count(r => !string.IsNullOrEmpty(r.Price)));
        Assert.Equal(18, results.Count(r => !string.IsNullOrEmpty(r.Title)));
        Assert.Equal(18, results.Count(r => !string.IsNullOrEmpty(r.Rating)));

        var first = results[0];
        Assert.Equal("B0GWRN4Q36", first.Asin);
        Assert.Contains("RTX 5080", first.Title);
        Assert.Equal("$2,736.03", first.Price);
        Assert.Equal("4.6 / 5", first.Rating);
        Assert.Equal("37", first.Reviews);
    }

    [Fact]
    public void Us_search_parses_images_outside_the_images_i_path()
    {
        var html = Fixtures.Load("us-search-x-image.html.gz");
        var results = new SearchParser(UnitedStatesMarketplace.Instance).Parse(html);
        var item = results.First(r => r.Asin == "B0HR89KGFF");

        Assert.Equal("$399.99", item.Price);
        Assert.StartsWith(
            "https://m.media-amazon.com/images/X/",
            item.Image);
    }

    [Fact]
    public void Us_detail_without_buybox_does_not_use_sponsored_prices()
    {
        var html = Fixtures.Load("us-detail-out-of-stock.html.gz");
        var detail = new DetailParser(UnitedStatesMarketplace.Instance)
            .Parse(html, "B0CHXRCG91");

        Assert.Equal(string.Empty, detail.Price);
        Assert.Equal(string.Empty, detail.OriginalPrice);
        Assert.Equal(string.Empty, detail.Discount);
    }

    [Fact]
    public void Us_detail_uses_dollar_and_amazon_com_url()
    {
        var html = Fixtures.Load("us-detail.html.gz");
        var detail = new DetailParser(UnitedStatesMarketplace.Instance).Parse(html, "B0H4SBSYBS");

        Assert.Equal("$1,699.99", detail.Price);
        Assert.Equal("4.3 / 5", detail.Rating);
        Assert.StartsWith("MichaelElectronics2", detail.Seller);
        Assert.StartsWith("MichaelElectronics2", detail.ShippedBy);
        Assert.Equal("In Stock", detail.Availability);
        Assert.NotEmpty(detail.Images);
        Assert.NotEmpty(detail.Description);
        Assert.NotEmpty(detail.Specs);
        Assert.Equal("https://www.amazon.com/dp/B0H4SBSYBS", detail.Url);
    }

    [Fact]
    public void Us_detail_uses_logo_brand_for_non_fashion_items()
    {
        var html = Fixtures.Load("us-rubik.html.gz");
        var detail = new DetailParser(UnitedStatesMarketplace.Instance).Parse(html, "B086PNKX2P");

        Assert.Equal("GAN", detail.Brand);
        Assert.Equal("$27.99", detail.Price);
        Assert.Equal("4.8 / 5", detail.Rating);
        Assert.NotEmpty(detail.Specs);
        Assert.Contains("61UWEny1PNL", detail.Image);
        Assert.Equal("https://www.amazon.com/dp/B086PNKX2P", detail.Url);
    }

    [Fact]
    public void Us_format_paths_and_cookie_force_usd()
    {
        Assert.Equal("$2,736.03", UnitedStatesFormat.Instance.Price("2,736", "03"));
        Assert.Equal("$1,699.99", UnitedStatesFormat.Instance.OriginalPrice("$1,699.99"));
        Assert.True(UnitedStatesFormat.Instance.TryParsePrice("$2,736.03", out var amount));
        Assert.Equal(2736.03m, amount);

        Assert.Equal(
            "https://www.amazon.com/-/en/s?k=rtx%205080",
            UnitedStatesMarketplace.Instance.SearchUrl("rtx 5080"));
        Assert.Equal("https://www.amazon.com/-/en/", UnitedStatesMarketplace.Instance.WarmUpUrl);
        Assert.Equal("i18n-prefs=USD", UnitedStatesMarketplace.Instance.DefaultCookieHeader);
        Assert.False(UnitedStatesMarketplace.Instance.WarmUpEnabled);
    }
}
