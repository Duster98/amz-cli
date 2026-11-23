using System.Net;
using System.Text;
using AmazonScraper.Cli.Services;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Tests;

public class SuggestionServiceTests
{
    [Fact]
    public async Task Cache_reuses_results_until_the_ttl_expires()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(
                JsonResponse("""{"suggestions":[{"type":"KEYWORD","value":"zaino donna"}]}"""));
        }));
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var service = new SuggestionService(
            new AmazonSuggestionClient(http),
            time,
            TimeSpan.FromMinutes(10));

        var first = await service.GetAsync(ItalyMarketplace.Instance, "zain");
        var second = await service.GetAsync(ItalyMarketplace.Instance, "ZAIN");

        Assert.Equal(["zaino donna"], first);
        Assert.Equal(first, second);
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromMinutes(9));
        _ = await service.GetAsync(ItalyMarketplace.Instance, "zain");
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromMinutes(2));
        _ = await service.GetAsync(ItalyMarketplace.Instance, "zain");
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_prefix_share_one_call()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(40);
            return JsonResponse("""{"suggestions":[{"type":"KEYWORD","value":"zaino"}]}""");
        }));
        var service = new SuggestionService(
            new AmazonSuggestionClient(http),
            TimeProvider.System);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ =>
                service.GetAsync(ItalyMarketplace.Instance, "zain")));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Single(result));
    }

    [Fact]
    public async Task A_failure_is_not_cached_and_has_a_short_cooldown()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            throw new HttpRequestException("transient");
        }));
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var service = new SuggestionService(
            new AmazonSuggestionClient(http),
            time);

        Assert.Empty(await service.GetAsync(ItalyMarketplace.Instance, "zain"));
        Assert.Empty(await service.GetAsync(ItalyMarketplace.Instance, "zain"));
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromSeconds(21));
        Assert.Empty(await service.GetAsync(ItalyMarketplace.Instance, "zain"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Minimum_prefix_length_is_two_except_for_japanese()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(
                JsonResponse("""{"suggestions":[{"type":"KEYWORD","value":"リュック"}]}"""));
        }));
        var service = new SuggestionService(new AmazonSuggestionClient(http));

        Assert.Empty(await service.GetAsync(ItalyMarketplace.Instance, "z"));
        Assert.Equal(0, calls);

        Assert.Equal(
            ["リュック"],
            await service.GetAsync(JapanMarketplace.Instance, "リ"));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta)
        {
            _now += delta;
        }
    }
}
