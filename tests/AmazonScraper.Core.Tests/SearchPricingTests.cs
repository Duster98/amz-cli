using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Formatting;

namespace AmazonScraper.Core.Tests;

public class SearchPricingTests
{
    [Fact]
    public void Search_card_computes_discount_from_struck_reference_price()
    {
        const string html = """
            <div data-asin="B0D7MLHCQ7">
              <h2><span>WD Blue SN5000 1TB</span></h2>
              <span class="a-price-whole">197<span class="a-price-fraction">45</span></span>
              <div class="a-section aok-inline-block">
                <span class="a-size-base a-color-secondary">Consigl.: </span>
                <span class="a-price a-text-price" data-a-size="b" data-a-strike="true" data-a-color="secondary">
                  <span class="a-offscreen">233,99 €</span>
                </span>
              </div>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Equal("233,99 €", result.OriginalPrice);
        Assert.Equal("-16%", result.Discount);
        Assert.Contains("\"original_price\": \"233,99 €\"", JsonFormatter.Search([result]));
        Assert.Contains("\"discount\": \"-16%\"", JsonFormatter.Search([result]));
    }

    [Fact]
    public void Search_card_without_reference_price_leaves_discount_empty()
    {
        const string html = """
            <div data-asin="B000000001">
              <h2><span>Prodotto normale</span></h2>
              <span class="a-price-whole">10<span class="a-price-fraction">00</span></span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Null(result.OriginalPrice);
        Assert.Null(result.Discount);
    }
}
