using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class FranceParserTests
{
    [Fact]
    public void France_search_extracts_english_rating_and_reviews()
    {
        var html = Fixtures.Load("fr-search.html.gz");
        var results = new SearchParser(FranceMarketplace.Instance).Parse(html);

        Assert.Equal(59, results.Count);
        Assert.Equal(11, results.Count(r => r.Sponsored));
        Assert.Contains(results, r => r.Rating.Contains("/ 5", StringComparison.Ordinal) && r.Rating != string.Empty);
        Assert.Contains(results, r => !string.IsNullOrEmpty(r.Reviews));

        var first = results[0];
        Assert.Equal("B0FDQTZXDQ", first.Asin);
        Assert.Equal("499,99 €", first.Price);
        Assert.Equal("4,6 / 5", first.Rating);
        Assert.Equal("155", first.Reviews);
    }

    [Fact]
    public void France_search_reads_nbsp_review_counts()
    {
        var html =
            "<div data-asin=\"B0BTJ9WHL9\" data-component-type=\"s-search-result\">" +
            "<h2 aria-label=\"Sony WH-CH520\"></h2>" +
            "<span class=\"a-icon-alt\">4,6 sur 5 étoiles</span>" +
            "<span aria-label=\"47\u00a0379\u00a0évaluations\"></span>" +
            "<span class=\"a-price\"><span class=\"a-offscreen\">26,99 €</span></span>" +
            "</div>";
        var results = new SearchParser(FranceMarketplace.Instance).Parse(html);
        var item = results.First(r => r.Asin == "B0BTJ9WHL9");

        Assert.Equal("47 379", item.Reviews);
    }

    [Fact]
    public void France_detail_extracts_seller_from_single_quoted_link()
    {
        var html = """
            <html><body>
            <div class="offer-display-features-container">
              <div class="offer-display-feature-label">Vendu par</div>
              <div class="offer-display-feature-text">
                <a href="/gp/help/seller/at-a-glance.html?seller=A1KMG02JNH3213"
                   id='sellerProfileTriggerId'>aisuoduo</a>
              </div>
            </div>
            </body></html>
            """;

        var detail = new DetailParser(FranceMarketplace.Instance)
            .Parse(html, "B0BF8RBDFR");

        Assert.Equal("aisuoduo", detail.Seller);
    }

    [Fact]
    public void France_detail_extracts_localized_fields_and_url()
    {
        var html = Fixtures.Load("fr-detail.html.gz");
        var detail = new DetailParser(FranceMarketplace.Instance).Parse(html, "B0972BP9YR");

        Assert.Equal("519,99 €", detail.Price);
        Assert.Equal("599,99 €", detail.OriginalPrice);
        Assert.Equal("-13%", detail.Discount);
        Assert.Equal("4,6 / 5", detail.Rating);
        Assert.Equal("666", detail.Reviews);
        Assert.Equal("Amazon", detail.Seller);
        Assert.Equal("Amazon", detail.ShippedBy);
        Assert.Contains("Returnable", detail.Returns);
        Assert.Equal("In stock", detail.Availability);
        Assert.NotEmpty(detail.Images);
        Assert.NotEmpty(detail.Description);
        Assert.NotEmpty(detail.Specs);
        Assert.Equal("https://www.amazon.fr/dp/B0972BP9YR", detail.Url);
    }

    [Fact]
    public void France_live_detail_marks_shipped_by_amazon()
    {
        var html = Fixtures.Load("fr-detail-live.html.gz");
        var detail = new DetailParser(FranceMarketplace.Instance).Parse(html, "B08JH7KQKV");

        Assert.Equal("Amazon", detail.Seller);
        Assert.Equal("Amazon", detail.ShippedBy);
        Assert.Contains("Retournable", detail.Returns);
        Assert.NotEmpty(detail.Specs);
    }

    [Fact]
    public void France_price_parsing_supports_dot_decimal_format()
    {
        Assert.True(FranceMarketplaceFormat.Instance.TryParsePrice("599.99 €", out var amount));
        Assert.Equal(599.99m, amount);
        Assert.Equal("599,99 €", FranceMarketplaceFormat.Instance.OriginalPrice("599.99"));
        Assert.Equal("1.119,00 €", FranceMarketplaceFormat.Instance.SinglePrice("1,119.00"));
        Assert.Equal("1.119,00 €", FranceMarketplaceFormat.Instance.SinglePrice("1 119,00"));
    }
}
