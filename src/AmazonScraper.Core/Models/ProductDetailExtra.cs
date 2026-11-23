using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Additional product-page key/value rows that are not part of the primary
/// specification table, for example manufacturer name or sales rank.
/// </summary>
public sealed class ProductDetailExtra
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(0)]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    [JsonPropertyOrder(1)]
    public string Value { get; set; } = string.Empty;
}
