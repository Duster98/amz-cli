namespace AmazonScraper.Core.Marketplaces;

public sealed class FranceMarketplace : IMarketplaceProfile
{
    public static FranceMarketplace Instance { get; } = new();

    public string Id => "fr";
    public string DisplayName => "Amazon.fr";
    public string CountryCode => "FR";
    public Uri BaseUri { get; } = new("https://www.amazon.fr/", UriKind.Absolute);
    public string AcceptLanguage => "fr-FR,fr;q=0.9,en-GB;q=0.8,en;q=0.7";
    public IMarketplacePatterns Patterns { get; } = new FrancePatterns();
    public IMarketplaceFormat Format { get; } = FranceMarketplaceFormat.Instance;

    // `/s?k=` and `/` are often intercepted by AWS WAF on amazon.fr. The
    // localized paths `/-/fr/s?k=` and `/-/fr/` reach the same endpoints
    // without the WAF challenge on the current edge.
    public string SearchPath => "-/fr/s";

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);

    public string WarmUpUrl => new Uri(BaseUri, "-/fr/").AbsoluteUri;
}
