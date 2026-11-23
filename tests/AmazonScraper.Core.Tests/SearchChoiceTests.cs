using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class SearchChoiceTests
{
    [Fact]
    public void Italy_fixture_exposes_amazon_choice_and_for_text()
    {
        var results = new SearchParser(ItalyMarketplace.Instance)
            .Parse(Fixtures.Load("search.html.gz"));
        var result = results.Single(item => item.Asin == "B0DTJDR3V9");

        Assert.True(result.AmazonChoice);
        Assert.Equal("Scelta Amazon", result.AmazonChoiceText);
        Assert.Equal("per rtx 5080", result.AmazonChoiceFor);

        var tsv = TsvFormatter.Search([result]);
        Assert.Contains("\ttrue\tScelta Amazon\tper rtx 5080", tsv);
        var json = JsonFormatter.Search([result]);
        Assert.Contains("\"amazon_choice\": true", json);
        Assert.Contains("\"amazon_choice_text\": \"Scelta Amazon\"", json);
        Assert.Contains("\"amazon_choice_for\": \"per rtx 5080\"", json);
    }

    [Fact]
    public void US_fixture_uses_overall_pick_label()
    {
        var results = new SearchParser(UnitedStatesMarketplace.Instance)
            .Parse(Fixtures.Load("us-search.html.gz"));
        var result = results.Single(item => item.Asin == "B0GWRN4Q36");

        Assert.True(result.AmazonChoice);
        Assert.Equal("Overall Pick", result.AmazonChoiceText);
        Assert.Null(result.AmazonChoiceFor);
    }

    [Fact]
    public void Add_to_cart_fragment_is_not_emitted_as_a_product()
    {
        const string html = """
            <div data-asin="B0ATC12345" class="a-section atc-faceout-container">
              <form><input name="items[0.base][asin]" value="B0ATC12345"/></form>
            </div>
            <div data-asin="B0ATC12345" data-component-type="s-search-result" class="s-result-item">
              <h2><span>Prodotto reale</span></h2>
              <span class="a-price-whole">19<span class="a-price-fraction">99</span></span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Equal("B0ATC12345", result.Asin);
        Assert.Equal("Prodotto reale", result.Title);
        Assert.Equal("19,99 €", result.Price);
    }

    [Fact]
    public void Card_without_choice_leaves_fields_null()
    {
        const string html = """
            <div data-asin="B000000001">
              <h2><span>Prodotto normale</span></h2>
              <span class="a-price-whole">10<span class="a-price-fraction">00</span></span>
            </div>
            """;

        var result = Assert.Single(
            new SearchParser(ItalyMarketplace.Instance).Parse(html));

        Assert.Null(result.AmazonChoice);
        Assert.Null(result.AmazonChoiceText);
        Assert.Null(result.AmazonChoiceFor);
    }
}
