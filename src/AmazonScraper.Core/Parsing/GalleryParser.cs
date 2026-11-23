using System.Net;
using System.Text.Json;

namespace AmazonScraper.Core.Parsing;

internal static class GalleryParser
{
    /// <summary>
    /// Python <c>_parse_gallery</c>: extract hiRes/large URLs from the
    /// colorImages.initial JSON blob, tolerating escaped quotes and HTML
    /// entities.
    /// </summary>
    public static List<string> Parse(string html)
    {
        var match = AmazonPatterns.GalleryStart.Match(html);
        if (!match.Success)
        {
            return [];
        }

        var start = match.Index + match.Length - 1;
        var depth = 1;
        var i = start + 1;

        while (i < html.Length && depth > 0)
        {
            if (html[i] == '[')
            {
                depth++;
            }
            else if (html[i] == ']')
            {
                depth--;
            }
            i++;
        }

        if (depth != 0 || i <= start)
        {
            return [];
        }

        var raw = html[start..i];
        var candidates = new[]
        {
            raw,
            raw.Replace("\\'", "'", StringComparison.Ordinal),
            WebUtility.HtmlDecode(raw),
            raw.Replace('\'', '"'),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var images = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var url = GetString(item, "hiRes") ?? GetString(item, "large");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        images.Add(url);
                    }
                }

                return images;
            }
            catch (JsonException)
            {
                // Try the next candidate, exactly like the Python code.
            }
        }

        return [];
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
