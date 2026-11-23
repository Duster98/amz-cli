using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class SpainParserTests
{
    [Fact]
    public void Spain_search_uses_spanish_euro_format()
    {
        var html = Fixtures.Load("es-search.html.gz");
        var results = new SearchParser(SpainMarketplace.Instance).Parse(html);

        Assert.Equal(64, results.Count);
        Assert.Equal(40, results.Count(r => !string.IsNullOrEmpty(r.Price)));
        Assert.Equal(62, results.Count(r => !string.IsNullOrEmpty(r.Rating)));

        var first = results[0];
        Assert.Equal("B0DTJFZ4YS", first.Asin);
        Assert.Contains("RTX", first.Title);
        Assert.DoesNotContain("Anuncio patrocinado", first.Title);
        Assert.Equal("1.930,40 €", first.Price);
        Assert.Equal("4,5 / 5", first.Rating);
        Assert.Equal("205", first.Reviews);
    }

    [Fact]
    public void Spain_search_reads_reviews_from_long_aria_label()
    {
        const string html = """
            <div data-asin="B0H9ST5WN3">
              <h2 aria-label="LG gram">LG gram</h2>
              <span class="a-icon-alt">5 de 5 estrellas.</span>
              <a aria-label="Tiene una valoración de 5 de 5 estrellas por 1 reseñas. Ir a la sección de reseñas.">1</a>
            </div>
            """;

        var results = new SearchParser(SpainMarketplace.Instance).Parse(html);

        var result = Assert.Single(results);
        Assert.Equal("5 / 5", result.Rating);
        Assert.Equal("1", result.Reviews);
    }

    [Fact]
    public void Spain_search_reads_calificaciones_review_label()
    {
        const string html = """
            <div data-asin="B09CDB56GF">
              <h2 aria-label="ASUS Vivobook 15">ASUS Vivobook 15</h2>
              <span class="a-icon-alt">4,8 de 5 estrellas</span>
              <a aria-label="6 calificaciones">6</a>
            </div>
            """;

        var results = new SearchParser(SpainMarketplace.Instance).Parse(html);

        var result = Assert.Single(results);
        Assert.Equal("4,8 / 5", result.Rating);
        Assert.Equal("6", result.Reviews);
    }

    [Fact]
    public void Spain_detail_without_main_price_does_not_take_ads()
    {
        var html = Fixtures.Load("es-detail.html.gz");
        var detail = new DetailParser(SpainMarketplace.Instance).Parse(html, "B09CL8ZFJ5");

        Assert.StartsWith("ASUS ProArt", detail.Title);
        Assert.Equal("ASUS", detail.Brand);
        Assert.Empty(detail.Price);
        Assert.Equal("4,7 / 5", detail.Rating);
        Assert.Equal("24", detail.Reviews);
        Assert.NotEmpty(detail.Specs);
        Assert.NotEmpty(detail.Images);
        Assert.Equal("https://www.amazon.es/dp/B09CL8ZFJ5", detail.Url);
    }

    [Fact]
    public void Spain_live_detail_extracts_euro_and_returns()
    {
        var html = Fixtures.Load("es-detail-live.html.gz");
        var detail = new DetailParser(SpainMarketplace.Instance).Parse(html, "B0DTJFZ4YS");

        Assert.Equal("PNY Tarjeta Gráfica GeForce RTX™ 5080 16GB OC Triple Fan DLSS 4", detail.Title);
        Assert.Equal("PNY", detail.Brand);
        Assert.Equal("1.930,40 €", detail.Price);
        Assert.Equal("4,5 / 5", detail.Rating);
        Assert.Equal("205", detail.Reviews);
        Assert.Equal("Amazon", detail.Seller);
        Assert.Equal("Amazon", detail.ShippedBy);
        Assert.Contains("devolver", detail.Returns);
        Assert.Equal("En stock", detail.Availability);
        Assert.NotEmpty(detail.Specs);
        Assert.Equal("https://www.amazon.es/dp/B0DTJFZ4YS", detail.Url);
    }

    [Fact]
    public void Spain_format_paths_and_cookie_force_eur()
    {
        Assert.Equal("1.930,40 €", SpainFormat.Instance.Price("1,930", "40"));
        Assert.Equal("1.930,40 €", SpainFormat.Instance.OriginalPrice("€1,930.40"));
        Assert.True(SpainFormat.Instance.TryParsePrice("1.930,40 €", out var amount));
        Assert.Equal(1930.40m, amount);
        Assert.True(SpainFormat.Instance.TryParsePrice("€1,930.40", out var dotAmount));
        Assert.Equal(1930.40m, dotAmount);

        Assert.Equal(
            "https://www.amazon.es/-/es/s?k=rtx%205080",
            SpainMarketplace.Instance.SearchUrl("rtx 5080"));
        Assert.Equal("https://www.amazon.es/-/es/dp/B0DTJFZ4YS", SpainMarketplace.Instance.DetailUrl("B0DTJFZ4YS"));
        Assert.Equal("https://www.amazon.es/dp/B0DTJFZ4YS", SpainMarketplace.Instance.ProductUrl("B0DTJFZ4YS"));
        Assert.Equal("i18n-prefs=EUR", SpainMarketplace.Instance.DefaultCookieHeader);
        Assert.False(SpainMarketplace.Instance.WarmUpEnabled);
    }
}
