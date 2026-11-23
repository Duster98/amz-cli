using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// One review rendered on the product detail page. Amazon only exposes the
/// "top reviews" without authentication: normally eight local reviews plus up
/// to five international ones.
/// </summary>
public sealed class ProductReview
{
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    [JsonPropertyOrder(1)]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    [JsonPropertyOrder(2)]
    public string Rating { get; set; } = string.Empty;

    [JsonPropertyName("rating_value")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RatingValue { get; set; }

    [JsonPropertyName("title")]
    [JsonPropertyOrder(4)]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    [JsonPropertyOrder(5)]
    public string Date { get; set; } = string.Empty;

    /// <summary>"local" or "international", based on the Amazon list.</summary>
    [JsonPropertyName("source")]
    [JsonPropertyOrder(6)]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("verified_purchase")]
    [JsonPropertyOrder(7)]
    public bool VerifiedPurchase { get; set; }

    [JsonPropertyName("helpful")]
    [JsonPropertyOrder(8)]
    public string Helpful { get; set; } = string.Empty;

    [JsonPropertyName("variant")]
    [JsonPropertyOrder(9)]
    public string Variant { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    [JsonPropertyOrder(10)]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    [JsonPropertyOrder(11)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Image URLs are populated only with <c>--review-images</c>; the property
    /// is omitted from JSON when image collection was not requested.
    /// </summary>
    [JsonPropertyName("images")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Images { get; set; }
}
