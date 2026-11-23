using System.Text.Encodings.Web;
using System.Text.Json;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Formatting;

public static class JsonFormatter
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Search(IEnumerable<SearchResult> results) =>
        JsonSerializer.Serialize(results, Options);

    public static string Detail(ProductDetail detail) =>
        JsonSerializer.Serialize(detail, Options);
}
