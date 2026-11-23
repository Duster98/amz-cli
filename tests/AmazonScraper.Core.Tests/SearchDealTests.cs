using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class SearchDealTests
{
    [Fact]
    public void Search_card_exposes_deal_badge_and_id()
    {
        const string html = """
            <div data-asin="B0HMWSZDTD">
              <h2><span>Cuffie Bluetooth</span></h2>
              <span class="a-price-whole">23<span class="a-price-fraction">99</span></span>
              <span data-csa-c-item-id="amzn1.asin.B0HMWSZDTD:amzn1.deal.86ef82ae"></span>
              <span data-a-badge-type="deal">
                <span class="a-badge-label">
                  <span class="a-badge-text">Offerta a tempo</span>
                </span>
              </span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Equal("Offerta a tempo", result.Deal);
        Assert.Equal("amzn1.deal.86ef82ae", result.DealId);
        var tsv = TsvFormatter.Search([result]);
        Assert.Equal(
            "ASIN\tTitle\tPrice\tRating\tReviews\tCoupon\tSponsored\tDeal\tDealId\tOriginalPrice\tDiscount\tChoice\tChoiceText\tChoiceFor",
            tsv.Split('\n')[0]);
        Assert.Contains(
            "\tOfferta a tempo\tamzn1.deal.86ef82ae\t",
            tsv.Split('\n')[1]);
        Assert.Contains("deal_id", JsonFormatter.Search([result]));
    }

    [Fact]
    public void Germany_fixture_parses_limited_time_deal()
    {
        var html = Fixtures.Load("de-search.html.gz");

        var result = new SearchParser(GermanyMarketplace.Instance)
            .Parse(html)
            .Single(item => item.Asin == "B0CTWVM6W5");

        Assert.Equal("Limited time deal", result.Deal);
        Assert.Equal("amzn1.deal.406d9b23", result.DealId);
    }

    [Fact]
    public void Search_card_with_deal_id_but_no_badge_uses_generic_deal()
    {
        const string html = """
            <div data-asin="B0BWSFGQ49">
              <h2><span>De'Longhi Magnifica S</span></h2>
              <span class="a-price-whole">239<span class="a-price-fraction">00</span></span>
              <span data-csa-c-item-id="amzn1.asin.B0BWSFGQ49:amzn1.deal.2f26f333"></span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(GermanyMarketplace.Instance).Parse(html));

        Assert.Equal("Deal", result.Deal);
        Assert.Equal("amzn1.deal.2f26f333", result.DealId);
    }

    [Fact]
    public void Search_card_without_deal_leaves_fields_null()
    {
        const string html = """
            <div data-asin="B000000001">
              <h2><span>Prodotto normale</span></h2>
              <span class="a-price-whole">10<span class="a-price-fraction">00</span></span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Null(result.Deal);
        Assert.Null(result.DealId);
        Assert.DoesNotContain("deal_id", JsonFormatter.Search([result]));
    }
}
