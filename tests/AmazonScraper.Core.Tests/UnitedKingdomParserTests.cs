using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class UnitedKingdomParserTests
{
    [Fact]
    public void Uk_search_uses_aria_title_and_gbp_prices()
    {
        var html = Fixtures.Load("uk-search.html.gz");
        var results = new SearchParser(UnitedKingdomMarketplace.Instance).Parse(html);

        Assert.Equal(25, results.Count);
        Assert.Equal(9, results.Count(r => r.Sponsored));
        Assert.Equal(21, results.Count(r => !string.IsNullOrEmpty(r.Price)));
        Assert.Equal(24, results.Count(r => !string.IsNullOrEmpty(r.Rating)));

        var first = results[0];
        Assert.Equal("B08JH7KQKV", first.Asin);
        Assert.StartsWith("ProArt Display PA279CV", first.Title);
        Assert.Equal("£237.37", first.Price);
        Assert.Equal("4.4 / 5", first.Rating);
        Assert.Equal("1,298", first.Reviews);
    }

    [Fact]
    public void Uk_detail_extracts_gbp_original_and_local_fields()
    {
        var html = Fixtures.Load("uk-detail.html.gz");
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance).Parse(html, "B08JH7KQKV");

        Assert.Equal("£237.37", detail.Price);
        Assert.Equal("£265.82", detail.OriginalPrice);
        Assert.Equal("-11%", detail.Discount);
        Assert.Equal("4.4 / 5", detail.Rating);
        Assert.Equal("1,298", detail.Reviews);
        Assert.Equal("Amazon", detail.Seller);
        Assert.Equal("Amazon", detail.ShippedBy);
        Assert.Contains("Returnable", detail.Returns);
        Assert.Equal("In stock", detail.Availability);
        Assert.NotEmpty(detail.Images);
        Assert.NotEmpty(detail.Description);
        Assert.Equal(12, detail.Specs.Count);
        Assert.Equal("https://www.amazon.co.uk/dp/B08JH7KQKV", detail.Url);
    }

    [Fact]
    public void Uk_fashion_detail_extracts_store_brand_and_image()
    {
        var html = Fixtures.Load("uk-felpa.html.gz");
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance).Parse(html, "B08YX42D6F");

        Assert.Equal("Graphic Impact", detail.Brand);
        Assert.Equal("£11.70", detail.Price);
        Assert.Empty(detail.OriginalPrice);
        Assert.Contains("911", detail.Image); // hiRes/landing image
        Assert.Equal("Graphic Impact", detail.Seller);
        Assert.Equal("Amazon", detail.ShippedBy);
    }

    [Fact]
    public void Uk_search_skips_lazy_ajax_carousel_placeholders()
    {
        var html = Fixtures.Load("uk-search-honor.html.gz");
        var results = new SearchParser(UnitedKingdomMarketplace.Instance).Parse(html);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => string.IsNullOrWhiteSpace(result.Title));
        Assert.DoesNotContain(results, result => result.Asin == "B0FFG89T19");
    }

    [Fact]
    public void Uk_mobile_detail_fallbacks_still_extract_fields()
    {
        var html = Fixtures.Load("uk-detail-mobile.html.gz");
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance).Parse(html, "B08JH7KQKV");

        Assert.StartsWith("ASUS ProArt Display PA279CV", detail.Title);
        Assert.Equal("ASUS", detail.Brand);
        Assert.Equal("£237.37", detail.Price);
        Assert.Equal("1,298", detail.Reviews);
        Assert.StartsWith("https://m.media-amazon.com/images/I/81LYZ", detail.Image);
    }

    [Fact]
    public void Uk_unavailable_detail_does_not_take_sponsored_prices()
    {
        var html = Fixtures.Load("uk-detail-unavailable.html.gz");
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance).Parse(html, "B0BY2R4BHW");

        Assert.StartsWith("ASUS ProArt Display PA279CRV", detail.Title);
        Assert.Equal("ASUS", detail.Brand);
        Assert.Empty(detail.Price);
        Assert.Empty(detail.OriginalPrice);
        Assert.Equal("4.3 / 5", detail.Rating);
        Assert.Equal("524", detail.Reviews);
        Assert.Empty(detail.Availability);
        Assert.Equal("https://www.amazon.co.uk/dp/B0BY2R4BHW", detail.Url);
    }

    [Fact]
    public void Uk_mobile_header_price_uses_buybox_original()
    {
        var html = Fixtures.Load("uk-detail-mobile-header.html.gz");
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance).Parse(html, "B0DN6NHW6F");

        Assert.Equal("£68.96", detail.Price);
        Assert.Equal("£89.00", detail.OriginalPrice);
        Assert.Equal("-23%", detail.Discount);
        Assert.Equal("In stock", detail.Availability);
    }

    [Fact]
    public void Uk_detail_reads_was_price_when_offscreen_is_null()
    {
        var html = """
            <span id="productTitle">adidas Men's Galaxy 8 Running Shoes</span>
            <div id="corePriceDisplay_desktop_feature_div">
              <span class="a-price">
                <span class="a-offscreen">£29.25</span>
                <span aria-hidden="true">£29.25</span>
              </span>
              <span class="a-price a-text-price apex-basis-price-value" data-a-size="mini" data-a-strike="true">
                <span class="a-offscreen">null</span>
                <span aria-hidden="true">£45.00</span>
              </span>
            </div>
            """;
        var detail = new DetailParser(UnitedKingdomMarketplace.Instance)
            .Parse(html, "B0FW2RGQDR");

        Assert.Equal("£29.25", detail.Price);
        Assert.Equal("£45.00", detail.OriginalPrice);
        Assert.Equal("-35%", detail.Discount);
    }

    [Fact]
    public void Uk_search_reads_list_price_when_offscreen_is_null()
    {
        var html = """
            <div data-asin="B0FW2RGQDR" data-component-type="s-search-result">
              <h2 aria-label="adidas Men's Galaxy 8 Running Shoes"></h2>
              <span class="a-price">
                <span class="a-offscreen">£29.25</span>
                <span aria-hidden="true">£29.25</span>
              </span>
              <span class="a-price a-text-price" data-a-strike="true">
                <span class="a-offscreen">null</span>
                <span aria-hidden="true">£45.00</span>
              </span>
              <img class="s-image" src="https://m.media-amazon.com/images/I/61K9CwkMMmL._AC_SL1500_.jpg"/>
            </div>
            """;
        var results = new SearchParser(UnitedKingdomMarketplace.Instance).Parse(html);
        var item = results.First(r => r.Asin == "B0FW2RGQDR");

        Assert.Equal("£29.25", item.Price);
        Assert.Equal("£45.00", item.OriginalPrice);
        Assert.Equal("-35%", item.Discount);
    }

    [Fact]
    public void Uk_format_and_paths_force_gbp()
    {
        Assert.Equal("£2,749.17", UnitedKingdomFormat.Instance.Price("2,749", "17"));
        Assert.Equal("£237.37", UnitedKingdomFormat.Instance.OriginalPrice("£237.37"));
        Assert.True(UnitedKingdomFormat.Instance.TryParsePrice("£2,749.17", out var amount));
        Assert.Equal(2749.17m, amount);

        Assert.Equal("https://www.amazon.co.uk/-/en-gb/", UnitedKingdomMarketplace.Instance.WarmUpUrl);
        Assert.Equal(
            "https://www.amazon.co.uk/s?k=asus%20pro%20art",
            UnitedKingdomMarketplace.Instance.SearchUrl("asus pro art"));
        Assert.Equal(
            "i18n-prefs=GBP; lc-acbuk=en_GB",
            UnitedKingdomMarketplace.Instance.DefaultCookieHeader);
        Assert.True(UnitedKingdomMarketplace.Instance.WarmUpEnabled);
    }
}
