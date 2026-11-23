using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Tests;

public class MarketplaceRegistryTests
{
    [Theory]
    [InlineData("it")]
    [InlineData("IT")]
    [InlineData("amazon.it")]
    [InlineData("www.amazon.it")]
    [InlineData(".it")]
    public void Resolve_accepts_italian_aliases(string value)
    {
        var marketplace = MarketplaceRegistry.Resolve(value);
        Assert.Equal("it", marketplace.Id);
        Assert.Equal("https://www.amazon.it/dp/B0DTJDR3V9", marketplace.ProductUrl("B0DTJDR3V9"));
    }

    [Fact]
    public void Resolve_defaults_to_italy_when_empty()
    {
        Assert.Equal("it", MarketplaceRegistry.Resolve(null).Id);
        Assert.Equal("it", MarketplaceRegistry.Resolve("").Id);
    }

    [Fact]
    public void Resolve_com_returns_us_profile()
    {
        var marketplace = MarketplaceRegistry.Resolve("com");
        Assert.Equal("com", marketplace.Id);
        Assert.Equal("https://www.amazon.com/dp/B0H4SBSYBS", marketplace.ProductUrl("B0H4SBSYBS"));
        Assert.Equal("i18n-prefs=USD", marketplace.DefaultCookieHeader);
    }

    [Theory]
    [InlineData("us")]
    [InlineData("amazon.com")]
    [InlineData("www.amazon.com")]
    [InlineData(".com")]
    public void Resolve_accepts_us_aliases(string value)
    {
        Assert.Equal("com", MarketplaceRegistry.Resolve(value).Id);
    }

    [Fact]
    public void Resolve_france_returns_french_profile()
    {
        var marketplace = MarketplaceRegistry.Resolve("fr");
        Assert.Equal("fr", marketplace.Id);
        Assert.Equal("https://www.amazon.fr/dp/B0972BP9YR", marketplace.ProductUrl("B0972BP9YR"));
        Assert.Equal("https://www.amazon.fr/-/fr/", marketplace.WarmUpUrl);
        Assert.Equal("https://www.amazon.fr/-/fr/s?k=ram%2016%20gb", marketplace.SearchUrl("ram 16 gb"));
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("co.uk")]
    [InlineData("amazon.co.uk")]
    [InlineData("www.amazon.co.uk")]
    public void Resolve_uk_returns_british_profile(string value)
    {
        var marketplace = MarketplaceRegistry.Resolve(value);
        Assert.Equal("uk", marketplace.Id);
        Assert.Equal("https://www.amazon.co.uk/dp/B08JH7KQKV", marketplace.ProductUrl("B08JH7KQKV"));
        Assert.Equal("i18n-prefs=GBP; lc-acbuk=en_GB", marketplace.DefaultCookieHeader);
    }

    [Fact]
    public void Resolve_unknown_domain_lists_available_ids()
    {
        var exception = Assert.Throws<MarketplaceNotFoundException>(() => MarketplaceRegistry.Resolve("nl"));
        Assert.Contains("Unsupported domain", exception.Message);
        Assert.Contains("it", exception.Message);
        Assert.Contains("de", exception.Message);
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("co.jp")]
    [InlineData("amazon.co.jp")]
    [InlineData("www.amazon.co.jp")]
    public void Resolve_japan_returns_japanese_profile(string value)
    {
        var marketplace = MarketplaceRegistry.Resolve(value);
        Assert.Equal("jp", marketplace.Id);
        Assert.Equal("https://www.amazon.co.jp/dp/B0F5GWWKLP", marketplace.ProductUrl("B0F5GWWKLP"));
        Assert.Equal("i18n-prefs=JPY", marketplace.DefaultCookieHeader);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("spain")]
    [InlineData("amazon.es")]
    [InlineData("www.amazon.es")]
    public void Resolve_spain_returns_spanish_profile(string value)
    {
        var marketplace = MarketplaceRegistry.Resolve(value);
        Assert.Equal("es", marketplace.Id);
        Assert.Equal("https://www.amazon.es/dp/B0DTJFZ4YS", marketplace.ProductUrl("B0DTJFZ4YS"));
        Assert.Equal("i18n-prefs=EUR", marketplace.DefaultCookieHeader);
    }

    [Theory]
    [InlineData("it", "https://www.amazon.it/s?k=asus", "https://www.amazon.it/s?k=asus&page=2")]
    [InlineData("de", "https://www.amazon.de/s?k=asus", "https://www.amazon.de/s?k=asus&page=2")]
    [InlineData("fr", "https://www.amazon.fr/-/fr/s?k=asus", "https://www.amazon.fr/-/fr/s?k=asus&page=2")]
    [InlineData("uk", "https://www.amazon.co.uk/s?k=asus", "https://www.amazon.co.uk/s?k=asus&page=2")]
    [InlineData("com", "https://www.amazon.com/-/en/s?k=asus", "https://www.amazon.com/-/en/s?k=asus&page=2")]
    [InlineData("jp", "https://www.amazon.co.jp/-/ja/s?k=asus", "https://www.amazon.co.jp/-/ja/s?k=asus&page=2")]
    [InlineData("es", "https://www.amazon.es/-/es/s?k=asus", "https://www.amazon.es/-/es/s?k=asus&page=2")]
    public void Search_url_keeps_page_one_and_adds_page_parameter(string domain, string pageOne, string pageTwo)
    {
        var marketplace = MarketplaceRegistry.Resolve(domain);

        Assert.Equal(pageOne, marketplace.SearchUrl("asus"));
        Assert.Equal(pageTwo, marketplace.SearchUrl("asus", 2));
    }

    [Fact]
    public void Search_url_appends_the_deal_filter()
    {
        var marketplace = MarketplaceRegistry.Resolve("it");

        Assert.Equal(
            "https://www.amazon.it/s?k=asus&rh=p_n_deal_type%3A26901109031",
            marketplace.SearchUrl("asus", 1, "26901109031"));
        Assert.Equal(
            "https://www.amazon.it/s?k=asus&page=2&rh=p_n_deal_type%3A26901109031",
            marketplace.SearchUrl("asus", 2, "26901109031"));
    }

    [Fact]
    public void Deals_url_uses_the_filter_without_a_query()
    {
        var marketplace = MarketplaceRegistry.Resolve("uk");

        Assert.Equal(
            "https://www.amazon.co.uk/s?rh=p_n_deal_type%3A26901100031",
            MarketplaceUrlBuilder.Deals(marketplace, "26901100031"));
        Assert.Equal(
            "https://www.amazon.co.uk/s?rh=p_n_deal_type%3A26901100031&page=2",
            MarketplaceUrlBuilder.Deals(marketplace, "26901100031", 2));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("germany")]
    [InlineData("amazon.de")]
    [InlineData("www.amazon.de")]
    [InlineData(".de")]
    public void Resolve_germany_returns_german_profile(string value)
    {
        var marketplace = MarketplaceRegistry.Resolve(value);
        Assert.Equal("de", marketplace.Id);
        Assert.Equal("https://www.amazon.de/dp/B0FX39Q2TN", marketplace.ProductUrl("B0FX39Q2TN"));
        Assert.Contains("i18n-prefs=EUR", marketplace.DefaultCookieHeader);
    }

    [Fact]
    public void Domains_include_italy_germany_france_uk_us_japan_and_spain_as_available()
    {
        Assert.Equal(7, MarketplaceRegistry.All.Count(m => m.Available));
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "it" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "de" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "fr" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "uk" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "com" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "jp" && m.Available);
        Assert.Contains(MarketplaceRegistry.All, m => m.Id == "es" && m.Available);
    }
}
