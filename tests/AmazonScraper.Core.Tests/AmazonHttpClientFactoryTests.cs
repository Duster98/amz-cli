using AmazonScraper.Core.Http;

namespace AmazonScraper.Core.Tests;

public class AmazonHttpClientFactoryTests
{
    [Theory]
    [InlineData(false, AmazonTransport.Curl, true)]
    [InlineData(false, AmazonTransport.Http, false)]
    [InlineData(true, AmazonTransport.Curl, false)]
    [InlineData(true, AmazonTransport.Http, false)]
    public void Curl_executable_is_used_only_on_unix_curl_transport(
        bool isWindows,
        AmazonTransport transport,
        bool expected)
    {
        Assert.Equal(
            expected,
            AmazonHttpClientFactory.UsesCurlExecutable(isWindows, transport));
    }
}
