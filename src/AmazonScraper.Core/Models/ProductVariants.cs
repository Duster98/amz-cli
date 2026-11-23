using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

public sealed class ProductVariant
{
    [JsonPropertyName("asin")]
    [JsonPropertyOrder(0)]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("selected")]
    [JsonPropertyOrder(1)]
    public bool Selected { get; set; }

    [JsonPropertyName("attributes")]
    [JsonPropertyOrder(2)]
    public Dictionary<string, string> Attributes { get; set; } = [];

    [JsonPropertyName("url")]
    [JsonPropertyOrder(3)]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Price { get; set; }

    [JsonPropertyName("original_price")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalPrice { get; set; }

    [JsonPropertyName("discount")]
    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Discount { get; set; }

    [JsonPropertyName("availability")]
    [JsonPropertyOrder(7)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Availability { get; set; }
}

public sealed class ProductVariants
{
    [JsonPropertyName("parent_asin")]
    [JsonPropertyOrder(0)]
    public string ParentAsin { get; set; } = string.Empty;

    [JsonPropertyName("selected_asin")]
    [JsonPropertyOrder(1)]
    public string SelectedAsin { get; set; } = string.Empty;

    /// <summary>
    /// Raw dimension key (for example <c>size_name</c>) mapped to the
    /// localized label shown by Amazon (for example <c>Capacità</c>).
    /// </summary>
    [JsonPropertyName("dimensions")]
    [JsonPropertyOrder(2)]
    public Dictionary<string, string> Dimensions { get; set; } = [];

    [JsonPropertyName("items")]
    [JsonPropertyOrder(3)]
    public List<ProductVariant> Items { get; set; } = [];
}
