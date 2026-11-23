using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Delivery estimate for the selected destination. Fields are intentionally
/// strings: Amazon localizes costs and dates and may show free-form labels.
/// </summary>
public sealed class DeliveryInfo
{
    [JsonPropertyName("location")]
    [JsonPropertyOrder(0)]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    [JsonPropertyOrder(1)]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("postal_code")]
    [JsonPropertyOrder(2)]
    public string PostalCode { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonPropertyOrder(3)]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("cost")]
    [JsonPropertyOrder(4)]
    public string Cost { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    [JsonPropertyOrder(5)]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("cutoff")]
    [JsonPropertyOrder(6)]
    public string Cutoff { get; set; } = string.Empty;

    [JsonPropertyName("raw")]
    [JsonPropertyOrder(7)]
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    /// All delivery alternatives exposed by the page, when more than one is
    /// present (for example free delivery plus a faster option).
    /// </summary>
    [JsonPropertyName("options")]
    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeliveryOption>? Options { get; set; }
}
