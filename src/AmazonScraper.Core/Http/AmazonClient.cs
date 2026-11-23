using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Http;

/// <summary>
/// HTTP client for one marketplace. It performs the home-page warm-up,
/// detects Akamai/captcha pages, solves the Akamai interstitial proof-of-work
/// and retries once, mirroring the Python client.
/// </summary>
public sealed partial class AmazonClient : IDisposable
{
    private readonly IMarketplaceProfile _marketplace;
    private readonly AmazonOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _warmUpLock = new(1, 1);
    private bool _warmedUp;
    private string _glowFlowId = string.Empty;
    private bool _glowAddressFormUnavailable;
    private bool _disposed;

    public AmazonClient(IMarketplaceProfile marketplace, AmazonOptions? options = null, HttpClient? httpClient = null)
    {
        _marketplace = marketplace;
        _options = options ?? new AmazonOptions();
        _http = httpClient ?? AmazonHttpClientFactory.Create(marketplace, _options);
    }

    public IMarketplaceProfile Marketplace => _marketplace;

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        SearchPageAsync(query, 1, cancellationToken);

    public Task<string> SearchPageAsync(string query, int page, CancellationToken cancellationToken = default) =>
        SearchPageAsync(query, page, null, cancellationToken);

    /// <summary>
    /// Loads a search page with an optional <c>p_n_deal_type</c> refinement.
    /// </summary>
    public async Task<string> SearchPageAsync(
        string query,
        int page,
        string? dealFilterId,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "The page must be greater than zero.");
        }

        var url = _marketplace.SearchUrl(query, page, dealFilterId);
        return await FetchAsync(
                url,
                navigation: _marketplace.SearchNavigationHeaders,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a search page with an Amazon sort token.
    /// </summary>
    public async Task<string> SearchPageSortedAsync(
        string query,
        int page,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "The page must be greater than zero.");
        }

        var url = _marketplace.SearchUrlWithSort(query, page, sort);
        return await FetchAsync(
                url,
                navigation: _marketplace.SearchNavigationHeaders,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the queryless deals page selected by the marketplace-specific
    /// <c>p_n_deal_type</c> filter.
    /// </summary>
    public async Task<string> DealsPageAsync(
        string dealFilterId,
        int page,
        string? categoryNodeId = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "The page must be greater than zero.");
        }

        var url = MarketplaceUrlBuilder.Deals(
            _marketplace,
            dealFilterId,
            page,
            categoryNodeId);
        return await FetchAsync(
                url,
                navigation: _marketplace.SearchNavigationHeaders,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> DetailAsync(string asin, CancellationToken cancellationToken = default)
    {
        var url = _marketplace.DetailUrl(asin);
        return await FetchAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the product page with a specific seller selected
    /// (<c>?smid=...&amp;psc=1</c>), the same URL used by buying options.
    /// </summary>
    public async Task<string> SellerDetailAsync(
        string asin,
        string sellerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sellerId))
        {
            throw new ArgumentException("The Seller ID cannot be empty.", nameof(sellerId));
        }

        var url = _marketplace.BuyingOptionDetailUrl(asin, sellerId);
        return await FetchAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the "all offers display" fragment used by the product page's
    /// "see all buying options" link. The request keeps the same cookie jar,
    /// so delivery location and marketplace settings are preserved.
    /// </summary>
    public Task<string> FetchBuyingOptionsAsync(
        string asin,
        CancellationToken cancellationToken = default) =>
        FetchBuyingOptionsPageAsync(asin, 1, cancellationToken);

    /// <summary>
    /// Loads one AOD page. Page 1 is the initial fragment; pages 2+ use
    /// <c>ref=aod_page_N</c> plus <c>pageno</c> and
    /// <c>isonlyrenderofferlist=true</c>, as the AOD frontend does.
    /// </summary>
    public async Task<string> FetchBuyingOptionsPageAsync(
        string asin,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(asin))
        {
            throw new ArgumentException("The ASIN cannot be empty.", nameof(asin));
        }
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "The AOD page must be greater than zero.");
        }

        await WarmUpAsync(cancellationToken).ConfigureAwait(false);

        var referer = _marketplace.ProductUrl(asin);
        var url = _marketplace.BuyingOptionsUrl(asin, page);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,*/*");
            request.Headers.Referrer = new Uri(referer);

            var html = await CheckResponseAsync(request, referer, cancellationToken)
                .ConfigureAwait(false);
            if (html is not null)
            {
                return html;
            }
        }

        ReportHttpBlock("unresolved Akamai challenge");
        throw new AmazonBlockedException("unresolved Akamai challenge");
    }

    /// <summary>
    /// Sets the delivery location using Amazon's GLOW "address-change" flow.
    /// The seed page provides the validation token; the modal provides the
    /// CSRF token used by the POST. The same HttpClient cookie jar keeps the
    /// location for all subsequent search/detail requests.
    /// </summary>
    public async Task<bool> SetDeliveryLocationAsync(
        string seedUrl,
        string postalCode,
        string? countryCode = null,
        CancellationToken cancellationToken = default)
    {
        var countryOnly = string.IsNullOrWhiteSpace(postalCode);
        if (countryOnly)
        {
            if (!_marketplace.SupportsCountryOnlyDelivery)
            {
                throw new ArgumentException(
                    "The postal code cannot be empty for this marketplace.",
                    nameof(postalCode));
            }
            if (string.IsNullOrWhiteSpace(countryCode)
                || !PostalCodeValidator.IsCountryCode(countryCode))
            {
                throw new ArgumentException(
                    "A two-letter country code is required for a country-only " +
                    "delivery location.",
                    nameof(countryCode));
            }
            countryCode = countryCode.Trim().ToUpperInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(countryCode))
        {
            countryCode = countryCode.Trim().ToUpperInvariant();
        }

        _glowFlowId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        _glowAddressFormUnavailable = false;

        if (_marketplace.GlowPreflightEnabled)
        {
            await EnsureGlowPreflightAsync(cancellationToken).ConfigureAwait(false);
            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);
        }

        var seedHtml = await FetchAsync(seedUrl, cancellationToken).ConfigureAwait(false);
        var glowToken = AmazonPatterns.GlowToken.Match(seedHtml);
        if (!glowToken.Success)
        {
            return false;
        }

        if (countryOnly && _marketplace.Id == "com"
            && await TryExportsGatewayGlowAsync(
                glowToken.Groups[1].Value,
                countryCode!,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (await TryModernGlowAsync(
                seedUrl,
                seedHtml,
                glowToken.Groups[1].Value,
                postalCode,
                countryCode,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (!countryOnly)
        {
            var legacyApplied = await TryLegacyGlowAsync(
                seedUrl,
                glowToken.Groups[1].Value,
                postalCode,
                countryCode,
                cancellationToken).ConfigureAwait(false);
            if (legacyApplied)
            {
                return true;
            }
        }

        var gatewayApplied = await TryGatewayGlowAsync(
            seedUrl,
            glowToken.Groups[1].Value,
            postalCode,
            countryCode,
            cancellationToken).ConfigureAwait(false);
        if (gatewayApplied)
        {
            return true;
        }
        if (_glowAddressFormUnavailable)
        {
            var destination = countryOnly
                ? countryCode
                : $"{postalCode} ({countryCode})";
            throw new DeliveryLocationRejectedException(
                "Amazon did not provide the location form for this session " +
                $"(delivery location {destination} on {_marketplace.DisplayName}). " +
                "Use the automatic location or try again later.");
        }
        return false;
    }

    private async Task<bool> TryExportsGatewayGlowAsync(
        string glowToken,
        string countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var referer = _marketplace.BaseUri.ToString();
            var modalUrl = new Uri(
                _marketplace.BaseUri,
                "portal-migration/hz/glow/get-rendered-address-selections" +
                "?deviceType=desktop&pageType=exports-gateway-desktop" +
                "&storeContext=NoStoreName&actionSource=desktop-modal").AbsoluteUri;

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);
            var modalHtml = await FetchGlowModalAsync(
                referer, modalUrl, glowToken, cancellationToken).ConfigureAwait(false);
            if (modalHtml is null)
            {
                return false;
            }

            var csrfToken = AmazonPatterns.GlowCsrfToken.Match(modalHtml);
            if (!csrfToken.Success)
            {
                _glowAddressFormUnavailable = true;
                return false;
            }

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);

            var payload = BuildGlowPayload(
                "exports-gateway-desktop",
                "generic",
                string.Empty,
                countryCode);
            return await PostGlowAddressChangeAsync(
                referer,
                csrfToken.Groups[1].Value,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> TryModernGlowAsync(
        string seedUrl,
        string seedHtml,
        string glowToken,
        string postalCode,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var modalPath = ExtractGlowModalUrl(seedHtml);
            if (modalPath is null)
            {
                return false;
            }

            var modalUrl = new Uri(_marketplace.BaseUri, modalPath).AbsoluteUri;
            var pageType = ExtractQueryParameter(modalUrl, "pageType");
            var storeContext = ExtractQueryParameter(modalUrl, "storeContext");
            if (string.IsNullOrEmpty(pageType))
            {
                pageType = "Gateway";
            }
            if (string.IsNullOrEmpty(storeContext))
            {
                storeContext = "generic";
            }

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);
            var modalHtml = await FetchGlowModalAsync(
                seedUrl, modalUrl, glowToken, cancellationToken).ConfigureAwait(false);
            if (modalHtml is null)
            {
                return false;
            }

            var csrfToken = AmazonPatterns.GlowCsrfToken.Match(modalHtml);
            if (!csrfToken.Success)
            {
                _glowAddressFormUnavailable = true;
                return false;
            }

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);

            var payload = BuildGlowPayload(
                pageType,
                storeContext,
                postalCode,
                countryCode);
            return await PostGlowAddressChangeAsync(
                seedUrl,
                csrfToken.Groups[1].Value,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> TryGatewayGlowAsync(
        string seedUrl,
        string glowToken,
        string postalCode,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var gatewayPageType = _marketplace.GlowGatewayPageType;
            var modalUrl = new Uri(
                _marketplace.BaseUri,
                "portal-migration/hz/glow/get-rendered-address-selections" +
                $"?deviceType=desktop&pageType={gatewayPageType}&storeContext=NoStoreName" +
                "&actionSource=desktop-modal").AbsoluteUri;

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);
            var modalHtml = await FetchGlowModalAsync(
                seedUrl, modalUrl, glowToken, cancellationToken).ConfigureAwait(false);
            if (modalHtml is null)
            {
                return false;
            }

            var csrfToken = AmazonPatterns.GlowCsrfToken.Match(modalHtml);
            if (!csrfToken.Success)
            {
                _glowAddressFormUnavailable = true;
                return false;
            }

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);

            var payload = BuildGlowPayload(
                gatewayPageType,
                "generic",
                postalCode,
                countryCode);
            return await PostGlowAddressChangeAsync(
                seedUrl,
                csrfToken.Groups[1].Value,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> TryLegacyGlowAsync(
        string seedUrl,
        string glowToken,
        string postalCode,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var modalUrl = new Uri(
                _marketplace.BaseUri,
                "portal-migration/hz/glow/get-rendered-address-selections" +
                "?deviceType=desktop&pageType=Gateway&storeContext=generic&actionSource=desktop-modal")
                .AbsoluteUri;

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);
            var modalHtml = await FetchGlowModalAsync(
                seedUrl, modalUrl, glowToken, cancellationToken).ConfigureAwait(false);
            if (modalHtml is null)
            {
                return false;
            }

            var csrfToken = AmazonPatterns.GlowCsrfToken.Match(modalHtml);
            if (!csrfToken.Success)
            {
                _glowAddressFormUnavailable = true;
                return false;
            }

            await DelayGlowStepAsync(cancellationToken).ConfigureAwait(false);

            var form = new Dictionary<string, string>
            {
                ["locationType"] = "LOCATION_INPUT",
                ["zipCode"] = postalCode,
                ["storeContext"] = "generic",
                ["deviceType"] = "web",
                ["pageType"] = "Gateway",
                ["actionSource"] = "glow",
                ["almBrandId"] = "undefined",
            };
            if (IsCrossBorder(countryCode))
            {
                form["countryCode"] = countryCode!.ToUpperInvariant();
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                AddressChangeUrl())
            {
                Content = new FormUrlEncodedContent(form),
            };
            request.Headers.TryAddWithoutValidation("anti-csrftoken-a2z", csrfToken.Groups[1].Value);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/json, text/javascript, */*; q=0.01");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            AddGlowHeaders(request, seedUrl);

            using var response = await _http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return await IsSuccessfulAddressResponseAsync(
                response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Dictionary<string, object?> BuildGlowPayload(
        string pageType,
        string storeContext,
        string postalCode,
        string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            var code = countryCode!.ToUpperInvariant();
            return new Dictionary<string, object?>
            {
                ["locationType"] = "COUNTRY",
                ["district"] = code,
                ["countryCode"] = code,
                ["deviceType"] = "web",
                ["storeContext"] = storeContext,
                ["pageType"] = pageType,
                ["actionSource"] = "glow",
            };
        }

        var payload = new Dictionary<string, object?>
        {
            ["locationType"] = "LOCATION_INPUT",
            ["zipCode"] = postalCode,
            ["deviceType"] = "web",
            ["storeContext"] = storeContext,
            ["pageType"] = pageType,
            ["actionSource"] = "glow",
        };
        if (IsCrossBorder(countryCode))
        {
            payload["countryCode"] = countryCode!.ToUpperInvariant();
        }
        return payload;
    }

    private async Task<bool> PostGlowAddressChangeAsync(
        string seedUrl,
        string csrfToken,
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            AddressChangeUrl())
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(
            "anti-csrftoken-a2z",
            csrfToken);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,*/*");
        request.Headers.TryAddWithoutValidation(
            "X-Requested-With",
            "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation(
            "Origin",
            _marketplace.BaseUri.GetLeftPart(UriPartial.Authority));
        AddGlowHeaders(request, seedUrl);

        using var response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await IsSuccessfulAddressResponseAsync(
            response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> FetchGlowModalAsync(
        string seedUrl,
        string modalUrl,
        string glowToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, modalUrl);
        request.Headers.TryAddWithoutValidation("anti-csrftoken-a2z", glowToken);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,*/*");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        AddGlowHeaders(request, seedUrl);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddGlowHeaders(HttpRequestMessage request, string seedUrl)
    {
        request.Headers.TryAddWithoutValidation(
            "x-amzn-flow-closure-id",
            _glowFlowId);
        request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Priority", "u=0");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.Referrer = new Uri(seedUrl);
    }

    private string AddressChangeUrl() =>
        new Uri(
            _marketplace.BaseUri,
            "portal-migration/hz/glow/address-change?actionSource=glow").ToString();

    private bool IsCrossBorder(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode)
        && !string.Equals(countryCode, _marketplace.CountryCode, StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsSuccessfulAddressResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var validAddress = root.TryGetProperty("isValidAddress", out var valid)
                && valid.ValueKind == JsonValueKind.Number
                && valid.GetInt32() == 1;
            var successful = root.TryGetProperty("successful", out var success)
                && success.ValueKind == JsonValueKind.Number
                && success.GetInt32() == 1;
            return validAddress || successful;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsBlocked(string html) =>
        AmazonPatterns.BlockMarkers.Any(marker => html.Contains(marker, StringComparison.Ordinal));

    public static bool IsChallenge(string html) =>
        html.Contains("bm-verify", StringComparison.Ordinal)
        && html.Contains("/_sec/verify", StringComparison.Ordinal);

    public static bool IsWafChallenge(string html) =>
        html.Contains("awsWafCookieDomainList", StringComparison.Ordinal)
        || html.Contains("gokuProps", StringComparison.Ordinal)
        || html.Contains("challenge.js", StringComparison.Ordinal);

    private async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (!_marketplace.WarmUpEnabled || _warmedUp)
        {
            return;
        }

        await _warmUpLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_warmedUp)
            {
                return;
            }

            try
            {
                var url = _marketplace.WarmUpUrl;
                var html = await CheckResponseAsync(
                        url,
                        _marketplace.WarmUpNavigationHeaders,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (html is null)
                {
                    html = await CheckResponseAsync(
                            url,
                            _marketplace.WarmUpNavigationHeaders,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (html is null)
                    {
                        throw new AmazonBlockedException("unresolved Akamai challenge");
                    }
                }
            }
            catch (Exception ex) when (ex is AmazonBlockedException or AmazonNotFoundException)
            {
                // The warm-up is only an optimization: amazon.it serves an
                // AWS WAF interstitial (HTTP 202 + challenge.js) on the home
                // page even when the search endpoint is reachable. Do not let
                // that block the real request; if the target page is also
                // challenged, FetchAsync will raise the error itself.
            }

            _warmedUp = true;
        }
        finally
        {
            _warmUpLock.Release();
        }
    }

    private async Task<string> FetchAsync(string url, CancellationToken cancellationToken) =>
        await FetchAsync(url, navigation: false, cancellationToken).ConfigureAwait(false);

    private async Task<string> FetchAsync(
        string url,
        bool navigation,
        CancellationToken cancellationToken)
    {
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var html = await CheckResponseAsync(
                    url,
                    navigation,
                    cancellationToken)
                .ConfigureAwait(false);
            if (html is not null)
            {
                return html;
            }
        }

        ReportHttpBlock("unresolved Akamai challenge");
        throw new AmazonBlockedException("unresolved Akamai challenge");
    }

    private Task<string?> CheckResponseAsync(
        string url,
        CancellationToken cancellationToken) =>
        CheckResponseAsync(url, navigation: false, cancellationToken);

    private async Task<string?> CheckResponseAsync(
        string url,
        bool navigation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (navigation)
        {
            AddNavigationHeaders(request);
        }

        return await CheckResponseAsync(request, url, cancellationToken)
            .ConfigureAwait(false);
    }

    private void AddNavigationHeaders(HttpRequestMessage request)
    {
        request.Headers.Referrer = _marketplace.BaseUri;
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
        request.Headers.TryAddWithoutValidation("Priority", "u=0, i");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
    }

    private async Task<string?> CheckResponseAsync(
        HttpRequestMessage request,
        string referer,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? referer;
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            ReportHttpBlock($"HTTP {(int)response.StatusCode}");
            throw new AmazonBlockedException($"HTTP {(int)response.StatusCode}");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new AmazonNotFoundException(url);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (IsWafChallenge(html))
        {
            ReportHttpBlock("challenge AWS WAF");
            throw new AmazonBlockedException("challenge AWS WAF");
        }

        if (IsChallenge(html))
        {
            if (await SolveChallengeAsync(html, url, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            ReportHttpBlock("unresolved Akamai challenge");
            throw new AmazonBlockedException("unresolved Akamai challenge");
        }

        if (IsBlocked(html))
        {
            ReportHttpBlock("anti-bot block");
            throw new AmazonBlockedException();
        }

        return html;
    }

    private void ReportHttpBlock(string reason)
    {
        if (_options.Transport != AmazonTransport.Http)
        {
            return;
        }

        try
        {
            _options.OnHttpBlocked?.Invoke(reason);
        }
        catch (Exception)
        {
            // Persisting the cooldown must never hide the real block.
        }
    }

    private async Task<bool> SolveChallengeAsync(string html, string referer, CancellationToken cancellationToken)
    {
        var powMatch = AkamaiPow().Match(html);
        var tokenMatch = AkamaiToken().Match(html);
        if (!powMatch.Success || !tokenMatch.Success)
        {
            return false;
        }

        var baseValue = long.Parse(powMatch.Groups[1].Value);
        var suffix = long.Parse(powMatch.Groups[2].Value + powMatch.Groups[3].Value);
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["bm-verify"] = tokenMatch.Groups[1].Value,
            ["pow"] = baseValue + suffix,
        });

        var verifyUrl = new Uri(_marketplace.BaseUri, "_sec/verify?provider=interstitial").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", _marketplace.AcceptLanguage);
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
        request.Headers.Referrer = new Uri(referer);
        request.Headers.TryAddWithoutValidation("Origin", _marketplace.BaseUri.GetLeftPart(UriPartial.Authority));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return response.StatusCode == System.Net.HttpStatusCode.OK
            && body.Contains("\"reload\"", StringComparison.Ordinal);
    }

    private async Task DelayGlowStepAsync(CancellationToken cancellationToken)
    {
        if (_options.GlowStepDelayMs > 0)
        {
            await Task.Delay(_options.GlowStepDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureGlowPreflightAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .GetAsync(
                    _marketplace.WarmUpUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            // Cookies are stored in the shared jar; the status/body are
            // irrelevant for the preflight.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Best effort: the seed/modal flow can still work.
        }
    }

    private static string? ExtractGlowModalUrl(string html)
    {
        var index = html.IndexOf("glow-modal", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var window = html.Substring(index, Math.Min(2000, html.Length - index));
        var decoded = WebUtility.HtmlDecode(window);
        foreach (Match match in Regex.Matches(decoded, "\"url\"\\s*:\\s*\"([^\"]+)\""))
        {
            var candidate = match.Groups[1].Value;
            if (candidate.Contains("get-rendered-address-selections", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ExtractQueryParameter(string url, string name)
    {
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return string.Empty;
        }

        foreach (var part in url[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (part[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(part[(separator + 1)..]);
            }
        }

        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
        _warmUpLock.Dispose();
    }

    [GeneratedRegex(@"var i = (\d+); var j = i \+ Number\(""(\d+)"" \+ ""(\d+)""\);")]
    private static partial Regex AkamaiPow();

    [GeneratedRegex(@"xhr\.send\(JSON\.stringify\(\{""bm-verify"": ""([^""]+)""")]
    private static partial Regex AkamaiToken();
}
