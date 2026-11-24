using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Tests;

public class AmazonRetailSellerTests
{
    [Theory]
    [InlineData("it", "A11IL2PNWYJU7H")]
    [InlineData("com", "ATVPDKIKX0DER")]
    [InlineData("uk", "A3P5ROKL5A1OLE")]
    [InlineData("de", "A3JWKAKR8XB7XF")]
    [InlineData("fr", "A1X6FK5RDHNB96")]
    [InlineData("es", "A1AT7YVPFBWXBL")]
    [InlineData("jp", "AN1VRQENFRJN5")]
    public void Marketplace_exposes_its_amazon_retail_seller_id(
        string marketplaceId,
        string expected)
    {
        var marketplace = MarketplaceRegistry.Resolve(marketplaceId);

        Assert.Equal(expected, marketplace.AmazonSellerId);
        Assert.Equal(expected, AmazonRetailSeller.IdFor(marketplace.Id));
    }

    [Theory]
    [InlineData("Amazon", true)]
    [InlineData(" amazon ", true)]
    [InlineData("Amazon.it", true)]
    [InlineData("Amazon.co.uk", true)]
    [InlineData("Amazon Warehouse", false)]
    [InlineData("Amazon EU S.à r.l.", false)]
    [InlineData("x-kom", false)]
    [InlineData("", false)]
    public void Amazon_retail_name_matching_is_conservative(
        string sellerName,
        bool expected)
    {
        Assert.Equal(expected, AmazonRetailSeller.IsName(sellerName));
    }
}
