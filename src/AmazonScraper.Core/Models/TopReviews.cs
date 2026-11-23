using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Reviews rendered on the product detail page. Counts refer to the items
/// returned after applying <c>--review-limit</c>.
/// </summary>
public sealed class TopReviews
{
    [JsonPropertyName("count")]
    [JsonPropertyOrder(0)]
    public int Count => Items.Count;

    [JsonPropertyName("local_count")]
    [JsonPropertyOrder(1)]
    public int LocalCount =>
        Items.Count(item => string.Equals(item.Source, "local", StringComparison.Ordinal));

    [JsonPropertyName("international_count")]
    [JsonPropertyOrder(2)]
    public int InternationalCount =>
        Items.Count(item => string.Equals(item.Source, "international", StringComparison.Ordinal));

    [JsonPropertyName("items")]
    [JsonPropertyOrder(3)]
    public List<ProductReview> Items { get; set; } = [];
}
