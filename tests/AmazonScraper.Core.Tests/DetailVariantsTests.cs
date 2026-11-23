using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailVariantsTests
{
    [Fact]
    public void Parses_variants_and_modern_frequently_returned_badge()
    {
        const string html = """
            <div id="title">Ediloca EN600 PRO 256GB</div>
            <div class="a-price-whole">53<span class="a-price-fraction">99</span></div>
            <div id="buyingOptionRipersBadge_feature_div">
              <h2 class="a-spacing-none ripers-hrr-badge-title">
                <p><span class="a-text-bold">Restituito frequentemente</span>
                <span> a causa di problemi di compatibilità</span></p>
              </h2>
            </div>
            <script>
            P.register('twister-js-init-dpx-data', function() {
              var dataToReturn = {
                "parentAsin": "B0F21C1SY9",
                "dimensions": ["style_name", "size_name"],
                "variationDisplayLabels": {"size_name": "Capacità", "style_name": "Nome stile"},
                "dimensionValuesDisplayData": {
                  "B0C9C4GHGN": ["EN600 pro", "256GB"],
                  "B0CBJHP6Z3": ["EN600 pro", "512GB"],
                  "B0B7QFDFFQ": ["EN605", "256GB"]
                },
                "selectedVariationValues": {"style_name": 0, "size_name": 1}
              };
              return dataToReturn;
            });
            </script>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0C9C4GHGN");

        Assert.Equal(
            "Restituito frequentemente a causa di problemi di compatibilità",
            detail.FrequentlyReturned);
        Assert.NotNull(detail.Variants);
        Assert.Equal("B0F21C1SY9", detail.Variants.ParentAsin);
        Assert.Equal("B0C9C4GHGN", detail.Variants.SelectedAsin);
        Assert.Equal("Capacità", detail.Variants.Dimensions["size_name"]);
        Assert.Equal(3, detail.Variants.Items.Count);
        Assert.True(detail.Variants.Items[0].Selected);
        Assert.Equal("B0C9C4GHGN", detail.Variants.Items[0].Asin);
        Assert.Equal("256GB", detail.Variants.Items[0].Attributes["Capacità"]);
        Assert.Equal(
            "https://www.amazon.it/dp/B0C9C4GHGN",
            detail.Variants.Items[0].Url);
    }
}
