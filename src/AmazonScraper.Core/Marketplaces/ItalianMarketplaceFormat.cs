using System.Globalization;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Italian price and rating conventions: "1.234,56 €", "4,7 / 5".
/// </summary>
public sealed class ItalianMarketplaceFormat : IMarketplaceFormat
{
    public static ItalianMarketplaceFormat Instance { get; } = new();

    public string Rating(string value) =>
        $"{value.Replace('.', ',')} / 5";

    public string Price(string whole, string fraction) =>
        $"{whole},{fraction} €";

    public string SinglePrice(string value) =>
        $"{value.Trim()} €";

    public string OriginalPrice(string value) =>
        $"{value.Trim()} €";

    public bool TryParsePrice(string value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("\u00a0", string.Empty, StringComparison.Ordinal)
            .Replace("\u202f", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.')
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }
}
