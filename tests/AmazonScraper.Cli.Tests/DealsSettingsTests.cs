using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class DealsSettingsTests
{
    [Fact]
    public void Defaults_to_all_deals()
    {
        var settings = new DealsSettings();

        Assert.True(settings.Validate().Successful);
        Assert.Equal("all", settings.DealType);
        Assert.Null(settings.Limit);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("today")]
    [InlineData("coupons")]
    public void Accepts_supported_deal_types(string dealType)
    {
        var settings = new DealsSettings { DealType = dealType };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Rejects_invalid_deal_type()
    {
        var settings = new DealsSettings { DealType = "lightning" };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void No_ads_is_valid()
    {
        var settings = new DealsSettings { NoAds = true };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Rejects_page_and_pages_together()
    {
        var settings = new DealsSettings { Page = "2", Pages = 2 };

        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Rejects_limit_outside_bounds(int limit)
    {
        var settings = new DealsSettings { Limit = limit };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Accepts_valid_limit()
    {
        var settings = new DealsSettings { Limit = 20 };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Local_html_supports_only_one_page()
    {
        var settings = new DealsSettings
        {
            HtmlFile = "/tmp/deals.html",
            Pages = 2,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Category_is_valid_for_a_network_run()
    {
        var settings = new DealsSettings { Category = "Elettronica" };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Category_requires_network()
    {
        var settings = new DealsSettings
        {
            Category = "Elettronica",
            HtmlFile = "/tmp/deals.html",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Category_cannot_be_empty()
    {
        var settings = new DealsSettings { Category = " " };

        Assert.False(settings.Validate().Successful);
    }
}
