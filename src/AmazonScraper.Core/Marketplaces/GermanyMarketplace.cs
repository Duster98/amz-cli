namespace AmazonScraper.Core.Marketplaces;

public sealed class GermanyMarketplace : IMarketplaceProfile
{
    public static GermanyMarketplace Instance { get; } = new();

    public string Id => "de";
    public string DisplayName => "Amazon.de";
    public string CountryCode => "DE";
    public Uri BaseUri { get; } = new("https://www.amazon.de/", UriKind.Absolute);

    // The HAR used for this profile was captured with the English UI
    // (`lc-acbde=en_GB`); keeping that cookie makes parsing stable while
    // Amazon.de still uses EUR.
    public string AcceptLanguage => "en-GB,en;q=0.9,de;q=0.8";
    public IMarketplacePatterns Patterns { get; } = new GermanyPatterns();
    public IMarketplaceFormat Format { get; } = GermanyFormat.Instance;

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string DetailUrl(string asin) =>
        new Uri(BaseUri, $"dp/{asin}").AbsoluteUri;

    public string ProductUrl(string asin) => DetailUrl(asin);

    public string WarmUpUrl => new Uri(BaseUri, "-/en/").AbsoluteUri;

    // The direct search/detail endpoints work; the home warm-up can trigger
    // the AWS WAF interstitial.
    public bool WarmUpEnabled => false;

    public string? DefaultCookieHeader => "i18n-prefs=EUR; lc-acbde=en_GB";
}
