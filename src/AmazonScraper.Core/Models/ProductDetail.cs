using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

public sealed class ProductDetail
{
    [JsonPropertyName("asin")]
    [JsonPropertyOrder(0)]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonPropertyOrder(1)]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    [JsonPropertyOrder(2)]
    public string Brand { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    [JsonPropertyOrder(3)]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("original_price")]
    [JsonPropertyOrder(4)]
    public string OriginalPrice { get; set; } = string.Empty;

    [JsonPropertyName("discount")]
    [JsonPropertyOrder(5)]
    public string Discount { get; set; } = string.Empty;

    /// <summary>
    /// Prime price shown separately from the standard offer. Null when the
    /// page has no Prime-only price.
    /// </summary>
    [JsonPropertyName("prime")]
    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrimePricing? Prime { get; set; }

    [JsonPropertyName("rating")]
    [JsonPropertyOrder(6)]
    public string Rating { get; set; } = string.Empty;

    [JsonPropertyName("reviews")]
    [JsonPropertyOrder(7)]
    public string Reviews { get; set; } = string.Empty;

    [JsonPropertyName("seller")]
    [JsonPropertyOrder(8)]
    public string Seller { get; set; } = string.Empty;

    [JsonPropertyName("shipped_by")]
    [JsonPropertyOrder(9)]
    public string ShippedBy { get; set; } = string.Empty;

    [JsonPropertyName("returns")]
    [JsonPropertyOrder(10)]
    public string Returns { get; set; } = string.Empty;

    [JsonPropertyName("availability")]
    [JsonPropertyOrder(11)]
    public string Availability { get; set; } = string.Empty;

    [JsonPropertyName("coupon")]
    [JsonPropertyOrder(12)]
    public string Coupon { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    [JsonPropertyOrder(13)]
    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("frequently_returned")]
    [JsonPropertyOrder(14)]
    public string FrequentlyReturned { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    [JsonPropertyOrder(15)]
    public List<string> Images { get; set; } = [];

    [JsonPropertyName("description")]
    [JsonPropertyOrder(16)]
    public List<string> Description { get; set; } = [];

    [JsonPropertyName("specs")]
    [JsonPropertyOrder(17)]
    public Dictionary<string, string> Specs { get; set; } = [];

    /// <summary>
    /// Domain-aware product URL. Kept last to match the Python JSON order.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonPropertyOrder(18)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Delivery estimate for the current/requested destination. This is a
    /// .NET-only extension and is omitted from the JSON when no delivery block
    /// was found.
    /// </summary>
    [JsonPropertyName("delivery")]
    [JsonPropertyOrder(19)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeliveryInfo? Delivery { get; set; }

    /// <summary>
    /// Buying options and their offer listing IDs. Omitted from JSON when the
    /// product page has no "all offers" summary and AOD returned no offers.
    /// </summary>
    [JsonPropertyName("buying_options")]
    [JsonPropertyOrder(20)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BuyingOptionsSummary? BuyingOptions { get; set; }

    /// <summary>
    /// Localized deal badge text (for example "Black Friday anticipado",
    /// "Limited time deal"). Omitted when the page has no deal badge.
    /// </summary>
    [JsonPropertyName("deal")]
    [JsonPropertyOrder(21)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Deal { get; set; }

    /// <summary>
    /// Amazon promotion identifier from the deal badge
    /// (<c>amzn1.deal.…</c>).
    /// </summary>
    [JsonPropertyName("deal_id")]
    [JsonPropertyOrder(22)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DealId { get; set; }

    /// <summary>
    /// Localized deal terms sentence, for example
    /// "Questa offerta termina il 30 novembre 2026".
    /// </summary>
    [JsonPropertyName("deal_ends_text")]
    [JsonPropertyOrder(23)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DealEndsText { get; set; }

    /// <summary>
    /// Normalized calendar day from the deal terms popover
    /// (<c>YYYY-MM-DD</c>).
    /// </summary>
    [JsonPropertyName("deal_ends_on")]
    [JsonPropertyOrder(24)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DealEndsOn { get; set; }

    /// <summary>
    /// Whole days remaining until <see cref="DealEndsOn"/>.
    /// </summary>
    [JsonPropertyName("deal_ends_in_days")]
    [JsonPropertyOrder(25)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DealEndsInDays { get; set; }

    /// <summary>
    /// True when Amazon renders countdown labels for this deal. The live
    /// HH:MM:SS values are filled by client-side JavaScript and are not
    /// present in the server HTML.
    /// </summary>
    [JsonPropertyName("deal_has_countdown")]
    [JsonPropertyOrder(26)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DealHasCountdown { get; set; }

    /// <summary>
    /// Available style/size variants exposed by the product twister.
    /// Omitted when Amazon does not render a variant selector.
    /// </summary>
    [JsonPropertyName("variants")]
    [JsonPropertyOrder(27)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductVariants? Variants { get; set; }

    /// <summary>
    /// Numeric rating summary and star histogram. Populated by
    /// <c>--rating</c>; omitted otherwise.
    /// </summary>
    [JsonPropertyName("rating_details")]
    [JsonPropertyOrder(28)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RatingDetails? RatingDetails { get; set; }

    /// <summary>
    /// Top reviews rendered on the product page. Populated by
    /// <c>--reviews</c>; omitted otherwise.
    /// </summary>
    [JsonPropertyName("top_reviews")]
    [JsonPropertyOrder(29)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopReviews? TopReviews { get; set; }

    /// <summary>
    /// True when the product page exposes the Amazon's Choice badge.
    /// Omitted when the badge is not present.
    /// </summary>
    [JsonPropertyName("amazon_choice")]
    [JsonPropertyOrder(30)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AmazonChoice { get; set; }

    /// <summary>Localized badge label, for example "Amazon's Choice".</summary>
    [JsonPropertyName("amazon_choice_text")]
    [JsonPropertyOrder(31)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmazonChoiceText { get; set; }

    /// <summary>Optional "for ..." phrase shown next to the badge.</summary>
    [JsonPropertyName("amazon_choice_for")]
    [JsonPropertyOrder(32)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmazonChoiceFor { get; set; }

    /// <summary>
    /// Import charges / landed cost shown for cross-border shipping. Omitted
    /// when Amazon does not expose the block for the current destination.
    /// </summary>
    [JsonPropertyName("import_fees")]
    [JsonPropertyOrder(33)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImportFeesInfo? ImportFees { get; set; }

    /// <summary>
    /// Extra rows from Amazon's secondary specification tables. Kept separate
    /// from <see cref="Specs"/> so the primary technical specs stay clean.
    /// </summary>
    [JsonPropertyName("amazon_details")]
    [JsonPropertyOrder(34)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProductDetailExtra>? AmazonDetails { get; set; }

    /// <summary>
    /// Charger information from Amazon's safety/resources widget and the
    /// description bullets. Omitted when the page says nothing about it.
    /// </summary>
    [JsonPropertyName("charger")]
    [JsonPropertyOrder(35)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChargerInfo? Charger { get; set; }

    /// <summary>
    /// Product safety notices (choking hazards, small parts, magnets, age
    /// suitability, ...). Empty when Amazon does not render a warning.
    /// </summary>
    [JsonPropertyName("safety_warnings")]
    [JsonPropertyOrder(36)]
    public List<string> SafetyWarnings { get; set; } = [];

    /// <summary>
    /// Short product subtitle/differentiator shown by Amazon under the title
    /// (for example "Illuminazione personalizzabile, Bluetooth, ... - Grafite").
    /// Empty when the page does not expose the title differentiators block.
    /// </summary>
    [JsonPropertyName("subtitle")]
    [JsonPropertyOrder(37)]
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>
    /// Amazon best-seller rank from the page badge (for example 1 for
    /// "il #1 più venduto"). Omitted when the badge is not present.
    /// </summary>
    [JsonPropertyName("best_seller_rank")]
    [JsonPropertyOrder(38)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BestSellerRank { get; set; }

    /// <summary>Best-seller category shown by the badge, if any.</summary>
    [JsonPropertyName("best_seller_category")]
    [JsonPropertyOrder(39)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BestSellerCategory { get; set; }

    /// <summary>Localized badge label, for example "il #1 più venduto".</summary>
    [JsonPropertyName("best_seller_label")]
    [JsonPropertyOrder(40)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BestSellerLabel { get; set; }

    /// <summary>Absolute Amazon best-sellers URL behind the badge.</summary>
    [JsonPropertyName("best_seller_url")]
    [JsonPropertyOrder(41)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BestSellerUrl { get; set; }

    /// <summary>
    /// Manufacturer model name from Amazon's product details (for example
    /// "GV-N5060GAMING OC-8GD"). Omitted when the page does not expose it.
    /// </summary>
    [JsonPropertyName("model_name")]
    [JsonPropertyOrder(42)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelName { get; set; }
}
