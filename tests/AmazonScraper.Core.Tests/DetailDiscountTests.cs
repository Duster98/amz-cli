using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailDiscountTests
{
    [Fact]
    public void Discount_with_a_space_before_the_percent_is_parsed()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">24</span>
                <span class="a-price-fraction">99</span>
              </span>
              <span class="savingsPercentage">-72 %</span>
              <span data-a-strike="true"><span class="a-offscreen">89,99 €</span></span>
            </div>
            """;

        var detail = new DetailParser(FranceMarketplace.Instance).Parse(html, "B0HN7CZHSD");

        Assert.Equal("24,99 €", detail.Price);
        Assert.Equal("89,99 €", detail.OriginalPrice);
        Assert.Equal("-72%", detail.Discount);
    }

    [Fact]
    public void Discount_is_computed_when_the_page_has_no_percentage()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">24</span>
                <span class="a-price-fraction">99</span>
              </span>
              <span data-a-strike="true"><span class="a-offscreen">89,99 €</span></span>
            </div>
            """;

        var detail = new DetailParser(FranceMarketplace.Instance).Parse(html, "B0HN7CZHSD");

        Assert.Equal("-72%", detail.Discount);
    }

    [Fact]
    public void Spanish_coupon_percentage_overrides_an_internal_numeric_code()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">16</span>
                <span class="a-price-fraction">91</span>
              </span>
              <span data-a-strike="true"><span class="a-offscreen">19,90 €</span></span>
            </div>
            <p id="pqv-price-coupon-message">Aplicar cup&oacute;n de 30&nbsp;% en la primera entrega.</p>
            <span class="newCouponBadge"><i></i>9900</span>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance).Parse(html, "B07M7L3J7Y");

        Assert.Equal("-30%", detail.Coupon);
        Assert.Equal("-15%", detail.Discount);
    }

    [Fact]
    public void Coupon_word_in_description_or_css_is_ignored()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">10</span>
                <span class="a-price-fraction">00</span>
              </span>
            </div>
            <div id="couponsInBuybox_feature_div"></div>
            <div class="description">The microphones block 80% of background noise.</div>
            <style>.newCouponBadge { color: #ff9900; font-size: 80%; }</style>
            """;

        var detail = new DetailParser(FranceMarketplace.Instance).Parse(html, "B0HN7CZHSD");

        Assert.Empty(detail.Coupon);
    }

    [Fact]
    public void Deal_metadata_and_countdown_are_exposed()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">23</span>
                <span class="a-price-fraction">99</span>
              </span>
            </div>
            <div id="dealBadge_feature_div">
              <span class="dealBadge" data-csa-c-item-id="amzn1.asin.B0HMWSZDTD:amzn1.deal.86ef82ae">
                <span id="deals_countdown_timer_from_hours_screen_reader_label">
                  Offerta a tempo NO_OF_HOURS ore NO_OF_MINUTES minuti
                </span>
                <span id="dealBadgeSupportingText" class="a-size-small a-text-bold">
                  <span>Offerta a tempo</span>
                </span>
              </span>
              <span data-a-popover="{&quot;name&quot;:&quot;dealTermsMessage&quot;,&quot;header&quot;:&quot;Questa offerta termina il 18 novembre 2026&quot;}"></span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0HMWSZDTD");

        Assert.Equal("Offerta a tempo", detail.Deal);
        Assert.Equal("amzn1.deal.86ef82ae", detail.DealId);
        Assert.Equal(
            "Questa offerta termina il 18 novembre 2026",
            detail.DealEndsText);
        Assert.Equal("2026-11-18", detail.DealEndsOn);
        Assert.True(detail.DealHasCountdown);
        var expectedDays = Math.Max(
            0,
            new DateOnly(2026, 11, 18).DayNumber
            - DateOnly.FromDateTime(DateTime.Now).DayNumber);
        Assert.Equal(expectedDays, detail.DealEndsInDays);

        var tsv = TsvFormatter.Detail(detail);
        Assert.Contains("DealId\tamzn1.deal.86ef82ae", tsv);
        Assert.Contains(
            "DealEnds\tQuesta offerta termina il 18 novembre 2026",
            tsv);
        Assert.Contains("DealEndsOn\t2026-11-18", tsv);
        Assert.Contains($"DealEndsInDays\t{expectedDays}", tsv);
        Assert.Contains("DealCountdown\ttrue", tsv);

        var json = JsonFormatter.Detail(detail);
        Assert.Contains("\"deal_id\": \"amzn1.deal.86ef82ae\"", json);
        Assert.Contains("\"deal_ends_on\": \"2026-11-18\"", json);
        Assert.Contains("\"deal_has_countdown\": true", json);
    }

    [Fact]
    public void Deal_without_id_or_end_date_keeps_only_the_badge()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">16</span>
                <span class="a-price-fraction">91</span>
              </span>
            </div>
            <div id="dealBadge_feature_div">
              <span class="dealBadge" data-csa-c-painter="dp-deal">
                <span id="dealBadgeSupportingText" class="a-size-small a-text-bold">
                  <span>Black Friday anticipado</span>
                </span>
              </span>
            </div>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance)
            .Parse(html, "B07M7L3J7Y");

        Assert.Equal("Black Friday anticipado", detail.Deal);
        Assert.Null(detail.DealId);
        Assert.Null(detail.DealEndsText);
        Assert.Null(detail.DealEndsOn);
        Assert.Null(detail.DealEndsInDays);
        Assert.False(detail.DealHasCountdown);
    }

    [Fact]
    public void Deal_badge_text_is_exposed()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">16</span>
                <span class="a-price-fraction">91</span>
              </span>
            </div>
            <div id="dealBadge_feature_div">
              <span class="dealBadge" data-csa-c-painter="dp-deal">
                <span id="dealBadgeSupportingText" class="a-size-small a-text-bold">
                  <span>Black Friday anticipado</span>
                </span>
              </span>
            </div>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance).Parse(html, "B07M7L3J7Y");

        Assert.Equal("Black Friday anticipado", detail.Deal);
    }

    [Fact]
    public void No_deal_badge_leaves_deal_null()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price">
                <span class="a-price-whole">10</span>
                <span class="a-price-fraction">00</span>
              </span>
            </div>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance).Parse(html, "B07M7L3J7Y");

        Assert.Null(detail.Deal);
        Assert.Null(detail.DealId);
        Assert.Null(detail.DealEndsText);
        Assert.Null(detail.DealEndsOn);
        Assert.Null(detail.DealHasCountdown);
    }
}
