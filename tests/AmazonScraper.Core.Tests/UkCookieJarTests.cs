using System.Net;
using AmazonScraper.Core.Http;

namespace AmazonScraper.Core.Tests;

public class UkCookieJarTests
{
    private static readonly Uri BaseUri = new("https://www.amazon.co.uk/");

    [Fact]
    public void Apply_writes_all_cookies_in_the_header()
    {
        var jar = new UkCookieJar(
            BaseUri,
            "i18n-prefs=GBP; lc-acbuk=en_GB",
            null);
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUri);

        jar.Apply(request);

        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("i18n-prefs=GBP", header);
        Assert.Contains("lc-acbuk=en_GB", header);
    }

    [Fact]
    public void Capture_stores_set_cookie_values()
    {
        var jar = new UkCookieJar(BaseUri, null, null);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, BaseUri),
        };
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "session-id=123; Domain=.amazon.co.uk; Path=/; Secure; HttpOnly");
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "session-id-time=456; Domain=.amazon.co.uk; Path=/; Secure");

        jar.Capture(response);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUri);
        jar.Apply(request);
        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("session-id=123", header);
        Assert.Contains("session-id-time=456", header);
    }

    [Fact]
    public void Expired_cookie_for_www_domain_does_not_delete_valid_domain_cookie()
    {
        var jar = new UkCookieJar(
            BaseUri,
            "session-id=VALID; session-token=OLD; ubid-acbuk=UBID",
            null);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("https://www.amazon.co.uk/dp/B0BY2R4BHW")),
        };
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "session-id=-; path=/; domain=.www.amazon.co.uk; " +
            "expires=Wed, 19-Nov-2014 19:46:48 GMT");
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "session-token=NEW; Domain=.amazon.co.uk; " +
            "Expires=Fri, 19-Nov-2027 19:46:48 GMT; Path=/; Secure");
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "ubid-acbuk=-; path=/; domain=.www.amazon.co.uk; " +
            "expires=Wed, 19-Nov-2014 19:46:48 GMT");

        jar.Capture(response);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("https://www.amazon.co.uk/dp/B0BY2R4BHW"));
        jar.Apply(request);
        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("session-id=VALID", header);
        Assert.Contains("session-token=NEW", header);
        Assert.Contains("ubid-acbuk=UBID", header);
    }

    [Fact]
    public void Option_cookies_override_defaults()
    {
        var jar = new UkCookieJar(
            BaseUri,
            "i18n-prefs=GBP; lc-acbuk=en_GB",
            "i18n-prefs=EUR; aws-waf-token=TOKEN");
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUri);

        jar.Apply(request);

        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("i18n-prefs=EUR", header);
        Assert.DoesNotContain("i18n-prefs=GBP", header);
        Assert.Contains("aws-waf-token=TOKEN", header);
        Assert.Contains("lc-acbuk=en_GB", header);
    }
}
