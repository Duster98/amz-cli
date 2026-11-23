using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class BuyingOptionsParserTests
{
    [Fact]
    public void Parses_aod_offers_with_codes_seller_shipping_and_delivery()
    {
        var html = Fixtures.Load("aod-it-B07R3YS8R2.html.gz");

        var result = BuyingOptionsParser.Parse(
            html,
            ItalyMarketplace.Instance,
            "B07R3YS8R2");

        Assert.NotNull(result);
        Assert.Equal(7, result!.Count);
        Assert.Equal(7, result.Options.Count);
        Assert.Equal("87,25 €", result.LowestPrice);

        var first = result.Options[0];
        Assert.Equal("TEST+OFFER+1/SANITIZED", first.OfferListingId);
        Assert.Equal("Furan", first.Seller);
        Assert.Equal("A131AJORA55M3D", first.SellerId);
        Assert.Equal("87,25 €", first.Price);
        Assert.Equal("GRATUITA", first.Shipping);
        Assert.Equal("Nuovo", first.Condition);
        Assert.Equal("new", first.ConditionType);
        Assert.Equal("", first.ConditionGrade);
        Assert.Equal("21 - 29 dicembre", first.Delivery);
        Assert.Equal(
            "https://www.amazon.it/dp/B07R3YS8R2?smid=A131AJORA55M3D&psc=1",
            first.DetailUrl);

        var second = result.Options[1];
        Assert.Equal("TEST+OFFER+2/SANITIZED", second.OfferListingId);
        Assert.Equal("The Idea for Life", second.Seller);
        Assert.Equal("AJJP4LFEFPII7", second.SellerId);
        Assert.Equal("96,59 €", second.Price);
        Assert.Equal("a 15€", second.Shipping);
        Assert.Equal("29 dicembre - 5 gennaio", second.Delivery);
    }

    [Fact]
    public void Parses_mobile_table_aod_with_pinned_featured_offer()
    {
        var html = Fixtures.Load("aod-jp-mobile-B0F54D861F.html.gz");

        var result = BuyingOptionsParser.Parse(
            html,
            JapanMarketplace.Instance,
            "B0F54D861F");

        Assert.NotNull(result);
        Assert.Equal(50, result!.Count);
        Assert.Equal(11, result.Options.Count);
        Assert.Equal("￥5,800", result.LowestPrice);

        var pinned = result.Options[0];
        Assert.Equal("TEST+OFFER+1", pinned.OfferListingId);
        Assert.Equal("グルグル", pinned.Seller);
        Assert.Equal("APME61CR1EQOJ", pinned.SellerId);
        Assert.Equal("￥8,300", pinned.Price);
        Assert.Equal("無料", pinned.Shipping);
        Assert.Equal("新品", pinned.Condition);
        Assert.Equal("new", pinned.ConditionType);
        Assert.Equal("", pinned.ConditionGrade);
        Assert.Equal("11月17日 火曜日にお届け", pinned.Delivery);
        Assert.Equal(
            "https://www.amazon.co.jp/dp/B0F54D861F?smid=APME61CR1EQOJ&psc=1",
            pinned.DetailUrl);

        var firstList = result.Options[1];
        Assert.Equal("TEST+OFFER+2", firstList.OfferListingId);
        Assert.Equal("浪漫遊 各務原店", firstList.Seller);
        Assert.Equal("A14F3Y9XMICQQV", firstList.SellerId);
        Assert.Equal("￥6,100", firstList.Price);
        Assert.Equal("無料", firstList.Shipping);
        Assert.Equal("中古商品 - 良い", firstList.Condition);
        Assert.Equal("used", firstList.ConditionType);
        Assert.Equal("good", firstList.ConditionGrade);
        Assert.Equal("11月18日 水曜日にお届け", firstList.Delivery);
    }

    [Fact]
    public void Parses_aod_page_two_fragment()
    {
        var html = Fixtures.Load("aod-fr-page2-B08JH7KQKV.html.gz");

        var result = BuyingOptionsParser.Parse(
            html,
            FranceMarketplace.Instance,
            "B08JH7KQKV");

        Assert.NotNull(result);
        Assert.Equal(10, result!.Count);
        Assert.Equal(10, result.Options.Count);

        var first = result.Options[0];
        Assert.Equal("TEST+OFFER+11", first.OfferListingId);
        Assert.Equal("Realphone", first.Seller);
        Assert.Equal("A9B1VBHEHY161", first.SellerId);
        Assert.Equal("536,00 €", first.Price);
        Assert.Equal("à 48,61 €", first.Shipping);
        Assert.Equal("Neuf", first.Condition);
        Assert.Equal("new", first.ConditionType);
        Assert.Equal("", first.ConditionGrade);
        Assert.Equal("29 - 30 décembre", first.Delivery);
        Assert.Equal(
            "https://www.amazon.fr/dp/B08JH7KQKV?smid=A9B1VBHEHY161&psc=1",
            first.DetailUrl);
    }

    [Fact]
    public void Reports_count_from_aod_hidden_input()
    {
        const string html = """
            <input type="hidden" name="" value="3" id="aod-total-offer-count"/>
            <input type="hidden" name="" value="3 opzioni" id="aod-total-offer-count-string"/>
            """;

        var result = BuyingOptionsParser.Parse(
            html,
            ItalyMarketplace.Instance,
            "B07R3YS8R2");

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Returns_null_when_the_fragment_has_no_offers()
    {
        Assert.Null(BuyingOptionsParser.Parse(
            "<html><body>Nessuna offerta</body></html>",
            ItalyMarketplace.Instance,
            "B07R3YS8R2"));
    }

    [Fact]
    public void Parses_renewed_condition_grade()
    {
        const string html = """
            <input type="hidden" name="" value="1" id="aod-total-offer-count"/>
            <div id="aod-offer" role="listitem">
              <div id="aod-offer-heading"><span>Ricondizionato: premium</span></div>
              <input type="hidden" name="items[0.base][offerListingId]" value="TEST%2BOFFER"/>
              <div id="aod-offer-price">
                <span data-pricetopay-label="{priceToPay}" class="aok-offscreen apex-pricetopay-accessibility-label">849,00 €</span>
              </div>
            </div>
            """;

        var result = BuyingOptionsParser.Parse(
            html,
            ItalyMarketplace.Instance,
            "B0FRYG5R77");

        Assert.NotNull(result);
        var option = Assert.Single(result!.Options);
        Assert.Equal("Ricondizionato: premium", option.Condition);
        Assert.Equal("refurbished", option.ConditionType);
        Assert.Equal("premium", option.ConditionGrade);
    }

    [Fact]
    public void Detail_url_without_seller_id_is_the_product_url()
    {
        const string html = """
            <input type="hidden" name="" value="1" id="aod-total-offer-count"/>
            <div id="aod-offer" role="listitem">
              <div id="aod-offer-heading"><span>Nuovo</span></div>
              <input type="hidden" name="items[0.base][offerListingId]" value="TEST%2BOFFER"/>
              <div id="aod-offer-price">
                <span data-pricetopay-label="{priceToPay}" class="aok-offscreen apex-pricetopay-accessibility-label">19,99 €</span>
              </div>
            </div>
            """;

        var result = BuyingOptionsParser.Parse(
            html,
            ItalyMarketplace.Instance,
            "B07R3YS8R2");

        Assert.NotNull(result);
        var option = Assert.Single(result!.Options);
        Assert.Equal("TEST+OFFER", option.OfferListingId);
        Assert.Equal("https://www.amazon.it/dp/B07R3YS8R2", option.DetailUrl);
    }
}
