namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// A fully implemented Amazon marketplace. A new domain is added by
/// implementing this interface, registering it in
/// <see cref="MarketplaceRegistry"/> and adding parser fixtures.
/// </summary>
public interface IMarketplaceProfile
{
    string Id { get; }
    string DisplayName { get; }
    string CountryCode { get; }

    /// <summary>
    /// ISO 4217 currency used by the marketplace. Prices are never converted:
    /// this is only metadata for monitoring and grouped totals.
    /// </summary>
    string CurrencyCode => Id switch
    {
        "uk" => "GBP",
        "com" => "USD",
        "jp" => "JPY",
        _ => "EUR",
    };

    Uri BaseUri { get; }
    string AcceptLanguage { get; }
    IMarketplacePatterns Patterns { get; }
    IMarketplaceFormat Format { get; }

    /// <summary>Localized search path without the query string.</summary>
    string SearchPath => "s";

    string SearchUrl(string query, int page = 1);

    /// <summary>
    /// Adds browser navigation headers (Referer, Sec-Fetch-*, Pragma, TE) to
    /// search requests. Enabled only for Amazon.co.uk, whose anti-bot layer is
    /// stricter; other marketplaces keep their previous request shape.
    /// </summary>
    bool SearchNavigationHeaders => false;

    /// <summary>
    /// Search URL with an optional Amazon <c>p_n_deal_type</c> refinement.
    /// The filter id is marketplace-specific and comes from
    /// <see cref="AmazonScraper.Core.Searching.DealFilterCatalog"/>.
    /// </summary>
    string SearchUrl(string query, int page, string? dealFilterId)
    {
        var url = SearchUrl(query, page);
        if (string.IsNullOrWhiteSpace(dealFilterId))
        {
            return url;
        }

        return $"{url}&rh=p_n_deal_type%3A{Uri.EscapeDataString(dealFilterId)}";
    }

    /// <summary>
    /// Search URL with an Amazon sort token (for example
    /// <c>price-asc-rank</c> or <c>review-rank</c>).
    /// </summary>
    string SearchUrlWithSort(string query, int page, string? sort)
    {
        var url = SearchUrl(query, page);
        if (string.IsNullOrWhiteSpace(sort))
        {
            return url;
        }

        return $"{url}&s={Uri.EscapeDataString(sort)}";
    }

    string WarmUpUrl => BaseUri.ToString();

    bool WarmUpEnabled => true;

    /// <summary>
    /// Adds browser navigation headers to the warm-up request. Used by
    /// Amazon.co.uk's locale warm-up.
    /// </summary>
    bool WarmUpNavigationHeaders => false;

    /// <summary>
    /// Best-effort home request before the GLOW CAP flow to establish
    /// site-wide session cookies. Needed by Amazon.es.
    /// </summary>
    bool GlowPreflightEnabled => false;

    /// <summary>
    /// True when the marketplace accepts a country-only destination for
    /// international shipping. Amazon.com exposes a country picker for
    /// non-US destinations and reserves the ZIP code field for the US.
    /// Domestic destinations still require a postal code.
    /// </summary>
    bool SupportsCountryOnlyDelivery => false;

    /// <summary>
    /// GLOW page type used by the gateway modal. Amazon.com uses the exports
    /// gateway, other marketplaces use the standard gateway.
    /// </summary>
    string GlowGatewayPageType => "Gateway";

    bool AllowPriceFallback => true;

    string? DefaultCookieHeader => null;

    /// <summary>
    /// Amazon's own retail seller ID for this marketplace. AOD exposes
    /// Amazon-sold offers as plain text without a seller link, so this ID is
    /// needed to build a URL that selects the correct offer.
    /// </summary>
    string AmazonSellerId => AmazonRetailSeller.IdFor(Id);

    string DetailUrl(string asin) =>
        new Uri(BaseUri, $"dp/{asin}").AbsoluteUri;

    string ProductUrl(string asin) => DetailUrl(asin);

    /// <summary>
    /// AOD endpoint used by the product page to load all buying options.
    /// Page 1 is the initial fragment; pages 2+ use Amazon's pagination
    /// parameters (10 offers per page).
    /// </summary>
    string BuyingOptionsUrl(string asin, int page = 1)
    {
        var query =
            $"asin={Uri.EscapeDataString(asin)}&m=&qid=&smid=" +
            "&sourcecustomerorglistid=&sourcecustomerorglistitemid=&sr=&pc=dp";

        if (page <= 1)
        {
            return new Uri(
                BaseUri,
                $"gp/product/ajax/aodAjaxMain/ref=dp_aod_unknown_mbc?{query}").AbsoluteUri;
        }

        return new Uri(
            BaseUri,
            $"gp/product/ajax/aodAjaxMain/ref=aod_page_{page}?{query}" +
            $"&isonlyrenderofferlist=true&pageno={page}").AbsoluteUri;
    }

    /// <summary>
    /// Product URL selecting one seller's offer, matching the URL Amazon
    /// opens after adding a specific AOD offer to the cart.
    /// </summary>
    string BuyingOptionDetailUrl(string asin, string sellerId)
    {
        var url = ProductUrl(asin);
        if (string.IsNullOrEmpty(sellerId))
        {
            return url;
        }

        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}smid={Uri.EscapeDataString(sellerId)}&psc=1";
    }
}
