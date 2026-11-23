using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailSubtitleTests
{
    [Fact]
    public void Parses_the_title_differentiators_subtitle()
    {
        const string html = """
            <div id="title_feature_div">
              <div id="titleSection">
                <h1 id="title">
                  <span id="productTitle">Logitech MX Keys S Combo</span>
                </h1>
                <div class="a-section dp-title-differentiators">
                  <span class="a-size-base a-color-secondary">
                    Illuminazione personalizzabile, Scorrimento veloce,
                    Bluetooth, USB C, Windows/Linux/Chrome/Mac - Grafite
                  </span>
                </div>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B07W4DHRV2");

        Assert.Equal(
            "Illuminazione personalizzabile, Scorrimento veloce, Bluetooth, USB C, Windows/Linux/Chrome/Mac - Grafite",
            detail.Subtitle);
    }

    [Fact]
    public void Subtitle_is_empty_when_the_block_is_missing()
    {
        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse("<html><body><h1 id=\"title\">Product</h1></body></html>", "B0TEST0006");

        Assert.Equal(string.Empty, detail.Subtitle);
    }
}
