namespace AmazonScraper.Core.Marketplaces;

public sealed class JapanMarketplace : IMarketplaceProfile
{
    public static JapanMarketplace Instance { get; } = new();

    public string Id => "jp";
    public string DisplayName => "Amazon.co.jp";
    public string CountryCode => "JP";
    public Uri BaseUri { get; } = new("https://www.amazon.co.jp/", UriKind.Absolute);
    public string AcceptLanguage => "ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7";
    public IMarketplacePatterns Patterns { get; } = new JapanPatterns();
    public IMarketplaceFormat Format { get; } = JapaneseFormat.Instance;

    public string SearchPath => "-/ja/s";

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string WarmUpUrl => new Uri(BaseUri, "-/ja/").AbsoluteUri;

    public bool WarmUpEnabled => false;

    // Non-JP destinations use the country picker, as on Amazon.com.
    public bool SupportsCountryOnlyDelivery => true;

    public string? DefaultCookieHeader => "i18n-prefs=JPY";

    public string DetailUrl(string asin) =>
        new Uri(BaseUri, $"-/ja/dp/{asin}").AbsoluteUri;

    public string ProductUrl(string asin) =>
        new Uri(BaseUri, $"dp/{asin}").AbsoluteUri;
}
