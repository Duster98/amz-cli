using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailAmazonDetailsTests
{
    [Fact]
    public void Secondary_tables_go_to_amazon_details_not_specs()
    {
        const string html = """
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">カラー</th>
                <td class="a-size-base prodDetAttrValue">マルチカラー</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">メーカー名</th>
                <td class="a-size-base prodDetAttrValue">BANDAI SPIRITS</td>
              </tr>
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Amazon 売れ筋ランキング</th>
                <td>
                  <ul>
                    <li>ホビー - 238位</li>
                    <li>フィギュア - 4位</li>
                  </ul>
                </td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(JapanMarketplace.Instance)
            .Parse(html, "B0HR4QBPCR");

        Assert.Equal("マルチカラー", detail.Specs["カラー"]);
        Assert.DoesNotContain(
            "Amazon 売れ筋ランキング",
            detail.Specs.Keys);

        Assert.NotNull(detail.AmazonDetails);
        var manufacturer = Assert.Single(
            detail.AmazonDetails!,
            item => item.Name == "メーカー名");
        Assert.Equal("BANDAI SPIRITS", manufacturer.Value);

        var ranking = Assert.Single(
            detail.AmazonDetails!,
            item => item.Name == "Amazon 売れ筋ランキング");
        Assert.Contains("ホビー - 238位", ranking.Value);
        Assert.Contains("フィギュア - 4位", ranking.Value);
        Assert.Contains(" · ", ranking.Value);
    }

    [Fact]
    public void Parses_the_model_name_from_the_secondary_details()
    {
        const string html = """
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">GPU Clock Speed</th>
                <td class="a-size-base prodDetAttrValue">2595 MHz</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Model Name</th>
                <td class="a-size-base prodDetAttrValue">GV-N5060GAMING OC-8GD</td>
              </tr>
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Model Number</th>
                <td class="a-size-base prodDetAttrValue">GV-N5060GAMING OC-8GD</td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(GermanyMarketplace.Instance)
            .Parse(html, "B0F6NJT2K6");

        Assert.Equal("GV-N5060GAMING OC-8GD", detail.ModelName);
        Assert.Contains(
            "Model\tGV-N5060GAMING OC-8GD",
            TsvFormatter.Detail(detail));
    }

    [Fact]
    public void Review_summary_rows_are_skipped_and_inline_scripts_never_leak()
    {
        const string html = """
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Marca</th>
                <td class="a-size-base prodDetAttrValue">HONOR</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Media recensioni</th>
                <td class="a-size-base">
                  <div id="averageCustomerReviews" data-asin="B0HHF3NQN2">
                    <span id="acrPopover" title="4,6 su 5 stelle">4,6</span>
                    <span id="acrCustomerReviewText" aria-label="3 Recensioni">(3)</span>
                    <script type="text/javascript">
                      var dpAcrHasRegisteredArcLinkClickAction;
                      P.when('A', 'ready').execute(function(A) {});
                    </script>
                  </div>
                </td>
              </tr>
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">ASIN</th>
                <td>B0HHF3NQN2</td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0HHF3NQN2");

        Assert.NotNull(detail.AmazonDetails);
        Assert.DoesNotContain(
            detail.AmazonDetails!,
            item => item.Name == "Media recensioni");
        Assert.DoesNotContain(
            detail.AmazonDetails!,
            item => item.Value.Contains("P.when", StringComparison.Ordinal));
        var asin = Assert.Single(
            detail.AmazonDetails!,
            item => item.Name == "ASIN");
        Assert.Equal("B0HHF3NQN2", asin.Value);
    }
}
