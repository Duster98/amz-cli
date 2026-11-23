using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Formatting;

public static class PriceParser
{
    public static decimal? TryParse(string price, IMarketplaceFormat format)
    {
        return format.TryParsePrice(price, out var amount) ? amount : null;
    }
}
