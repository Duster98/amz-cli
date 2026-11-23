using System.Net;

namespace AmazonScraper.Core.Http;

/// <summary>
/// Cookie jar used by the curl-impersonate executable and by Amazon.co.uk on
/// Windows, where CurlHandler does not reliably carry warm-up session cookies.
/// Other Windows marketplaces keep using CookieContainer.
/// </summary>
internal sealed class UkCookieJar
{
    // Amazon sends delete directives for cookies on ".www.amazon.it" while the
    // real session cookies live on ".amazon.it". Keeping both scopes separate
    // is essential: a flat name/value map would overwrite the valid session
    // with an expired value and Amazon would return a reduced GLOW dialog.
    private readonly CookieContainer _cookies = new();
    private readonly Uri _baseUri;
    private readonly object _gate = new();

    public UkCookieJar(
        Uri baseUri,
        string? defaultCookieHeader,
        string? optionCookieHeader)
    {
        _baseUri = baseUri;
        Seed(defaultCookieHeader);
        Seed(optionCookieHeader);
    }

    public void Seed(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return;
        }

        var domain = CookieDomain(_baseUri);
        lock (_gate)
        {
            foreach (var part in header.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = part[..separator].Trim();
                var value = part[(separator + 1)..].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                try
                {
                    _cookies.SetCookies(
                        _baseUri,
                        $"{name}={value}; Domain={domain}; Path=/");
                }
                catch (CookieException)
                {
                    // Ignore malformed cookies from a user-supplied header.
                }
            }
        }
    }

    public void Apply(HttpRequestMessage request)
    {
        lock (_gate)
        {
            var uri = request.RequestUri ?? _baseUri;
            var header = _cookies.GetCookieHeader(uri);
            request.Headers.Remove("Cookie");
            if (!string.IsNullOrWhiteSpace(header))
            {
                request.Headers.TryAddWithoutValidation("Cookie", header);
            }
        }
    }

    public void Capture(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        var uri = response.RequestMessage?.RequestUri ?? _baseUri;
        lock (_gate)
        {
            foreach (var header in values)
            {
                try
                {
                    _cookies.SetCookies(uri, header);
                }
                catch (CookieException)
                {
                    // Ignore malformed Set-Cookie directives instead of
                    // aborting the whole response.
                }
            }
        }
    }

    private static string CookieDomain(Uri baseUri)
    {
        var host = baseUri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return "." + host;
    }
}

internal sealed class CurlCookieHandler(UkCookieJar jar) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        jar.Apply(request);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        jar.Capture(response);
        return response;
    }
}
