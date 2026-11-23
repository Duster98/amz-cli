using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// One buying option from Amazon's "all offers display" (AOD) response.
/// <see cref="OfferListingId"/> is the value Amazon uses for the add-to-cart
/// form (<c>items[0.base][offerListingId]</c>), decoded once so it can be
/// copied and reused as-is.
/// </summary>
public sealed class BuyingOption
{
    [JsonPropertyName("offer_listing_id")]
    [JsonPropertyOrder(0)]
    public string OfferListingId { get; set; } = string.Empty;

    [JsonPropertyName("seller")]
    [JsonPropertyOrder(1)]
    public string Seller { get; set; } = string.Empty;

    [JsonPropertyName("seller_id")]
    [JsonPropertyOrder(2)]
    public string SellerId { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    [JsonPropertyOrder(3)]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("shipping")]
    [JsonPropertyOrder(4)]
    public string Shipping { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    [JsonPropertyOrder(5)]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("condition_type")]
    [JsonPropertyOrder(6)]
    public string ConditionType { get; set; } = string.Empty;

    [JsonPropertyName("condition_grade")]
    [JsonPropertyOrder(7)]
    public string ConditionGrade { get; set; } = string.Empty;

    [JsonPropertyName("delivery")]
    [JsonPropertyOrder(8)]
    public string Delivery { get; set; } = string.Empty;

    [JsonPropertyName("detail_url")]
    [JsonPropertyOrder(9)]
    public string DetailUrl { get; set; } = string.Empty;

    /// <summary>
    /// True for the offer shown in the buy box. The Web offers list hides it
    /// because the selected offer is already visible above.
    /// </summary>
    [JsonPropertyName("is_featured")]
    [JsonPropertyOrder(10)]
    public bool IsFeatured { get; set; }
}
