using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailUsuallyKeptTests
{
    [Fact]
    public void Success_badge_is_read()
    {
        const string html = """
            <div id="buyingOptionRipersInlineBadge_feature_div">
              <div id="NEW_1_ripers_inline_badge" class="a-section aok-hidden">
                <div class="a-box a-alert a-alert-success ripers-lrr-badge">
                  <div class="a-box-inner a-alert-container">
                    <i class="a-icon a-icon-alert" aria-hidden="true"></i>
                    <div class="a-alert-content">
                      <p><span>I clienti di solito tengono questo articolo.</span></p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0BK3FGFRC");

        Assert.Equal(
            "I clienti di solito tengono questo articolo.",
            detail.UsuallyKept);
    }

    [Fact]
    public void Badge_without_success_class_is_ignored()
    {
        const string html = """
            <div id="buyingOptionRipersInlineBadge_feature_div">
              <div id="NEW_1_ripers_inline_badge">
                <div class="a-box a-alert ripers-lrr-badge">
                  <p><span>I clienti di solito tengono questo articolo.</span></p>
                </div>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0BK3FGFRC");

        Assert.Empty(detail.UsuallyKept);
    }

    [Fact]
    public void Localized_badge_text_is_kept_as_is()
    {
        const string html = """
            <div id="buyingOptionRipersInlineBadge_feature_div">
              <div class="a-box a-alert a-alert-success ripers-lrr-badge">
                <p><span>Customers usually keep this item.</span></p>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0TEST0000");

        Assert.Equal("Customers usually keep this item.", detail.UsuallyKept);
    }
}
