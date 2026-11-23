using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Monitoring;

public sealed class MonitorItemObservation
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("record_type")]
    public string RecordType => "item";

    [JsonPropertyName("observed_at")]
    public string ObservedAt { get; set; } = string.Empty;

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("cycle")]
    public int Cycle { get; set; }

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("asin")]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("price_amount")]
    public decimal? PriceAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("original_price")]
    public string OriginalPrice { get; set; } = string.Empty;

    [JsonPropertyName("original_price_amount")]
    public decimal? OriginalPriceAmount { get; set; }

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = string.Empty;

    [JsonPropertyName("coupon")]
    public string Coupon { get; set; } = string.Empty;

    [JsonPropertyName("deal")]
    public string? Deal { get; set; }

    [JsonPropertyName("deal_id")]
    public string? DealId { get; set; }

    [JsonPropertyName("deal_ends_text")]
    public string? DealEndsText { get; set; }

    [JsonPropertyName("deal_ends_on")]
    public string? DealEndsOn { get; set; }

    [JsonPropertyName("deal_ends_in_days")]
    public int? DealEndsInDays { get; set; }

    [JsonPropertyName("deal_has_countdown")]
    public bool? DealHasCountdown { get; set; }

    [JsonPropertyName("availability")]
    public string Availability { get; set; } = string.Empty;

    [JsonPropertyName("seller")]
    public string Seller { get; set; } = string.Empty;

    [JsonPropertyName("shipped_by")]
    public string ShippedBy { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public string Rating { get; set; } = string.Empty;

    [JsonPropertyName("reviews")]
    public string Reviews { get; set; } = string.Empty;

    [JsonPropertyName("triggered_rules")]
    public List<string> TriggeredRules { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MonitorGroupItemObservation
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("asin")]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    [JsonPropertyName("price_amount")]
    public decimal? PriceAmount { get; set; }

    [JsonPropertyName("original_currency")]
    public string OriginalCurrency { get; set; } = string.Empty;

    [JsonPropertyName("original_price_amount")]
    public decimal? OriginalPriceAmount { get; set; }

    [JsonPropertyName("original_line_total")]
    public decimal? OriginalLineTotal { get; set; }

    [JsonPropertyName("fx_rate")]
    public decimal? FxRate { get; set; }

    [JsonPropertyName("line_total")]
    public decimal? LineTotal { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MonitorGroupObservation
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("record_type")]
    public string RecordType => "group";

    [JsonPropertyName("observed_at")]
    public string ObservedAt { get; set; } = string.Empty;

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("cycle")]
    public int Cycle { get; set; }

    [JsonPropertyName("group_id")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = [];

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("total_max")]
    public decimal? TotalMax { get; set; }

    [JsonPropertyName("total")]
    public decimal? Total { get; set; }

    [JsonPropertyName("complete")]
    public bool Complete { get; set; }

    [JsonPropertyName("items_count")]
    public int ItemsCount { get; set; }

    [JsonPropertyName("available_count")]
    public int AvailableCount { get; set; }

    [JsonPropertyName("missing_count")]
    public int MissingCount { get; set; }

    [JsonPropertyName("triggered_rules")]
    public List<string> TriggeredRules { get; set; } = [];

    [JsonPropertyName("items")]
    public List<MonitorGroupItemObservation> Items { get; set; } = [];
}

public sealed class MonitorAlert
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("record_type")]
    public string RecordType => "alert";

    [JsonPropertyName("alerted_at")]
    public string AlertedAt { get; set; } = string.Empty;

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("cycle")]
    public int Cycle { get; set; }

    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("asin")]
    public string? Asin { get; set; }

    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public decimal Threshold { get; set; }

    [JsonPropertyName("observed")]
    public decimal Observed { get; set; }

    [JsonPropertyName("complete")]
    public bool? Complete { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
