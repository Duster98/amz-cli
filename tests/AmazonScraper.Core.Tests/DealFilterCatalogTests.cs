using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class DealFilterCatalogTests
{
    [Theory]
    [InlineData("it", "26901109031")]
    [InlineData("de", "26902994031")]
    [InlineData("fr", "26902977031")]
    [InlineData("es", "26902953031")]
    [InlineData("uk", "26901100031")]
    [InlineData("com", "23566065011")]
    [InlineData("jp", "10343616051")]
    public void All_discounts_filter_exists_for_every_marketplace(
        string domain,
        string expected)
    {
        Assert.True(DealFilterCatalog.TryGet(
            domain,
            DealFilterKind.All,
            out var filterId));
        Assert.Equal(expected, filterId);
    }

    [Theory]
    [InlineData("it", "26901107031")]
    [InlineData("de", "26902993031")]
    [InlineData("fr", "26902975031")]
    [InlineData("uk", "26901098031")]
    [InlineData("com", "23566064011")]
    [InlineData("jp", "10343614051")]
    public void Today_filter_is_mapped_where_amazon_exposes_it(
        string domain,
        string expected)
    {
        Assert.True(DealFilterCatalog.TryGet(
            domain,
            DealFilterKind.Today,
            out var filterId));
        Assert.Equal(expected, filterId);
    }

    [Fact]
    public void Today_filter_is_not_available_on_spain()
    {
        Assert.False(DealFilterCatalog.TryGet(
            "es",
            DealFilterKind.Today,
            out _));
    }

    [Theory]
    [InlineData("it", "210770022031")]
    [InlineData("de", "210770231031")]
    [InlineData("fr", "210770227031")]
    public void Coupon_filter_is_mapped_where_verified(
        string domain,
        string expected)
    {
        Assert.True(DealFilterCatalog.TryGet(
            domain,
            DealFilterKind.Coupons,
            out var filterId));
        Assert.Equal(expected, filterId);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("uk")]
    [InlineData("com")]
    [InlineData("jp")]
    public void Coupon_filter_is_not_claimed_for_unverified_markets(
        string domain)
    {
        Assert.False(DealFilterCatalog.TryGet(
            domain,
            DealFilterKind.Coupons,
            out _));
    }

    [Theory]
    [InlineData(null, DealFilterKind.All)]
    [InlineData("", DealFilterKind.All)]
    [InlineData("all", DealFilterKind.All)]
    [InlineData("ALL-DISCOUNTS", DealFilterKind.All)]
    [InlineData("today", DealFilterKind.Today)]
    [InlineData("today-deals", DealFilterKind.Today)]
    [InlineData("coupon", DealFilterKind.Coupons)]
    [InlineData("COUPONS", DealFilterKind.Coupons)]
    public void Deal_type_aliases_are_parsed(
        string? value,
        DealFilterKind expected)
    {
        Assert.True(DealFilterCatalog.TryParseKind(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("lightning")]
    [InlineData("invalid")]
    public void Unknown_deal_type_is_rejected(string value)
    {
        Assert.False(DealFilterCatalog.TryParseKind(value, out _));
    }
}
