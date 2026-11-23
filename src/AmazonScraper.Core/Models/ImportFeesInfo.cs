using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Import charges shown by Amazon for cross-border shipping. Amazon can expose
/// a numeric estimate (price + shipping + estimated import charges + total) or
/// only a disclosure saying that charges are applied at checkout.
/// </summary>
public sealed class ImportFeesInfo
{
    /// <summary>
    /// Price used by Amazon in the import table. It can differ from the current
    /// buybox price; <see cref="PriceBasis"/> explains which price it is.
    /// </summary>
    [JsonPropertyName("price")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Price { get; set; }

    /// <summary>
    /// "current", "list" or "other" when the import-table price can be matched
    /// against the detail price/list price.
    /// </summary>
    [JsonPropertyName("price_basis")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PriceBasis { get; set; }

    [JsonPropertyName("destination")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Destination { get; set; }

    [JsonPropertyName("shipping")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Shipping { get; set; }

    [JsonPropertyName("import_charges")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImportCharges { get; set; }

    /// <summary>Price + shipping + import charges, as displayed by Amazon.</summary>
    [JsonPropertyName("landed_total")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LandedTotal { get; set; }

    /// <summary>
    /// True when Amazon only discloses that import charges are applied at
    /// checkout and does not show an amount on the product page.
    /// </summary>
    [JsonPropertyName("at_checkout")]
    [JsonPropertyOrder(6)]
    public bool AtCheckout { get; set; }

    [JsonPropertyName("text")]
    [JsonPropertyOrder(7)]
    public string Text { get; set; } = string.Empty;
}
