namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Locale-dependent formatting and parsing of prices and ratings.
/// </summary>
public interface IMarketplaceFormat
{
    string Rating(string value);
    string Price(string whole, string fraction);
    string SinglePrice(string value);
    string OriginalPrice(string value);
    bool TryParsePrice(string value, out decimal amount);
}
