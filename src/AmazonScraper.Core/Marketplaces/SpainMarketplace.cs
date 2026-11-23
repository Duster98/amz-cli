namespace AmazonScraper.Core.Marketplaces;

public sealed class SpainMarketplace : IMarketplaceProfile
{
    public static SpainMarketplace Instance { get; } = new();

    public string Id => "es";
    public string DisplayName => "Amazon.es";
    public string CountryCode => "ES";
    public Uri BaseUri { get; } = new("https://www.amazon.es/", UriKind.Absolute);
    public string AcceptLanguage => "es-ES,es;q=0.9,en;q=0.8";
    public IMarketplacePatterns Patterns { get; } = new SpainPatterns();
    public IMarketplaceFormat Format { get; } = SpainFormat.Instance;

    public string SearchPath => "-/es/s";

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string WarmUpUrl => new Uri(BaseUri, "-/es/").AbsoluteUri;

    public bool WarmUpEnabled => false;

    public bool GlowPreflightEnabled => true;

    // Spain pages often have sponsored `a-price` blocks when the main offer is
    // unavailable; do not fall back to the whole document.
    public bool AllowPriceFallback => false;

    public string? DefaultCookieHeader => "i18n-prefs=EUR";

    public string DetailUrl(string asin) =>
        new Uri(BaseUri, $"-/es/dp/{asin}").AbsoluteUri;

    public string ProductUrl(string asin) =>
        new Uri(BaseUri, $"dp/{asin}").AbsoluteUri;
}
