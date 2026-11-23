using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Numeric rating summary exposed by the product page. The localized
/// <see cref="ProductDetail.Rating"/> string remains unchanged; this model
/// adds the machine-readable average, count and 5-to-1 star distribution.
/// </summary>
public sealed class RatingDetails
{
    [JsonPropertyName("average")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Average { get; set; }

    [JsonPropertyName("count")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; set; }

    [JsonPropertyName("histogram")]
    [JsonPropertyOrder(2)]
    public Dictionary<string, int> Histogram { get; set; } = [];
}
