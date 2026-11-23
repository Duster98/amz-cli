using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class BuyingOptionsSummaryParserTests
{
    [Fact]
    public void Parses_mobile_unqualified_summary()
    {
        const string html = """
            <span class="a-declarative" data-action="show-all-offers-display">
              <span class="a-button a-spacing-base a-button-base"><span class="a-button-inner">
                <a href="/gp/offer-listing/B07R3YS8R2/ref=dp_olp_NEW_mbc?ie=UTF8&amp;condition=NEW"
                   data-csa-c-content-id="unqualified-aodingress-summary-enabled"
                   class="a-button-text a-text-center">
                  <span>7 opzioni a partire da </span>
                  <span class="a-price" data-a-size="base_plus" data-a-color="base">
                    <span class="a-offscreen">87,25€</span>
                    <span aria-hidden="true">
                      <span class="a-price-whole">87<span class="a-price-decimal">,</span></span>
                      <span class="a-price-fraction">25</span>
                      <span class="a-price-symbol">€</span>
                    </span>
                  </span>
                </a>
              </span></span>
            </span>
            """;

        var summary = BuyingOptionsSummaryParser.Parse(html, ItalyMarketplace.Instance);

        Assert.NotNull(summary);
        Assert.Equal(7, summary!.Count);
        Assert.Equal("87,25 €", summary.LowestPrice);
    }

    [Fact]
    public void Parses_desktop_new_count_summary()
    {
        const string html = """
            <a id="aod-ingress-link" aria-expanded="false" class="a-link-normal"
               href="/gp/offer-listing/B07R3YS8R2/ref=dp_olp_NEW_mbc?ie=UTF8&amp;condition=NEW"
               role="button">
              <span class="a-color-base">Nuovo (7) da</span>
              <span class="a-color-base"> </span>
              <span class="a-price" data-a-size="base_plus" data-a-color="base">
                <span class="a-offscreen">87,25€</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">87<span class="a-price-decimal">,</span></span>
                  <span class="a-price-fraction">25</span>
                  <span class="a-price-symbol">€</span>
                </span>
              </span>
            </a>
            """;

        var summary = BuyingOptionsSummaryParser.Parse(html, ItalyMarketplace.Instance);

        Assert.NotNull(summary);
        Assert.Equal(7, summary!.Count);
        Assert.Equal("87,25 €", summary.LowestPrice);
        Assert.Equal("Nuovo", summary.Condition);
    }

    [Fact]
    public void Parses_japanese_whole_yen_price()
    {
        const string html = """
            <a id="aod-ingress-link" href="/gp/offer-listing/B000000000/">
              <span>新品 (3) から</span>
              <span class="a-price">
                <span class="a-offscreen">￥2,399</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">2,399</span>
                  <span class="a-price-symbol">￥</span>
                </span>
              </span>
            </a>
            """;

        var summary = BuyingOptionsSummaryParser.Parse(html, JapanMarketplace.Instance);

        Assert.NotNull(summary);
        Assert.Equal(3, summary!.Count);
        Assert.Equal("￥2,399", summary.LowestPrice);
        Assert.Equal("新品", summary.Condition);
    }

    [Fact]
    public void Parses_mobile_touch_link_summary()
    {
        const string html = """
            <a href="/gp/offer-listing/B0F54D861F/ref=dp_olp_ALL_mbc?ie=UTF8&amp;condition=ALL"
               class="a-touch-link a-box a-touch-link-noborder olp-touch-link">
              <div class="a-box-inner">
                <div class="a-section a-spacing-small">
                  <span class="a-size-base a-color-base"> 新品&中古(50)点を比較する </span>
                </div>
                <div class="a-section a-spacing-mini">
                  <span class="a-price">
                    <span class="a-offscreen">￥6,100</span>
                    <span aria-hidden="true">
                      <span class="a-price-symbol">￥</span>
                      <span class="a-price-whole">6,100</span>
                    </span>
                  </span>
                </div>
              </div>
            </a>
            """;

        var summary = BuyingOptionsSummaryParser.Parse(html, JapanMarketplace.Instance);

        Assert.NotNull(summary);
        Assert.Equal(50, summary!.Count);
        Assert.Equal("￥6,100", summary.LowestPrice);
        Assert.Equal("新品&中古", summary.Condition);
    }

    [Fact]
    public void Parses_a_used_only_buybox()
    {
        const string html = """
            <div id="usedOnlyBuybox">
              <div id="usedBuySection">
                <span class="a-text-bold">Acheter D'occasion :</span>
                <span class="a-price">
                  <span class="a-offscreen">480,00 €</span>
                  <span aria-hidden="true">
                    <span class="a-price-whole">480<span class="a-price-decimal">,</span></span>
                    <span class="a-price-fraction">00</span>
                    <span class="a-price-symbol">€</span>
                  </span>
                </span>
              </div>
              <div id="usedbuyBox">
                <input type="hidden" id="usedMerchantID" value="A2BJX04VEIC906"/>
                <input type="hidden" id="usedOfferListingID" value="abc"/>
                <strong>D&#039;occasion: Très bon</strong>
              </div>
            </div>
            """;

        var summary = BuyingOptionsSummaryParser.Parse(
            html,
            FranceMarketplace.Instance);

        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Count);
        Assert.Equal("480,00 €", summary.LowestPrice);
        Assert.Equal("D'occasion: Très bon", summary.Condition);
    }

    [Fact]
    public void Returns_null_without_an_offers_anchor()
    {
        Assert.Null(BuyingOptionsSummaryParser.Parse(
            "<div id=\"buybox\">Solo acquisto</div>",
            ItalyMarketplace.Instance));
    }
}
