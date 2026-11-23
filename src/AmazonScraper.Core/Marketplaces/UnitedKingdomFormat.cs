using System.Globalization;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// UK price/rating conventions: "£237.37", "£2,749.17", "4.6 / 5".
/// </summary>
public sealed class UnitedKingdomFormat : IMarketplaceFormat
{
    public static UnitedKingdomFormat Instance { get; } = new();

    public string Rating(string value) =>
        $"{value.Replace(',', '.')} / 5";

    public string Price(string whole, string fraction) =>
        $"£{whole}.{fraction}";

    public string SinglePrice(string value) =>
        $"£{CleanNumber(value)}";

    public string OriginalPrice(string value) =>
        $"£{CleanNumber(value)}";

    public bool TryParsePrice(string value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = CleanNumber(value)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string CleanNumber(string value) =>
        value
            .Replace("Was:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("GBP", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("£", string.Empty, StringComparison.Ordinal)
            .Trim();
}
