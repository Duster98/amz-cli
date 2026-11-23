using System.Text.Json.Nodes;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Reads the desktop twister payload embedded in Amazon product pages.
/// The payload is JavaScript, not strict JSON, so the relevant fields are
/// extracted one by one instead of parsing the whole object.
/// </summary>
public static class ProductVariantsParser
{
    private const string Marker = "twister-js-init-dpx-data";
    private const string DataMarker = "dataToReturn";

    public static ProductVariants? Parse(
        string html,
        string selectedAsin,
        IMarketplaceProfile marketplace)
    {
        var marker = html.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var dataMarker = html.IndexOf(DataMarker, marker, StringComparison.Ordinal);
        if (dataMarker < 0)
        {
            return null;
        }

        var start = dataMarker;
        var parentAsin = ExtractStringProperty(html, start, "parentAsin") ?? string.Empty;
        var dimensions = ExtractNode(html, start, "dimensions") as JsonArray;
        var labels = ExtractNode(html, start, "variationDisplayLabels") as JsonObject;
        var displayData = ExtractNode(html, start, "dimensionValuesDisplayData") as JsonObject;
        if (dimensions is null || displayData is null || displayData.Count == 0)
        {
            return null;
        }

        var dimensionKeys = dimensions
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .Where(key => key.Length > 0)
            .ToList();
        if (dimensionKeys.Count == 0)
        {
            return null;
        }

        var dimensionLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in dimensionKeys)
        {
            dimensionLabels[key] = labels?[key]?.GetValue<string>() ?? key;
        }

        var items = new List<ProductVariant>();
        foreach (var (asin, valuesNode) in displayData)
        {
            if (valuesNode is not JsonArray values)
            {
                continue;
            }

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < dimensionKeys.Count && index < values.Count; index++)
            {
                var value = values[index]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    attributes[dimensionLabels[dimensionKeys[index]]] = value;
                }
            }

            items.Add(new ProductVariant
            {
                Asin = asin,
                Selected = string.Equals(asin, selectedAsin, StringComparison.OrdinalIgnoreCase),
                Attributes = attributes,
                Url = marketplace.ProductUrl(asin),
            });
        }

        if (items.Count == 0)
        {
            return null;
        }

        items = items
            .OrderByDescending(item => item.Selected)
            .ThenBy(item => item.Asin, StringComparer.Ordinal)
            .ToList();

        return new ProductVariants
        {
            ParentAsin = parentAsin,
            SelectedAsin = selectedAsin,
            Dimensions = dimensionLabels,
            Items = items,
        };
    }

    private static string? ExtractStringProperty(
        string text,
        int start,
        string name)
    {
        var key = $"\"{name}\"";
        var index = text.IndexOf(key, start, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var colon = text.IndexOf(':', index + key.Length);
        if (colon < 0)
        {
            return null;
        }

        var valueStart = SkipWhitespace(text, colon + 1);
        if (valueStart >= text.Length || text[valueStart] != '"')
        {
            return null;
        }

        var valueEnd = text.IndexOf('"', valueStart + 1);
        return valueEnd < 0
            ? null
            : text[(valueStart + 1)..valueEnd];
    }

    private static JsonNode? ExtractNode(
        string text,
        int start,
        string name)
    {
        var key = $"\"{name}\"";
        var index = text.IndexOf(key, start, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var colon = text.IndexOf(':', index + key.Length);
        if (colon < 0)
        {
            return null;
        }

        var valueStart = SkipWhitespace(text, colon + 1);
        if (valueStart >= text.Length)
        {
            return null;
        }

        var open = text[valueStart];
        var close = open switch
        {
            '{' => '}',
            '[' => ']',
            _ => '\0',
        };
        if (close == '\0')
        {
            return null;
        }

        var value = ExtractBalanced(text, valueStart, open, close);
        if (value is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static string? ExtractBalanced(
        string text,
        int start,
        char open,
        char close)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (character == '\\')
                {
                    escape = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(index + 1)];
                }
            }
        }

        return null;
    }
}
