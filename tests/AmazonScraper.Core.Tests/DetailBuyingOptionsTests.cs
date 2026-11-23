using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailBuyingOptionsTests
{
    [Fact]
    public void Featured_offer_is_read_from_the_product_page()
    {
        var html = Fixtures.Load("detail2.html.gz");

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0FMRWBK4R");

        Assert.Equal("24,99 €", detail.Price);
        Assert.Equal("Yaqiusi", detail.Seller);
        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(2, detail.BuyingOptions!.Count);
        Assert.Equal("24,99 €", detail.BuyingOptions.LowestPrice);
        Assert.Equal("Nuovo", detail.BuyingOptions.Condition);

        var featured = Assert.Single(detail.BuyingOptions.Options);
        Assert.StartsWith("YljwZENTu", featured.OfferListingId);
        Assert.DoesNotContain("%", featured.OfferListingId);
        Assert.Equal("Yaqiusi", featured.Seller);
        Assert.Equal("AB4K1MTYXAXDZ", featured.SellerId);
        Assert.Equal("24,99 €", featured.Price);
        Assert.Equal("GRATUITA", featured.Shipping);
        Assert.Equal("Nuovo", featured.Condition);
        Assert.Equal("sabato, 14 novembre", featured.Delivery);
        Assert.Equal(
            "https://www.amazon.it/dp/B0FMRWBK4R?smid=AB4K1MTYXAXDZ&psc=1",
            featured.DetailUrl);
    }

    [Fact]
    public void Featured_offer_is_read_for_a_large_offer_set()
    {
        var html = Fixtures.Load("detail.html.gz");

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0DTJDR3V9");

        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(34, detail.BuyingOptions!.Count);
        var featured = Assert.Single(detail.BuyingOptions.Options);
        Assert.StartsWith("9rUKG+LW", featured.OfferListingId);
        Assert.Equal("A11IL2PNWYJU7H", featured.SellerId);
        Assert.Equal("1.250,50 €", featured.Price);
        Assert.Equal("Nuovo", featured.Condition);
    }

    [Fact]
    public void Empty_featured_offer_inputs_do_not_create_an_option()
    {
        const string html = """
            <div id="corePrice_mobile_feature_div"></div>
            <input type="hidden" id="offerListingID" name="offerListingID" value="">
            <input type="hidden" id="merchantID" name="merchantID" value="">
            <a data-csa-c-content-id="unqualified-aodingress-summary-enabled" href="/gp/offer-listing/B07R3YS8R2/">
              <span>7 opzioni a partire da </span>
              <span class="a-price">
                <span class="a-offscreen">87,25€</span>
                <span aria-hidden="true">
                  <span class="a-price-whole">87</span>
                  <span class="a-price-fraction">25</span>
                </span>
              </span>
            </a>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B07R3YS8R2");

        Assert.Empty(detail.Price);
        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(7, detail.BuyingOptions!.Count);
        Assert.Empty(detail.BuyingOptions.Options);
    }

    [Fact]
    public void Combine_keeps_featured_offer_and_other_sellers()
    {
        var featured = new BuyingOption
        {
            OfferListingId = "FEATURED",
            Seller = "Yaqiusi",
            SellerId = "AB4K1MTYXAXDZ",
            Price = "24,99 €",
            Condition = "Nuovo",
        };
        var other = new BuyingOption
        {
            OfferListingId = "OTHER",
            Seller = "Juyutuo",
            SellerId = "A37WRS68D84O4A",
            Price = "34,72 €",
            Condition = "Nuovo",
        };

        var productPage = new BuyingOptionsSummary
        {
            Count = 2,
            LowestPrice = "24,99 €",
            Condition = "Nuovo",
            Options = [featured],
        };
        var aod = new BuyingOptionsSummary
        {
            Count = 2,
            LowestPrice = "34,72 €",
            Options = [other],
        };

        var combined = BuyingOptionsSummary.Combine(productPage, aod);

        Assert.Equal(2, combined.Count);
        Assert.Equal("24,99 €", combined.LowestPrice);
        Assert.Equal(["FEATURED", "OTHER"], combined.Options.Select(option => option.OfferListingId));
        Assert.Equal("Nuovo", combined.Condition);
    }

    [Fact]
    public void Combine_prefers_aod_code_when_the_same_offer_appears_twice()
    {
        var featured = new BuyingOption
        {
            OfferListingId = "FEATURED",
            Seller = "Furan",
            SellerId = "A131AJORA55M3D",
            Price = "87,25 €",
            Condition = "Nuovo",
        };
        var sameOffer = new BuyingOption
        {
            OfferListingId = "AOD",
            Seller = "Furan",
            SellerId = "A131AJORA55M3D",
            Price = "87,25 €",
            Condition = "Nuovo",
        };
        var other = new BuyingOption
        {
            OfferListingId = "OTHER",
            Seller = "The Idea for Life",
            SellerId = "AJJP4LFEFPII7",
            Price = "96,59 €",
            Condition = "Nuovo",
        };

        var productPage = new BuyingOptionsSummary
        {
            Count = 7,
            LowestPrice = "87,25 €",
            Condition = "Nuovo",
            Options = [featured],
        };
        var aod = new BuyingOptionsSummary
        {
            Count = 7,
            LowestPrice = "87,25 €",
            Options = [sameOffer, other],
        };

        var combined = BuyingOptionsSummary.Combine(productPage, aod);

        Assert.Equal(7, combined.Count);
        Assert.Equal(["AOD", "OTHER"], combined.Options.Select(option => option.OfferListingId));
    }

    [Fact]
    public void Combine_ignores_punctuation_differences_in_the_condition()
    {
        var featured = new BuyingOption
        {
            OfferListingId = "FEATURED",
            Seller = "Bangbai electronic market",
            SellerId = "A2BJX04VEIC906",
            Price = "480,00 €",
            Condition = "D'occasion: Très bon",
        };
        var sameOffer = new BuyingOption
        {
            OfferListingId = "AOD",
            Seller = "Bangbai electronic market",
            SellerId = "A2BJX04VEIC906",
            Price = "480,00 €",
            Condition = "D'occasion - Très bon",
        };

        var productPage = new BuyingOptionsSummary
        {
            Count = 0,
            LowestPrice = "480,00 €",
            Condition = "D'occasion: Très bon",
            Options = [featured],
        };
        var aod = new BuyingOptionsSummary
        {
            Count = 1,
            LowestPrice = "480,00 €",
            Options = [sameOffer],
        };

        var combined = BuyingOptionsSummary.Combine(productPage, aod);

        Assert.Equal(1, combined.Count);
        Assert.Equal(["AOD"], combined.Options.Select(option => option.OfferListingId));
    }

    [Fact]
    public void Combine_keeps_same_seller_with_a_different_condition()
    {
        var featured = new BuyingOption
        {
            OfferListingId = "FEATURED",
            Seller = "Furan",
            SellerId = "A131AJORA55M3D",
            Price = "87,25 €",
            Condition = "Nuovo",
        };
        var used = new BuyingOption
        {
            OfferListingId = "USED",
            Seller = "Furan",
            SellerId = "A131AJORA55M3D",
            Price = "87,25 €",
            Condition = "Usato",
        };

        var productPage = new BuyingOptionsSummary
        {
            Count = 2,
            LowestPrice = "87,25 €",
            Condition = "Nuovo",
            Options = [featured],
        };
        var aod = new BuyingOptionsSummary
        {
            Count = 2,
            LowestPrice = "87,25 €",
            Options = [used],
        };

        var combined = BuyingOptionsSummary.Combine(productPage, aod);

        Assert.Equal(["FEATURED", "USED"], combined.Options.Select(option => option.OfferListingId));
    }

    [Fact]
    public void Featured_condition_prefers_the_active_accordion_row()
    {
        const string html = """
            <div data-csa-c-slot-id="newAccordionRow_0" data-csa-c-asin="B0F54D861F"
                 data-csa-c-is-in-initial-active-row="true">
              <div class="accordion-caption"><span class="a-text-bold"> 新品 </span></div>
            </div>
            <div data-csa-c-slot-id="usedAccordionRow" data-csa-c-asin="B0F54D861F"
                 data-csa-c-is-in-initial-active-row="false">
              <div class="accordion-caption"><span class="a-text-bold"> 中古 - 良い </span></div>
            </div>
            """;

        Assert.Equal("新品", DetailParser.ExtractFeaturedCondition(html));
    }

    [Fact]
    public void Featured_condition_is_empty_without_an_active_accordion_row()
    {
        Assert.Equal(
            string.Empty,
            DetailParser.ExtractFeaturedCondition("<html><body>no accordion</body></html>"));
    }

    [Fact]
    public void Featured_condition_reads_the_renewed_single_offer_caption()
    {
        const string html = """
            <div id="renewedSingleOfferHeader">
              <div id="renewedSingleOfferCaption_feature_div">
                <div class="a-section accordion-caption">
                  <span class="a-size-base a-text-bold a-text-normal">
                    Ricondizionato: eccellente
                  </span>
                </div>
              </div>
            </div>
            """;

        Assert.Equal(
            "Ricondizionato: eccellente",
            DetailParser.ExtractFeaturedCondition(html));
    }

    [Fact]
    public void Featured_condition_uses_new_from_a_mixed_summary()
    {
        const string html = """
            <div id="page-load-buying-option">
              [BuyingOptionData(buyingOptionType=NEW, price=306,19 €)]
            </div>
            """;

        Assert.Equal(
            "Neuf",
            DetailParser.ExtractFeaturedCondition(html, "Neuf & D'occasion"));
    }

    [Fact]
    public void Featured_condition_uses_used_from_a_mixed_summary()
    {
        const string html = """
            <div class="aok-hidden twister-plus-buying-options-price-data">
              {"buyingOptionType":"USED"}
            </div>
            """;

        Assert.Equal(
            "D'occasion",
            DetailParser.ExtractFeaturedCondition(html, "Neuf & D'occasion"));
    }

    [Fact]
    public void Japanese_mixed_summary_keeps_the_featured_condition_separate()
    {
        const string html = """
            <div id="corePrice_desktop">
              <span class="a-price"><span>9,980</span></span>
            </div>
            <div data-csa-c-slot-id="newAccordionRow_0" data-csa-c-asin="B0F54D861F"
                 data-csa-c-is-in-initial-active-row="true">
              <div class="accordion-caption"><span class="a-text-bold"> 新品 </span></div>
            </div>
            <a id="aod-ingress-link" href="/gp/offer-listing/B0F54D861F/ref=dp_olp_ALL_mbc?ie=UTF8&amp;condition=ALL">
              <span>新品＆中古品 (50)件の出品：</span>
              <span class="a-price">
                <span class="a-offscreen">￥6,100</span>
                <span class="a-price-whole">6,100</span>
              </span>
            </a>
            <input type="hidden" id="offerListingID" name="offerListingID" value="TEST%2BOFFER"/>
            <input type="hidden" id="merchantID" name="merchantID" value="A14F3Y9XMICQQV"/>
            """;

        var detail = new DetailParser(JapanMarketplace.Instance).Parse(html, "B0F54D861F");

        Assert.Equal("￥9,980", detail.Price);
        Assert.NotNull(detail.BuyingOptions);
        Assert.Equal(50, detail.BuyingOptions!.Count);
        Assert.Equal("￥6,100", detail.BuyingOptions.LowestPrice);
        Assert.Equal("新品＆中古品", detail.BuyingOptions.Condition);
        var featured = Assert.Single(detail.BuyingOptions.Options);
        Assert.Equal("新品", featured.Condition);
        Assert.Equal("￥9,980", featured.Price);
        Assert.Equal("A14F3Y9XMICQQV", featured.SellerId);
        Assert.Equal("TEST+OFFER", featured.OfferListingId);
    }

    [Fact]
    public void Aod_page_two_url_contains_pagination_parameters()
    {
        IMarketplaceProfile marketplace = FranceMarketplace.Instance;

        var url = marketplace.BuyingOptionsUrl("B08JH7KQKV", 2);

        Assert.Contains("/ref=aod_page_2?", url);
        Assert.Contains("isonlyrenderofferlist=true", url);
        Assert.Contains("pageno=2", url);
    }

    [Fact]
    public void Add_options_merges_pages_and_skips_duplicates()
    {
        var first = new BuyingOption
        {
            OfferListingId = "FIRST",
            Seller = "Realphone",
            SellerId = "A9B1VBHEHY161",
            Price = "536,00 €",
            Condition = "Neuf",
        };
        var second = new BuyingOption
        {
            OfferListingId = "SECOND",
            Seller = "Autre",
            SellerId = "A1OTHER",
            Price = "550,00 €",
            Condition = "Neuf",
        };
        var summary = new BuyingOptionsSummary
        {
            Count = 12,
            Options = [first],
        };

        summary.AddOptions([first, second]);

        Assert.Equal(12, summary.Count);
        Assert.Equal(["FIRST", "SECOND"], summary.Options.Select(option => option.OfferListingId));
    }

    [Fact]
    public void Detail_tsv_contains_buying_option_rows()
    {
        var html = Fixtures.Load("detail2.html.gz");
        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0FMRWBK4R");

        var tsv = TsvFormatter.Detail(detail);
        var lines = tsv.Split('\n');

        Assert.Contains("BuyingOptionsCount\t2", lines);
        Assert.Contains("BuyingOptionsFrom\t24,99 €", lines);

        var option = lines.Single(line => line.StartsWith("BuyingOption\t1\t", StringComparison.Ordinal));
        var fields = option.Split('\t');
        Assert.Equal(10, fields.Length);
        Assert.Equal("Yaqiusi", fields[3]);
        Assert.Equal("AB4K1MTYXAXDZ", fields[4]);
        Assert.Equal("24,99 €", fields[5]);
        Assert.Equal("GRATUITA", fields[6]);
        Assert.Equal("Nuovo", fields[7]);
        Assert.Equal(
            "https://www.amazon.it/dp/B0FMRWBK4R?smid=AB4K1MTYXAXDZ&psc=1",
            fields[9]);
    }

    [Fact]
    public void Combine_skips_the_aod_generic_copy_of_the_featured_offer()
    {
        var featured = new BuyingOption
        {
            OfferListingId = "FEATURED",
            Seller = "Phone-Buddy",
            SellerId = "A20TIXVFK0Z53C",
            Price = "840,00 €",
            Condition = "Ricondizionato: eccellente",
            IsFeatured = true,
        };
        var generic = new BuyingOption
        {
            OfferListingId = "AOD-GENERIC",
            Seller = "Phone-Buddy",
            SellerId = "A20TIXVFK0Z53C",
            Price = "840,00 €",
            Condition = "Rinnovato",
        };
        var other = new BuyingOption
        {
            OfferListingId = "OTHER",
            Seller = "Geekmarket tech",
            SellerId = "A28HFHHGBMSG5M",
            Price = "849,00 €",
            Condition = "Ricondizionato: buono",
        };

        var productPage = new BuyingOptionsSummary
        {
            Count = 15,
            LowestPrice = "840,00 €",
            Options = [featured],
        };
        var aod = new BuyingOptionsSummary
        {
            Count = 15,
            LowestPrice = "840,00 €",
            Options = [generic, other],
        };

        var combined = BuyingOptionsSummary.Combine(productPage, aod);

        Assert.Equal(
            ["FEATURED", "OTHER"],
            combined.Options.Select(option => option.OfferListingId));
    }
}
