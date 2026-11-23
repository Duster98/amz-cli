using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class ProductBadgesTests
{
    [Fact]
    public void Parses_us_choice_badge_and_numeric_import_fees()
    {
        const string html = """
            <div id="acBadge_feature_div">
              <div class="mvt-ac-badge-wrapper">
                <div class="a-popover-preload">
                  <span>Amazon's Choice highlights highly rated products.</span>
                </div>
                <span class="aok-float-left mvt-ac-badge-rectangle">
                  <span class="a-size-small">Amazon's Choice</span>
                </span>
                <span class="mvt-ac-for-text mvt-desc-space">for "rtx 5080"</span>
              </div>
            </div>
            <div id="amazonGlobal_feature_div">
              <span class="a-size-base a-color-secondary">
                $139.79 Shipping &amp; Import Charges to Italy
              </span>
              <div class="a-popover-preload">
                <table class="a-lineitem">
                  <tr>
                    <td class="a-span9 a-text-left"><span>Price</span></td>
                    <td class="a-span1"></td>
                    <td class="a-span2 a-text-right"><span>$424.99</span></td>
                  </tr>
                  <tr>
                    <td class="a-span9 a-text-left"><span>AmazonGlobal Shipping</span></td>
                    <td class="a-span1"></td>
                    <td class="a-span2 a-text-right"><span>$30.62</span></td>
                  </tr>
                  <tr>
                    <td class="a-span9 a-text-left"><span>Estimated Import Charges</span></td>
                    <td class="a-span1"></td>
                    <td class="a-span2 a-text-right"><span>$109.17</span></td>
                  </tr>
                  <tr>
                    <td class="a-span9 a-text-left"><span>Total</span></td>
                    <td class="a-span1"></td>
                    <td class="a-span2 a-text-right"><span>$564.78</span></td>
                  </tr>
                </table>
              </div>
            </div>
            """;

        var detail = new DetailParser(UnitedStatesMarketplace.Instance)
            .Parse(html, "B0HCPYBK67");

        Assert.True(detail.AmazonChoice);
        Assert.Equal("Amazon's Choice", detail.AmazonChoiceText);
        Assert.Equal("for \"rtx 5080\"", detail.AmazonChoiceFor);

        var importFees = Assert.IsType<ImportFeesInfo>(detail.ImportFees);
        Assert.Equal("$424.99", importFees.Price);
        Assert.Equal("$30.62", importFees.Shipping);
        Assert.Equal("$109.17", importFees.ImportCharges);
        Assert.Equal("$564.78", importFees.LandedTotal);
        Assert.False(importFees.AtCheckout);
        Assert.Equal("$139.79 Shipping & Import Charges to Italy", importFees.Text);

        var withDestination = ProductBadgesParser.ParseImportFees(
            html,
            new DeliveryInfo { Location = "Italy" },
            "$424.99",
            "$499.99",
            UnitedStatesFormat.Instance);
        Assert.NotNull(withDestination);
        Assert.Equal("Italy", withDestination.Destination);
        Assert.Equal("current", withDestination.PriceBasis);
    }

    [Fact]
    public void Import_table_can_use_the_list_price()
    {
        const string html = """
            <div id="amazonGlobal_feature_div">
              <span class="a-size-base a-color-secondary">
                $16.22 Shipping &amp; Import Charges to Italy
              </span>
              <table class="a-lineitem">
                <tr><td>Price</td><td></td><td>$39.99</td></tr>
                <tr><td>AmazonGlobal Shipping</td><td></td><td>$10.71</td></tr>
                <tr><td>Estimated Import Charges</td><td></td><td>$5.51</td></tr>
                <tr><td>Total</td><td></td><td>$56.21</td></tr>
              </table>
            </div>
            """;

        var fees = ProductBadgesParser.ParseImportFees(
            html,
            new DeliveryInfo { Location = "Italy" },
            "$24.99",
            "$39.99",
            UnitedStatesFormat.Instance);

        Assert.NotNull(fees);
        Assert.Equal("$39.99", fees.Price);
        Assert.Equal("list", fees.PriceBasis);
        Assert.Equal("$56.21", fees.LandedTotal);
    }

    [Fact]
    public void Parses_uk_import_disclosure_without_amounts()
    {
        const string html = """
            <div id="amazonGlobal_feature_div">
              <span class="a-size-base a-color-secondary">
                Import Charges applied at checkout in addition to price shown.
              </span>
              <span class="a-size-base">
                <a href="/help">See details.</a>
              </span>
            </div>
            """;

        var detail = new DetailParser(UnitedKingdomMarketplace.Instance)
            .Parse(html, "B0B9C3ZVHR");

        var importFees = Assert.IsType<ImportFeesInfo>(detail.ImportFees);
        Assert.True(importFees.AtCheckout);
        Assert.Null(importFees.Shipping);
        Assert.Null(importFees.ImportCharges);
        Assert.Null(importFees.LandedTotal);
        Assert.Equal(
            "Import Charges applied at checkout in addition to price shown.",
            importFees.Text);
    }

    [Fact]
    public void Localized_choice_and_absent_import_block_are_handled()
    {
        const string html = """
            <div id="acBadge_feature_div">
              <div class="mvt-ac-badge-wrapper">
                <span class="mvt-ac-badge-rectangle">Scelta Amazon</span>
                <span class="mvt-ac-for-text">per "rtx 5080"</span>
              </div>
            </div>
            <div id="amazonGlobal_feature_div"></div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0DTJDR3V9");

        Assert.True(detail.AmazonChoice);
        Assert.Equal("Scelta Amazon", detail.AmazonChoiceText);
        Assert.Equal("per \"rtx 5080\"", detail.AmazonChoiceFor);
        Assert.Null(detail.ImportFees);
    }

    [Fact]
    public void Domestic_page_has_no_choice_or_import_block()
    {
        const string html = """
            <div id="acBadge_feature_div"></div>
            <div id="amazonGlobal_feature_div"></div>
            """;

        var detail = new DetailParser(UnitedStatesMarketplace.Instance)
            .Parse(html, "B0HCPYBK67");

        Assert.Null(detail.AmazonChoice);
        Assert.Null(detail.ImportFees);
    }

    [Fact]
    public void Parses_localized_import_table()
    {
        const string html = """
            <div id="amazonGlobal_feature_div">
              <span class="a-size-base a-color-secondary">
                Spedizione e tasse di importazione per l'Italia
              </span>
              <table class="a-lineitem">
                <tr>
                  <td>Prezzo</td><td></td><td>100,00 €</td>
                </tr>
                <tr>
                  <td>Spedizione AmazonGlobal</td><td></td><td>10,00 €</td>
                </tr>
                <tr>
                  <td>Tasse di importazione stimate</td><td></td><td>22,00 €</td>
                </tr>
                <tr>
                  <td>Totale</td><td></td><td>132,00 €</td>
                </tr>
              </table>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0HCPYBK67");

        var importFees = Assert.IsType<ImportFeesInfo>(detail.ImportFees);
        Assert.Equal("10,00 €", importFees.Shipping);
        Assert.Equal("22,00 €", importFees.ImportCharges);
        Assert.Equal("132,00 €", importFees.LandedTotal);
        Assert.False(importFees.AtCheckout);
    }

    [Fact]
    public void Detail_tsv_exposes_choice_and_import_fields()
    {
        var detail = new ProductDetail
        {
            Asin = "B0HCPYBK67",
            AmazonChoice = true,
            AmazonChoiceText = "Amazon's Choice",
            AmazonChoiceFor = "for \"rtx 5080\"",
            ImportFees = new ImportFeesInfo
            {
                Price = "$424.99",
                PriceBasis = "current",
                Destination = "Italy",
                Shipping = "$30.62",
                ImportCharges = "$109.17",
                LandedTotal = "$564.78",
                AtCheckout = false,
                Text = "$139.79 Shipping & Import Charges to Italy",
            },
        };

        var tsv = AmazonScraper.Core.Formatting.TsvFormatter.Detail(detail);

        Assert.Contains("AmazonChoice\ttrue", tsv);
        Assert.Contains("AmazonChoiceText\tAmazon's Choice", tsv);
        Assert.Contains("AmazonChoiceFor\tfor \"rtx 5080\"", tsv);
        Assert.Contains("ImportPrice\t$424.99", tsv);
        Assert.Contains("ImportPriceBasis\tcurrent", tsv);
        Assert.Contains("ImportDestination\tItaly", tsv);
        Assert.Contains("ImportShipping\t$30.62", tsv);
        Assert.Contains("ImportCharges\t$109.17", tsv);
        Assert.Contains("ImportLandedTotal\t$564.78", tsv);
        Assert.Contains("ImportAtCheckout\tfalse", tsv);
    }

    [Fact]
    public void Compare_tsv_exposes_choice_and_landed_total()
    {
        var detail = new ProductDetail
        {
            Asin = "B0HCPYBK67",
            Title = "ASUS Vivobook",
            AmazonChoice = true,
            ImportFees = new ImportFeesInfo
            {
                Price = "$39.99",
                PriceBasis = "list",
                Destination = "Italy",
                ImportCharges = "$109.17",
                LandedTotal = "$564.78",
                AtCheckout = false,
                Text = "$139.79 Shipping & Import Charges to Italy",
            },
        };

        var lines = AmazonScraper.Core.Formatting.TsvFormatter
            .Compare([detail])
            .Split('\n');

        Assert.Contains(
            "AmazonChoice\tImportPrice\tImportPriceBasis\tImportCharges\tLandedTotal\tImportAtCheckout\tImportText",
            lines[0]);
        Assert.Contains("true\t$39.99\tlist\t$109.17\t$564.78\tfalse\t", lines[1]);
    }

    [Fact]
    public void Parses_best_seller_badge_with_rank_category_and_url()
    {
        const string html = """
            <div id="zeitgeistBadge_feature_div">
              <div class="zg-bf-badge-wrapper">
                <a class="badge-link" href="/gp/bestsellers/pc/460154031/ref=zg_b_bs_460154031_1" title="Tastiere">
                  <span class="a-size-small mvt-best-seller-badge">il #1 più venduto</span>
                  <span class="mvt-cat-name">
                    <span class="a-size-small cat-link">in Tastiere</span>
                  </span>
                </a>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B07W8P53H7");

        Assert.Equal(1, detail.BestSellerRank);
        Assert.Equal("Tastiere", detail.BestSellerCategory);
        Assert.Equal("il #1 più venduto", detail.BestSellerLabel);
        Assert.Equal(
            "https://www.amazon.it/gp/bestsellers/pc/460154031/ref=zg_b_bs_460154031_1",
            detail.BestSellerUrl);
    }

    [Fact]
    public void Best_seller_is_empty_when_the_badge_is_missing()
    {
        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse("<html><body><h1 id=\"title\">Product</h1></body></html>", "B0TEST0038");

        Assert.Null(detail.BestSellerRank);
        Assert.Null(detail.BestSellerCategory);
        Assert.Null(detail.BestSellerLabel);
        Assert.Null(detail.BestSellerUrl);
    }

    [Fact]
    public void Detail_tsv_exposes_best_seller_fields()
    {
        var detail = new ProductDetail
        {
            Asin = "B07W8P53H7",
            BestSellerRank = 1,
            BestSellerCategory = "Tastiere",
            BestSellerLabel = "il #1 più venduto",
            BestSellerUrl = "https://www.amazon.it/gp/bestsellers/pc/460154031/",
        };

        var tsv = AmazonScraper.Core.Formatting.TsvFormatter.Detail(detail);

        Assert.Contains("BestSellerRank\t1", tsv);
        Assert.Contains("BestSellerCategory\tTastiere", tsv);
        Assert.Contains("BestSellerLabel\til #1 più venduto", tsv);
        Assert.Contains("BestSellerUrl\thttps://www.amazon.it/gp/bestsellers/pc/460154031/", tsv);
    }
}
