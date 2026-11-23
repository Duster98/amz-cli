using System.Net;
using System.Text;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class SearchResultEnricherTests
{
    [Fact]
    public async Task Enrich_fills_pricing_from_detail_page()
    {
        var detailHtml = Fixtures.Load("detail.html.gz");
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(detailHtml, Encoding.UTF8, "text/html"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http },
            http);
        var result = new SearchResult
        {
            Asin = "B0DTJDR3V9",
            Title = "Prodotto",
        };

        var enriched = await SearchResultEnricher.EnrichAsync(
            [result],
            ItalyMarketplace.Instance,
            client,
            delayMs: 0);

        Assert.Empty(enriched.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(result.Price));
        Assert.False(string.IsNullOrWhiteSpace(result.OriginalPrice));
        Assert.False(string.IsNullOrWhiteSpace(result.Discount));
    }

    [Fact]
    public async Task Uses_the_shared_detail_loader_when_provided()
    {
        var calls = 0;
        var detail = new ProductDetail
        {
            Asin = "B0C8ZS7N73",
            Title = "Prodotto",
            Price = "480,00 €",
            BuyingOptions = new BuyingOptionsSummary
            {
                Count = 1,
                LowestPrice = "480,00 €",
                Condition = "D'occasion: Très bon",
                Options =
                [
                    new BuyingOption
                    {
                        Price = "480,00 €",
                        Condition = "D'occasion: Très bon",
                    },
                ],
            },
        };
        using var http = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("Network non atteso")));
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http },
            http);
        var result = new SearchResult
        {
            Asin = "B0C8ZS7N73",
            Title = "Prodotto",
        };

        var enriched = await SearchResultEnricher.EnrichAsync(
            [result],
            ItalyMarketplace.Instance,
            client,
            delayMs: 0,
            detailLoader: (_, _) =>
            {
                calls++;
                return Task.FromResult(detail);
            });

        Assert.Empty(enriched.Warnings);
        Assert.Equal(1, calls);
        Assert.Equal("480,00 €", result.Price);
        Assert.Equal("D'occasion: Très bon", result.Condition);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
