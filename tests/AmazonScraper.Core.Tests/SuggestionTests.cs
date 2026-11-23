using System.Net;
using System.Text;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class SuggestionTests
{
    [Fact]
    public void Catalog_exposes_the_expected_parameters_for_each_marketplace()
    {
        var expected = new Dictionary<string, string>
        {
            ["it"] = "completion.amazon.it",
            ["com"] = "completion.amazon.com",
            ["fr"] = "completion.amazon.fr",
            ["uk"] = "completion.amazon.co.uk",
            ["de"] = "completion.amazon.de",
            ["jp"] = "completion.amazon.co.jp",
            ["es"] = "completion.amazon.es",
        };

        foreach (var (id, host) in expected)
        {
            Assert.True(SuggestionCatalog.TryGet(id, out var settings));
            Assert.Equal(host, settings.Host);
            Assert.False(string.IsNullOrWhiteSpace(settings.Mid));
            Assert.False(string.IsNullOrWhiteSpace(settings.PlainMid));
        }

        Assert.False(SuggestionCatalog.TryGet("ca", out _));
    }

    [Fact]
    public void Suggestion_url_encodes_the_prefix_and_uses_plain_mid()
    {
        var url = SuggestionCatalog
            .Get("it")
            .SuggestionsUrl("zaino e cuffie");
        var query = url.Query;

        Assert.Equal("completion.amazon.it", url.Host);
        Assert.Contains("prefix=zaino%20e%20cuffie", query);
        Assert.Contains("mid=A11IL2PNWYJU7H", query);
        Assert.Contains("plain-mid=35691", query);
        Assert.Contains("lop=it_IT", query);
        Assert.Contains("alias=aps", query);
    }

    [Fact]
    public async Task Client_parses_keyword_suggestions_and_filters_widgets_duplicates_and_exact_query()
    {
        Uri? requested = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "suggestions": [
                        { "type": "KEYWORD", "value": "zaino" },
                        { "type": "KEYWORD", "value": "zaino donna" },
                        { "type": "KEYWORD", "value": "zaino donna" },
                        { "type": "WIDGET", "value": "widget" },
                        { "type": "KEYWORD", "value": "zaino &amp; co" }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        }));
        var client = new AmazonSuggestionClient(http);

        var values = await client.GetAsync(ItalyMarketplace.Instance, "zaino");

        Assert.Equal(["zaino donna", "zaino & co"], values);
        Assert.NotNull(requested);
        Assert.Equal("completion.amazon.it", requested!.Host);
        Assert.Contains("plain-mid=35691", requested.Query);
    }

    [Fact]
    public async Task Client_returns_empty_for_invalid_json_and_short_prefixes()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
            };
        }));
        var client = new AmazonSuggestionClient(http);

        Assert.Empty(await client.GetAsync(ItalyMarketplace.Instance, "zaino"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Client_throws_for_a_failed_upstream_response()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = new AmazonSuggestionClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(ItalyMarketplace.Instance, "zaino"));
    }

    [Fact]
    public void Parse_handles_missing_or_malformed_suggestion_entries()
    {
        var values = AmazonSuggestionClient.Parse(
            """
            {
              "suggestions": [
                null,
                { "value": 42 },
                { "type": "KEYWORD", "value": "  cuffie  " },
                { "type": "KEYWORD", "value": "", "ghost": true },
                { "type": "KEYWORD", "value": "cuffie bluetooth" }
              ]
            }
            """,
            "cuff");

        Assert.Equal(["cuffie", "cuffie bluetooth"], values);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
