using System.Net;
using AmazonScraper.Core.Marketplaces;
using CurlImpersonate.Http;

namespace AmazonScraper.Core.Http;

public static class AmazonHttpClientFactory
{
    public static HttpClient Create(IMarketplaceProfile marketplace, AmazonOptions options)
    {
        var cookies = new CookieContainer();
        var useExecutable = UsesCurlExecutable(
            OperatingSystem.IsWindows(),
            options.Transport);
        var useUkCookieJar = OperatingSystem.IsWindows()
            && marketplace.Id == "uk";
        var useCookieContainer = !useExecutable && !useUkCookieJar;
        HttpMessageHandler handler;

        if (useExecutable)
        {
            // On Unix the curl transport uses the system curl-impersonate
            // executable, so it can be updated with the package manager.
            // The HTTP transport deliberately uses SocketsHttpHandler instead:
            // forcing curl-impersonate for .com on Linux makes Amazon return a
            // contentless "product summary" shell page.
            handler = new UkCurlHandler(
                new UkCookieJar(
                    marketplace.BaseUri,
                    marketplace.DefaultCookieHeader,
                    options.CookieHeader),
                options);
        }
        else if (useUkCookieJar)
        {
            var jar = new UkCookieJar(
                marketplace.BaseUri,
                marketplace.DefaultCookieHeader,
                options.CookieHeader);
            var curl = CreateCurlHandler(options, cookies, useCookies: false);
            handler = new CurlCookieHandler(jar)
            {
                InnerHandler = curl,
            };
        }
        else if (options.Transport == AmazonTransport.Curl)
        {
            try
            {
                handler = CreateCurlHandler(options, cookies, useCookieContainer);
            }
            catch (Exception)
            {
                // Native curl unavailable (for example an unsupported Linux
                // RID): degrade to HttpClient. `amz-cli self-test` still
                // reports the missing transport explicitly.
                handler = CreateSocketsHandler(cookies, useCookieContainer);
            }
        }
        else
        {
            handler = CreateSocketsHandler(cookies, useCookieContainer);
        }

        if (useCookieContainer
            && (!string.IsNullOrWhiteSpace(options.CookieHeader)
                || !string.IsNullOrWhiteSpace(marketplace.DefaultCookieHeader)))
        {
            var cookieHeader = string.Join(
                "; ",
                new[] { marketplace.DefaultCookieHeader, options.CookieHeader }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            cookies.SetCookies(marketplace.BaseUri, cookieHeader);
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)),
        };

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                options.UserAgent);
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", options.Accept);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", marketplace.AcceptLanguage);
        return client;
    }

    internal static bool UsesCurlExecutable(
        bool isWindows,
        AmazonTransport transport) =>
        !isWindows && transport == AmazonTransport.Curl;

    private static CurlHandler CreateCurlHandler(
        AmazonOptions options,
        CookieContainer cookies,
        bool useCookies)
    {
        var handlerOptions = new CurlHandlerOptions
        {
            BrowserProfile = options.BrowserProfile,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)),
            UseCookies = useCookies,
            CookieContainer = cookies,
            UseBrowserHeaders = options.UseBrowserHeaders,
            FollowRedirects = true,
            MaxRedirects = 10,
            AutomaticDecompression = true,
        };

        return new CurlHandler(handlerOptions);
    }

    private static SocketsHttpHandler CreateSocketsHandler(
        CookieContainer cookies,
        bool useCookies)
    {
        return new SocketsHttpHandler
        {
            UseCookies = useCookies,
            CookieContainer = cookies,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
    }
}
