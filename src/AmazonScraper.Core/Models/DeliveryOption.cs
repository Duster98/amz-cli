using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// One delivery alternative shown on the product page. Amazon can expose a
/// free/default option and a faster option with its own order cutoff.
/// </summary>
public sealed class DeliveryOption
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(0)]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("cost")]
    [JsonPropertyOrder(1)]
    public string Cost { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    [JsonPropertyOrder(2)]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("cutoff")]
    [JsonPropertyOrder(3)]
    public string Cutoff { get; set; } = string.Empty;

    [JsonPropertyName("raw")]
    [JsonPropertyOrder(4)]
    public string Raw { get; set; } = string.Empty;
}
