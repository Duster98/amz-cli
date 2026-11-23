using System.Globalization;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Japanese yen formatting: no decimal digits, e.g. "￥2,399".
/// </summary>
public sealed class JapaneseFormat : IMarketplaceFormat
{
    public static JapaneseFormat Instance { get; } = new();

    public string Rating(string value) =>
        $"{value.Replace(',', '.')} / 5";

    public string Price(string whole, string fraction) =>
        $"￥{whole}";

    public string SinglePrice(string value) =>
        $"￥{CleanNumber(value)}";

    public string OriginalPrice(string value) =>
        $"￥{CleanNumber(value)}";

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
            .Replace("List:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("参考価格:", string.Empty, StringComparison.Ordinal)
            .Replace("JPY", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("￥", string.Empty, StringComparison.Ordinal)
            .Replace("¥", string.Empty, StringComparison.Ordinal)
            .Trim();
}
