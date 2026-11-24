using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// A Prime-only price shown next to the standard offer. When <see cref="Only"/>
/// is true the product has no regular offer and the main price is this price.
/// </summary>
public sealed class PrimePricing
{
    [JsonPropertyName("price")]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("original_price")]
    public string OriginalPrice { get; set; } = string.Empty;

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = string.Empty;

    [JsonPropertyName("only")]
    public bool Only { get; set; }
}
