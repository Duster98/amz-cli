using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Formatting;

public static class SearchDeliveryFormatter
{
    public static string Format(SearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.DeliveryText))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(result.DeliveryCountry)
            ? result.DeliveryText
            : $"{result.DeliveryText} ({result.DeliveryCountry})";
    }
}
