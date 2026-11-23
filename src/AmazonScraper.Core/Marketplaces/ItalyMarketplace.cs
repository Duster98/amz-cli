namespace AmazonScraper.Core.Marketplaces;

public sealed class ItalyMarketplace : IMarketplaceProfile
{
    public static ItalyMarketplace Instance { get; } = new();

    public string Id => "it";
    public string DisplayName => "Amazon.it";
    public string CountryCode => "IT";
    public Uri BaseUri { get; } = new("https://www.amazon.it/", UriKind.Absolute);
    public string AcceptLanguage => "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7";
    public IMarketplacePatterns Patterns { get; } = new ItalyPatterns();
    public IMarketplaceFormat Format { get; } = ItalianMarketplaceFormat.Instance;

    public string SearchUrl(string query, int page = 1) =>
        MarketplaceUrlBuilder.Search(this, query, page);
}
