using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Extracts the calendar day from Amazon deal-terms text. Amazon only exposes
/// the localized sentence on the product page, not a machine-readable
/// timestamp, so this parser handles the supported storefront languages.
/// </summary>
public static class DealEndDateParser
{
    private static readonly Regex IsoDate = new(
        @"(?<y>\d{4})[-/](?<m>\d{1,2})[-/](?<d>\d{1,2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JapaneseDate = new(
        @"(?<y>\d{4})\s*年\s*(?<m>\d{1,2})\s*月\s*(?<d>\d{1,2})\s*日",
        RegexOptions.Compiled);

    private static readonly Regex DayMonthYear = new(
        @"(?<d>\d{1,2})(?:er)?\.?\s+(?:de\s+)?(?<month>[A-Za-zÀ-ÿ]+)\.?\s+(?:de\s+)?(?<y>\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MonthDayYear = new(
        @"(?<month>[A-Za-zÀ-ÿ]+)\.?\s+(?<d>\d{1,2}),?\s+(?<y>\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, int> Months =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["january"] = 1,
            ["jan"] = 1,
            ["gennaio"] = 1,
            ["januar"] = 1,
            ["janvier"] = 1,
            ["enero"] = 1,
            ["february"] = 2,
            ["feb"] = 2,
            ["febbraio"] = 2,
            ["februar"] = 2,
            ["fevrier"] = 2,
            ["febrero"] = 2,
            ["march"] = 3,
            ["mar"] = 3,
            ["marzo"] = 3,
            ["marz"] = 3,
            ["mars"] = 3,
            ["april"] = 4,
            ["apr"] = 4,
            ["aprile"] = 4,
            ["avril"] = 4,
            ["abril"] = 4,
            ["may"] = 5,
            ["maggio"] = 5,
            ["mai"] = 5,
            ["mayo"] = 5,
            ["june"] = 6,
            ["jun"] = 6,
            ["giugno"] = 6,
            ["juni"] = 6,
            ["juin"] = 6,
            ["junio"] = 6,
            ["july"] = 7,
            ["jul"] = 7,
            ["luglio"] = 7,
            ["juli"] = 7,
            ["juillet"] = 7,
            ["julio"] = 7,
            ["august"] = 8,
            ["aug"] = 8,
            ["agosto"] = 8,
            ["aout"] = 8,
            ["september"] = 9,
            ["sep"] = 9,
            ["settembre"] = 9,
            ["septembre"] = 9,
            ["septiembre"] = 9,
            ["october"] = 10,
            ["oct"] = 10,
            ["ottobre"] = 10,
            ["oktober"] = 10,
            ["octobre"] = 10,
            ["octubre"] = 10,
            ["november"] = 11,
            ["nov"] = 11,
            ["novembre"] = 11,
            ["noviembre"] = 11,
            ["december"] = 12,
            ["dec"] = 12,
            ["dicembre"] = 12,
            ["dezember"] = 12,
            ["decembre"] = 12,
            ["diciembre"] = 12,
        };

    public static DateOnly? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();

        var iso = IsoDate.Match(value);
        if (iso.Success)
        {
            return Build(
                iso.Groups["y"].Value,
                iso.Groups["m"].Value,
                iso.Groups["d"].Value);
        }

        var japanese = JapaneseDate.Match(value);
        if (japanese.Success)
        {
            return Build(
                japanese.Groups["y"].Value,
                japanese.Groups["m"].Value,
                japanese.Groups["d"].Value);
        }

        var dayMonthYear = DayMonthYear.Match(value);
        if (dayMonthYear.Success
            && TryGetMonth(dayMonthYear.Groups["month"].Value, out var month))
        {
            return Build(
                dayMonthYear.Groups["y"].Value,
                month.ToString(CultureInfo.InvariantCulture),
                dayMonthYear.Groups["d"].Value);
        }

        var monthDayYear = MonthDayYear.Match(value);
        if (monthDayYear.Success
            && TryGetMonth(monthDayYear.Groups["month"].Value, out month))
        {
            return Build(
                monthDayYear.Groups["y"].Value,
                month.ToString(CultureInfo.InvariantCulture),
                monthDayYear.Groups["d"].Value);
        }

        return null;
    }

    private static bool TryGetMonth(string value, out int month)
    {
        var normalized = NormalizeMonth(value);
        return Months.TryGetValue(normalized, out month);
    }

    private static string NormalizeMonth(string value)
    {
        var normalized = value
            .Trim()
            .Trim('.')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static DateOnly? Build(string year, string month, string day)
    {
        if (!int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d)
            || y is < 2000 or > 2100
            || m is < 1 or > 12
            || d is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateOnly(y, m, d);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
