using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class SearchSettingsTests
{
    [Fact]
    public void No_ads_is_valid_for_a_search()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            NoAds = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Deals_filter_is_valid_for_a_network_search()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            Deals = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("today")]
    [InlineData("coupons")]
    public void Deal_type_is_valid_with_deals(string dealType)
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            Deals = true,
            DealType = dealType,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Deal_type_requires_deals()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            DealType = "today",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Invalid_deal_type_is_rejected()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            Deals = true,
            DealType = "lightning",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Deals_is_incompatible_with_local_html()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            Deals = true,
            HtmlFile = "/tmp/search.html",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Deals_works_with_pagination()
    {
        var settings = new SearchSettings
        {
            Query = "cuffie",
            Deals = true,
            Pages = 3,
        };

        Assert.True(settings.Validate().Successful);
    }
}
