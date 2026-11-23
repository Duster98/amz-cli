using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Formatting;

public static partial class TextCleaner
{
    public static string Clean(string? text, int maxLength = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
        normalized = Whitespace().Replace(normalized, " ").Trim();

        if (maxLength > 0 && normalized.Length > maxLength)
        {
            normalized = normalized[..(maxLength - 1)] + "…";
        }

        return normalized;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
