using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Tests;

public class PriceFormatTests
{
    [Theory]
    [InlineData("1.234,56 €", "1234.56")]
    [InlineData("1.250,50 €", "1250.50")]
    [InlineData("24,99 €", "24.99")]
    public void Italian_price_parses(string display, string expected)
    {
        Assert.True(ItalianMarketplaceFormat.Instance.TryParsePrice(display, out var amount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Fact]
    public void Italian_price_formats_whole_and_fraction()
    {
        Assert.Equal("1.250,50 €", ItalianMarketplaceFormat.Instance.Price("1.250", "50"));
        Assert.Equal("1.250,50 €", ItalianMarketplaceFormat.Instance.SinglePrice("1.250,50"));
    }

    [Fact]
    public void Italian_rating_uses_comma()
    {
        Assert.Equal("4,7 / 5", ItalianMarketplaceFormat.Instance.Rating("4.7"));
        Assert.Equal("4,7 / 5", ItalianMarketplaceFormat.Instance.Rating("4,7"));
    }

    [Fact]
    public void Price_parser_returns_null_for_empty()
    {
        Assert.Null(PriceParser.TryParse(string.Empty, ItalianMarketplaceFormat.Instance));
    }
}
