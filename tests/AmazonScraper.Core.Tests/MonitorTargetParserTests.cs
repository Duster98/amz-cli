using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorTargetParserTests
{
    [Fact]
    public void Bare_asin_uses_default_domain()
    {
        var target = MonitorTargetParser.Parse("b07q6y5sc7", "it");

        Assert.Equal("it", target.Domain);
        Assert.Equal("B07Q6Y5SC7", target.Asin);
    }

    [Fact]
    public void Domain_prefix_overrides_default()
    {
        var target = MonitorTargetParser.Parse("de:B0DP1VG1DZ", "it");

        Assert.Equal("de", target.Domain);
        Assert.Equal("B0DP1VG1DZ", target.Asin);
    }

    [Fact]
    public void Us_alias_maps_to_com()
    {
        var target = MonitorTargetParser.Parse("us:B08DL8WH9V", "it");

        Assert.Equal("com", target.Domain);
    }

    [Theory]
    [InlineData(
        "https://www.amazon.co.uk/dp/B0D9YDFGHD?th=1",
        "uk",
        "B0D9YDFGHD")]
    [InlineData(
        "https://www.amazon.co.jp/gp/product/B089SJRZ42/ref=ppx",
        "jp",
        "B089SJRZ42")]
    [InlineData(
        "https://www.amazon.es/-/es/dp/B0CGVQNB9F",
        "es",
        "B0CGVQNB9F")]
    public void Amazon_url_infers_domain(string url, string domain, string asin)
    {
        var target = MonitorTargetParser.Parse(url, "it");

        Assert.Equal(domain, target.Domain);
        Assert.Equal(asin, target.Asin);
    }

    [Fact]
    public void Relative_path_uses_default_domain()
    {
        var target = MonitorTargetParser.Parse("/dp/B07Q6Y5SC7", "fr");

        Assert.Equal("fr", target.Domain);
        Assert.Equal("B07Q6Y5SC7", target.Asin);
    }

    [Fact]
    public void Invalid_asin_is_rejected()
    {
        Assert.Throws<MonitorException>(() =>
            MonitorTargetParser.Parse("B07", "it"));
    }

    [Fact]
    public void Unsupported_domain_is_rejected()
    {
        Assert.Throws<MonitorException>(() =>
            MonitorTargetParser.Parse("nl:B07Q6Y5SC7", "it"));
    }

    [Fact]
    public void Asin_file_supports_plain_text_and_comments()
    {
        var path = TempFile(
            """
            # list
            B07Q6Y5SC7
            de:B0DP1VG1DZ
            https://www.amazon.fr/dp/B0HN7CZHSD
            """);

        try
        {
            var targets = MonitorTargetParser.ReadFile(path, "it");

            Assert.Equal(3, targets.Count);
            Assert.Equal(["it", "de", "fr"], targets.Select(item => item.Domain));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Asin_file_supports_csv_header()
    {
        var path = TempFile(
            """
            domain,asin
            it,B07Q6Y5SC7
            de,B0DP1VG1DZ
            """);

        try
        {
            var targets = MonitorTargetParser.ReadFile(path, "it");

            Assert.Equal(2, targets.Count);
            Assert.Equal("it:B07Q6Y5SC7", targets[0].Key);
            Assert.Equal("de:B0DP1VG1DZ", targets[1].Key);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-targets-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
