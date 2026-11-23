using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

public sealed class SearchResult
{
    [JsonPropertyName("asin")]
    [JsonPropertyOrder(0)]
    public string Asin { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonPropertyOrder(1)]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("price")]
    [JsonPropertyOrder(2)]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    [JsonPropertyOrder(3)]
    public string Rating { get; init; } = string.Empty;

    [JsonPropertyName("reviews")]
    [JsonPropertyOrder(4)]
    public string Reviews { get; init; } = string.Empty;

    [JsonPropertyName("image")]
    [JsonPropertyOrder(5)]
    public string Image { get; init; } = string.Empty;

    [JsonPropertyName("coupon")]
    [JsonPropertyOrder(6)]
    public string Coupon { get; init; } = string.Empty;

    [JsonPropertyName("sponsored")]
    [JsonPropertyOrder(7)]
    public bool Sponsored { get; init; }

    /// <summary>
    /// Early delivery estimate shown on the search card. .NET-only
    /// extension, omitted from JSON unless the caller requested it.
    /// </summary>
    [JsonPropertyName("delivery_text")]
    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryText { get; init; }

    /// <summary>
    /// True when the card's delivery text advertises free shipping.
    /// Omitted unless delivery data was requested.
    /// </summary>
    [JsonPropertyName("free_delivery")]
    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FreeDelivery { get; init; }

    [JsonPropertyName("delivery_country")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryCountry { get; init; }

    /// <summary>
    /// Marketplace that produced the result. Set only by the web
    /// multi-marketplace search so the UI can open the correct detail page.
    /// </summary>
    [JsonPropertyName("marketplace_id")]
    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarketplaceId { get; set; }

    [JsonPropertyName("marketplace_name")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarketplaceName { get; set; }

    /// <summary>ISO 4217 currency of <see cref="Price"/> for this item.</summary>
    [JsonPropertyName("currency")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Currency { get; set; }

    /// <summary>
    /// Localized deal badge text shown on the search card.
    /// </summary>
    [JsonPropertyName("deal")]
    [JsonPropertyOrder(14)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Deal { get; init; }

    /// <summary>
    /// Amazon promotion identifier from the search card
    /// (<c>amzn1.deal.…</c>).
    /// </summary>
    [JsonPropertyName("deal_id")]
    [JsonPropertyOrder(15)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DealId { get; init; }

    /// <summary>
    /// Struck reference price shown on the search card ("Consigl.",
    /// "Mediano", "Was", etc.).
    /// </summary>
    [JsonPropertyName("original_price")]
    [JsonPropertyOrder(16)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalPrice { get; set; }

    /// <summary>
    /// Computed discount percentage, for example <c>-16%</c>.
    /// </summary>
    [JsonPropertyName("discount")]
    [JsonPropertyOrder(17)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Discount { get; set; }

    /// <summary>
    /// True when the search card exposes the Amazon's Choice badge.
    /// </summary>
    [JsonPropertyName("amazon_choice")]
    [JsonPropertyOrder(18)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AmazonChoice { get; init; }

    /// <summary>Localized search badge label, for example "Overall Pick".</summary>
    [JsonPropertyName("amazon_choice_text")]
    [JsonPropertyOrder(19)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmazonChoiceText { get; init; }

    /// <summary>Optional "for ..." phrase shown next to the search badge.</summary>
    [JsonPropertyName("amazon_choice_for")]
    [JsonPropertyOrder(20)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmazonChoiceFor { get; init; }

    /// <summary>
    /// Localized condition of the offer used to fill a missing search-card
    /// price ("D'occasion: Très bon", "Usato", "New"). Set only by enrichment.
    /// </summary>
    [JsonPropertyName("condition")]
    [JsonPropertyOrder(21)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Condition { get; set; }
}
