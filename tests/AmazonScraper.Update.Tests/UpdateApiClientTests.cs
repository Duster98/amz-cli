using System.Net;
using System.Text;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class UpdateApiClientTests
{
    private const string BaseUrl =
        "https://github.com/Duster98/amz-cli/releases/latest/download/";

    [Fact]
    public async Task Reads_the_latest_release_from_the_github_feed()
    {
        var (manifest, signature, _) = TestRelease.Create();
        var releaseId = Guid.NewGuid();
        var body = $$"""
            {
              "schema": 1,
              "release_id": "{{releaseId}}",
              "version": "2.0.0",
              "platform": "linux-x64",
              "channel": "stable",
              "manifest": {{System.Text.Json.JsonSerializer.Serialize(manifest)}},
              "signature": "{{signature}}",
              "signing_key_id": "test-key",
              "created_at": "2026-11-16T18:00:00Z",
              "download_urls": {
                "stable/linux-x64/2.0.0/amazon.gz": "https://github.com/Duster98/amz-cli/releases/download/v2.0.0/amazon.gz"
              }
            }
            """;
        using var client = CreateClient(body);

        var result = await client.GetLatestAsync("linux-x64", "stable", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(releaseId, result.ReleaseId);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal(manifest, result.Manifest);
        Assert.Equal(
            "https://github.com/Duster98/amz-cli/releases/download/v2.0.0/amazon.gz",
            result.DownloadUrls["stable/linux-x64/2.0.0/amazon.gz"]);
    }

    [Fact]
    public async Task Returns_null_when_the_feed_is_missing()
    {
        using var client = CreateClient(
            "{\"message\":\"Not Found\"}",
            HttpStatusCode.NotFound);

        var result = await client.GetLatestAsync("linux-x64", "stable", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Rejects_http_errors()
    {
        using var client = CreateClient(
            "{\"message\":\"denied\"}",
            HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<UpdateApiException>(() =>
            client.GetLatestAsync("linux-x64", "stable", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_non_github_download_url()
    {
        var (manifest, signature, _) = TestRelease.Create();
        var releaseId = Guid.NewGuid();
        var body = $$"""
            {
              "schema": 1,
              "release_id": "{{releaseId}}",
              "version": "2.0.0",
              "platform": "linux-x64",
              "channel": "stable",
              "manifest": {{System.Text.Json.JsonSerializer.Serialize(manifest)}},
              "signature": "{{signature}}",
              "signing_key_id": "test-key",
              "download_urls": {
                "stable/linux-x64/2.0.0/amazon.gz": "https://evil.example.com/amazon.gz"
              }
            }
            """;
        using var client = CreateClient(body);

        await Assert.ThrowsAsync<UpdateApiException>(() =>
            client.GetLatestAsync("linux-x64", "stable", CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_a_feed_for_another_platform()
    {
        var (manifest, signature, _) = TestRelease.Create();
        var releaseId = Guid.NewGuid();
        var body = $$"""
            {
              "schema": 1,
              "release_id": "{{releaseId}}",
              "version": "2.0.0",
              "platform": "win-x64",
              "channel": "stable",
              "manifest": {{System.Text.Json.JsonSerializer.Serialize(manifest)}},
              "signature": "{{signature}}",
              "signing_key_id": "test-key",
              "download_urls": {
                "stable/win-x64/2.0.0/amz-cli.exe.gz": "https://github.com/Duster98/amz-cli/releases/download/v2.0.0/amz-cli.exe.gz"
              }
            }
            """;
        using var client = CreateClient(body);

        await Assert.ThrowsAsync<UpdateApiException>(() =>
            client.GetLatestAsync("linux-x64", "stable", CancellationToken.None));
    }

    private static UpdateApiClient CreateClient(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(
            new HttpClient(new StubHandler(body, status)),
            new Uri(BaseUrl));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
