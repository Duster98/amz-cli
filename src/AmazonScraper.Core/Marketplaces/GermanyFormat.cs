using System.Globalization;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// German price/rating conventions: "1.234,56 €", "4,5 / 5". Amazon.de can
/// also serve English pages ("€129.95"), so both separators are accepted.
/// </summary>
public sealed class GermanyFormat : IMarketplaceFormat
{
    public static GermanyFormat Instance { get; } = new();

    private static readonly NumberFormatInfo GermanNumberFormat = CreateNumberFormat();

    public string Rating(string value) =>
        $"{value.Replace('.', ',')} / 5";

    public string Price(string whole, string fraction)
    {
        return TryParsePrice($"{whole},{fraction}", out var amount)
            ? FormatAmount(amount)
            : $"{whole},{fraction} €";
    }

    public string SinglePrice(string value)
    {
        return TryParsePrice(value, out var amount)
            ? FormatAmount(amount)
            : $"{value.Trim()} €";
    }

    public string OriginalPrice(string value)
    {
        return TryParsePrice(value, out var amount)
            ? FormatAmount(amount)
            : $"{value.Trim()} €";
    }

    public bool TryParsePrice(string value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", string.Empty, StringComparison.Ordinal)
            .Replace("\u202f", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            normalized = lastComma > lastDot
                ? normalized.Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace(',', '.')
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastComma >= 0)
        {
            normalized = normalized.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            var decimals = normalized.Length - lastDot - 1;
            if (decimals is not (1 or 2))
            {
                normalized = normalized.Replace(".", string.Empty, StringComparison.Ordinal);
            }
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string FormatAmount(decimal amount) =>
        $"{amount.ToString("N2", GermanNumberFormat)} €";

    private static NumberFormatInfo CreateNumberFormat()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = ",";
        format.NumberGroupSeparator = ".";
        format.NumberGroupSizes = [3];
        return format;
    }
}
