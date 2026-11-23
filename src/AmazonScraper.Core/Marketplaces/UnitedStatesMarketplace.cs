namespace AmazonScraper.Core.Marketplaces;

public sealed class UnitedStatesMarketplace : IMarketplaceProfile
{
    public static UnitedStatesMarketplace Instance { get; } = new();

    public string Id => "com";
    public string DisplayName => "Amazon.com";
    public string CountryCode => "US";
    public Uri BaseUri { get; } = new("https://www.amazon.com/", UriKind.Absolute);
    public string AcceptLanguage => "en-US,en;q=0.9";
    public IMarketplacePatterns Patterns { get; } = new UnitedStatesPatterns();
    public IMarketplaceFormat Format { get; } = UnitedStatesFormat.Instance;

    public string SearchPath => "-/en/s";

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string WarmUpUrl => new Uri(BaseUri, "-/en/").AbsoluteUri;

    // The direct localized search/detail endpoints work; the home warm-up can
    // trigger the AWS WAF/captcha interstitial.
    public bool WarmUpEnabled => false;

    // International destinations use the "ship outside the US" country
    // picker; the ZIP code field only applies to US addresses.
    public bool SupportsCountryOnlyDelivery => true;

    public string GlowGatewayPageType => "exports-gateway-desktop";

    public string? DefaultCookieHeader => "i18n-prefs=USD";
}
