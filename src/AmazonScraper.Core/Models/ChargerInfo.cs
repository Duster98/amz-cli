using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Charger information shown by Amazon's safety/resources widget.
/// </summary>
public sealed class ChargerInfo
{
    /// <summary>
    /// True when Amazon explicitly says the charger is included; false when
    /// the page shows "Senza caricatore" / "Charger not included".
    /// </summary>
    [JsonPropertyName("included")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Included { get; set; }

    /// <summary>Localized widget label, for example "Senza caricatore".</summary>
    [JsonPropertyName("label")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>
    /// Description bullet that mentions the charger, when present.
    /// </summary>
    [JsonPropertyName("note")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    /// <summary>
    /// Localized "box contents" value, for example "Certificato di garanzia".
    /// </summary>
    [JsonPropertyName("box_contents")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BoxContents { get; set; }
}
