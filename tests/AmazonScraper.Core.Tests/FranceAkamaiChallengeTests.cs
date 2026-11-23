using System.Net;
using System.Text;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Tests;

public class FranceAkamaiChallengeTests
{
    [Fact]
    public async Task France_solves_akamai_interstitial_from_har()
    {
        var challenge = Fixtures.LoadText("fr-akamai-challenge.html");
        var requests = new List<string>();
        var postBody = string.Empty;

        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);

            if (request.Method == HttpMethod.Post)
            {
                postBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("{\"reload\": true}");
            }

            if (request.RequestUri!.AbsolutePath == "/-/fr/")
            {
                // First home request: the challenge. Second: normal home.
                return requests.Count == 1
                    ? Html(challenge)
                    : Html("<html>home</html>");
            }

            return Html("<html>search result</html>");
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            FranceMarketplace.Instance,
            new AmazonOptions { TimeoutSeconds = 5 },
            http);

        var html = await client.SearchAsync("ram 16 gb ddr5");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(new[]
        {
            "https://www.amazon.fr/-/fr/",
            "https://www.amazon.fr/_sec/verify?provider=interstitial",
            "https://www.amazon.fr/-/fr/",
            "https://www.amazon.fr/-/fr/s?k=ram%2016%20gb%20ddr5",
        }, requests);
        Assert.Contains("\"bm-verify\":\"AAQAAAAO", postBody);
        Assert.Contains("\"pow\":2410418766", postBody);
    }

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html"),
        };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
