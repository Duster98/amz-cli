using System.Text.Json;
using System.Text.Json.Nodes;
using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class ParserParityTests
{
    [Fact]
    public void Search_fixture_matches_python_golden()
    {
        var html = Fixtures.Load("search.html.gz");
        var results = new SearchParser(ItalyMarketplace.Instance).Parse(html);

        var actual = JsonNode.Parse(JsonSerializer.Serialize(results, JsonFormatter.Options));
        if (actual is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is JsonObject item)
                {
                    item.Remove("deal");
                    item.Remove("deal_id");
                    item.Remove("original_price");
                    item.Remove("discount");
                    item.Remove("amazon_choice");
                    item.Remove("amazon_choice_text");
                    item.Remove("amazon_choice_for");
                }
            }
        }
        var expected = Fixtures.LoadJson("expected-search.json");

        Assert.Equal(48, results.Count);
        Assert.True(JsonNode.DeepEquals(expected, actual), "search JSON differs from Python golden");
    }

    [Theory]
    [InlineData("detail.html.gz", "expected-detail.json", "B0DTJDR3V9")]
    [InlineData("detail2.html.gz", "expected-detail2.json", "B0FMRWBK4R")]
    public void Detail_fixture_matches_python_golden(string htmlFile, string expectedFile, string asin)
    {
        var html = Fixtures.Load(htmlFile);
        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, asin);

        var actual = JsonNode.Parse(JsonSerializer.Serialize(detail, JsonFormatter.Options))!.AsObject();
        // Delivery and buying options are .NET-only extensions; the Python
        // golden covers the shared schema only.
        actual.Remove("delivery");
        actual.Remove("buying_options");
        actual.Remove("deal");
        actual.Remove("deal_id");
        actual.Remove("deal_ends_text");
        actual.Remove("deal_ends_on");
        actual.Remove("deal_ends_in_days");
        actual.Remove("deal_has_countdown");
        actual.Remove("variants");
        actual.Remove("amazon_details");
        actual.Remove("amazon_choice");
        actual.Remove("amazon_choice_text");
        actual.Remove("amazon_choice_for");
        actual.Remove("import_fees");
        actual.Remove("safety_warnings");
        actual.Remove("subtitle");
        actual.Remove("model_name");
        actual.Remove("usually_kept");
        var expected = Fixtures.LoadJson(expectedFile);

        Assert.True(JsonNode.DeepEquals(expected, actual), "detail JSON differs from Python golden");
        Assert.Equal($"https://www.amazon.it/dp/{asin}", detail.Url);
    }

    [Fact]
    public void Search_tsv_has_python_header_and_first_row()
    {
        var html = Fixtures.Load("search.html.gz");
        var results = new SearchParser(ItalyMarketplace.Instance).Parse(html);
        var tsv = TsvFormatter.Search(results);
        var lines = tsv.Split('\n');

        Assert.Equal(
            "ASIN\tTitle\tPrice\tRating\tReviews\tCoupon\tSponsored\tDeal\tDealId\tOriginalPrice\tDiscount\tChoice\tChoiceText\tChoiceFor",
            lines[0]);
        Assert.StartsWith("B0DS643QRW\t", lines[1]);
        Assert.Contains("\t1.692,51 €\t4,8 / 5\t205\t\t", lines[1]);
    }

    [Fact]
    public void Italy_detail_reads_prodDetTable_without_localized_heading()
    {
        const string html = """
            <div>
              <table class="a-keyvalue prodDetTable">
                <tr>
                  <th class="a-color-secondary a-size-base prodDetSectionEntry">Formato schermo</th>
                  <td class="a-size-base prodDetAttrValue">16:9</td>
                </tr>
                <tr>
                  <th class="a-color-secondary a-size-base prodDetSectionEntry">Frequenza di aggiornamento</th>
                  <td class="a-size-base prodDetAttrValue">60 Hz</td>
                </tr>
              </table>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0BY2R4BHW");

        Assert.Equal("16:9", detail.Specs["Formato schermo"]);
        Assert.Equal("60 Hz", detail.Specs["Frequenza di aggiornamento"]);
    }

    [Fact]
    public void Detail_tsv_contains_domain_url_and_specs_are_not_in_tsv()
    {
        var html = Fixtures.Load("detail.html.gz");
        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0DTJDR3V9");
        var tsv = TsvFormatter.Detail(detail);

        Assert.Contains("URL\thttps://www.amazon.it/dp/B0DTJDR3V9", tsv);
        Assert.Contains("Price\t1.250,50 €", tsv);
        Assert.DoesNotContain("Specs", tsv);
    }
}
