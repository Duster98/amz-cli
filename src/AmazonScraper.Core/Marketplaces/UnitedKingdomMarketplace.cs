namespace AmazonScraper.Core.Marketplaces;

public sealed class UnitedKingdomMarketplace : IMarketplaceProfile
{
    public static UnitedKingdomMarketplace Instance { get; } = new();

    public string Id => "uk";
    public string DisplayName => "Amazon.co.uk";
    public string CountryCode => "GB";
    public Uri BaseUri { get; } = new("https://www.amazon.co.uk/", UriKind.Absolute);
    public string AcceptLanguage => "en-GB,en;q=0.9";
    public IMarketplacePatterns Patterns { get; } = new UnitedKingdomPatterns();
    public IMarketplaceFormat Format { get; } = UnitedKingdomFormat.Instance;

    public string SearchPath => "s";

    public bool SearchNavigationHeaders => true;

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string WarmUpUrl => new Uri(BaseUri, "-/en-gb/").AbsoluteUri;

    public bool WarmUpNavigationHeaders => true;

    // A neutral locale page: it establishes the session cookies (session-id,
    // session-id-time, lc-acbuk) that keep the following /s request out of the
    // AWS WAF challenge on a fresh client. Unlike a product page, it does not
    // depend on any ASIN remaining available.
    public bool WarmUpEnabled => true;

    // Mobile detail pages have a main `priceToPay` block; when it is empty
    // the offer is unavailable, so never fall back to sponsored carousel prices.
    public bool AllowPriceFallback => false;

    // Without these cookies amazon.co.uk may serve non-UK pricing/language.
    public string? DefaultCookieHeader => "i18n-prefs=GBP; lc-acbuk=en_GB";
}
