using System.Text;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Rendering;

/// <summary>
/// Tab-separated output for scripts and agents. The normal table keeps the
/// commerce columns; the diff table transposes all attributes so missing values
/// and specification mismatches are explicit.
/// </summary>
public static class CompareTabsTsv
{
    public static string Table(IReadOnlyList<CompareShowItem> items)
    {
        var lines = new List<string>
        {
            "Market\tSource\tASIN\tTitle\tBrand\tPrice\tOriginal\tDiscount\t" +
            "Coupon\tRating\tReviews\tSeller\tShippedBy\tAvailability\tReturns\t" +
            "DeliveryCost\tDeliveryTime\tDeliveryLocation\tDeliveryType\t" +
            "AmazonChoice\tDeal\tCharger\tOffersCount\tLowestOffer\tCondition\t" +
            "LandedTotal\tSpecs\tAmazonDetails\tSafetyWarnings\tError",
        };

        foreach (var item in items)
        {
            var detail = item.Detail;
            lines.Add(string.Join('\t',
                Clean(item.Item.Domain),
                Clean(item.Item.Source),
                Clean(item.Item.Asin),
                Clean(detail?.Title ?? string.Empty),
                Clean(detail?.Brand ?? string.Empty),
                Clean(detail?.Price ?? string.Empty),
                Clean(detail?.OriginalPrice ?? string.Empty),
                Clean(detail?.Discount ?? string.Empty),
                Clean(detail?.Coupon ?? string.Empty),
                Clean(detail?.Rating ?? string.Empty),
                Clean(detail?.Reviews ?? string.Empty),
                Clean(detail?.Seller ?? string.Empty),
                Clean(detail?.ShippedBy ?? string.Empty),
                Clean(detail?.Availability ?? string.Empty),
                Clean(detail?.Returns ?? string.Empty),
                Clean(detail?.Delivery?.Cost ?? string.Empty),
                Clean(detail?.Delivery?.Time ?? string.Empty),
                Clean(detail?.Delivery?.Location ?? string.Empty),
                Clean(detail?.Delivery?.Type ?? string.Empty),
                detail?.AmazonChoice == true ? "true" : string.Empty,
                Clean(detail?.Deal ?? string.Empty),
                detail?.Charger?.Included is { } charger
                    ? (charger ? "true" : "false")
                    : string.Empty,
                detail?.BuyingOptions?.Count.ToString() ?? string.Empty,
                Clean(detail?.BuyingOptions?.LowestPrice ?? string.Empty),
                Clean(CompareDiffBuilder.FeaturedCondition(detail) ?? string.Empty),
                Clean(detail?.ImportFees?.LandedTotal ?? string.Empty),
                Clean(SpecsText(detail)),
                Clean(AmazonDetailsText(detail)),
                Clean(string.Join("; ", detail?.SafetyWarnings ?? [])),
                Clean(item.Error ?? string.Empty)));
        }

        return string.Join('\n', lines);
    }

    public static string Diff(CompareDiff diff)
    {
        var header = new List<string> { "Attribute", "Different" };
        header.AddRange(diff.Products.Select(product =>
            $"#{product.Index} {product.Market} {product.Asin}"));
        var lines = new List<string> { string.Join('\t', header) };
        foreach (var attribute in diff.Attributes)
        {
            var row = new List<string>
            {
                Clean(attribute.Name),
                attribute.Different ? "true" : "false",
            };
            row.AddRange(attribute.Values.Select(Clean));
            lines.Add(string.Join('\t', row));
        }
        return string.Join('\n', lines);
    }

    private static string AmazonDetailsText(ProductDetail? detail)
    {
        if (detail?.AmazonDetails is not { Count: > 0 } entries)
        {
            return string.Empty;
        }
        return string.Join(
            "; ",
            entries.Select(entry => $"{entry.Name}={entry.Value}"));
    }

    private static string SpecsText(ProductDetail? detail)
    {
        if (detail is null || detail.Specs.Count == 0)
        {
            return string.Empty;
        }
        var builder = new StringBuilder();
        foreach (var pair in detail.Specs)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }
            builder.Append(pair.Key).Append('=').Append(pair.Value);
        }
        return builder.ToString();
    }

    private static string Clean(string value) =>
        value
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
