using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailPriceTests
{
    [Fact]
    public void Italy_no_featured_offer_does_not_use_carousel_price()
    {
        const string html = """
            <div id="corePrice_desktop"></div>
            <div id="apex_desktop"></div>
            <div class="a-carousel-card" data-asin="B08XWBS3WM">
              <span class="a-price">
                <span class="a-offscreen">47,00 €</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">47</span>
                  <span class="a-price-fraction">00</span>
                </span>
              </span>
            </div>
            <div id="buybox">
              <span id="buybox-see-all-buying-choices">
                Visualizza tutte le opzioni di acquisto
              </span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B07R3YS8R2");

        Assert.Empty(detail.Price);
        Assert.Empty(detail.OriginalPrice);
    }

    [Fact]
    public void Italy_no_featured_offer_exposes_buying_options_summary()
    {
        const string html = """
            <div id="corePrice_mobile_feature_div"></div>
            <span class="a-declarative" data-action="show-all-offers-display">
              <span class="a-button a-spacing-base a-button-base"><span class="a-button-inner">
                <a href="/gp/offer-listing/B07R3YS8R2/ref=dp_olp_NEW_mbc?ie=UTF8&amp;condition=NEW"
                   data-csa-c-content-id="unqualified-aodingress-summary-enabled"
                   class="a-button-text a-text-center">
                  <span>7 opzioni a partire da </span>
                  <span class="a-price">
                    <span class="a-offscreen">87,25€</span>
                    <span aria-hidden="true">
                      <span class="a-price-whole">87<span class="a-price-decimal">,</span></span>
                      <span class="a-price-fraction">25</span>
                    </span>
                  </span>
                </a>
              </span></span>
            </span>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B07R3YS8R2");

        Assert.Empty(detail.Price);
        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(7, detail.BuyingOptions!.Count);
        Assert.Equal("87,25 €", detail.BuyingOptions.LowestPrice);
        Assert.Empty(detail.BuyingOptions.Options);
    }

    [Fact]
    public void Italy_featured_offer_keeps_price_and_reports_options()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-offscreen">99,00 €</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">99</span>
                  <span class="a-price-fraction">00</span>
                </span>
              </span>
            </div>
            <a id="aod-ingress-link" href="/gp/offer-listing/B0TEST0000/">
              <span>Nuovo (7) da</span>
              <span class="a-price">
                <span class="a-offscreen">87,25€</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">87</span>
                  <span class="a-price-fraction">25</span>
                </span>
              </span>
            </a>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0TEST0000");

        Assert.Equal("99,00 €", detail.Price);
        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(7, detail.BuyingOptions!.Count);
        Assert.Equal("87,25 €", detail.BuyingOptions.LowestPrice);
    }

    [Fact]
    public void Italy_main_price_block_still_wins()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-offscreen">19,99 €</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">19</span>
                  <span class="a-price-fraction">99</span>
                </span>
              </span>
            </div>
            <div class="a-carousel-card" data-asin="B08XWBS3WM">
              <span class="a-price">
                <span class="a-price-whole">47</span>
                <span class="a-price-fraction">00</span>
              </span>
            </div>
            <div id="buybox">
              <span id="buybox-see-all-buying-choices">Visualizza tutte le opzioni</span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B07R3YS8R2");

        Assert.Equal("19,99 €", detail.Price);
    }
}
