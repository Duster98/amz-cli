using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.History;

namespace AmazonScraper.Cli.Web;

public static class WebJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

public sealed class SessionRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class SearchRequest
{
    public string Query { get; set; } = string.Empty;

    public string? Domain { get; set; }

    public List<string>? Domains { get; set; }

    public SearchHistoryRequest? History { get; set; }

    public int? Pages { get; set; }

    public int? Page { get; set; }

    public bool NoAds { get; set; }

    public bool Delivery { get; set; }

    public bool Enrich { get; set; }

    public string? Sort { get; set; }

    public int? PageDelayMs { get; set; }

    public int? EnrichDelayMs { get; set; }

    public double? TimeoutSeconds { get; set; }

    public bool? FillMissingPrices { get; set; }

    public string? DeliveryCountry { get; set; }

    public string? DeliveryPostalCode { get; set; }
}

public sealed class DealRequest
{
    public string? Domain { get; set; }

    public string? DealType { get; set; }

    public string? Category { get; set; }

    public int? Page { get; set; }

    public bool NoAds { get; set; } = true;

    public bool Delivery { get; set; } = true;

    public bool Enrich { get; set; }

    public int? PageDelayMs { get; set; }

    public int? EnrichDelayMs { get; set; }

    public double? TimeoutSeconds { get; set; }

    public string? DeliveryCountry { get; set; }

    public string? DeliveryPostalCode { get; set; }
}

public sealed class CompareItemInput
{
    public string? Domain { get; set; }

    public string? Asin { get; set; }
}

public sealed class CompareAddRequest
{
    public List<CompareItemInput> Items { get; set; } = [];

    public string? Domain { get; set; }

    public string? Asin { get; set; }

    public string? Source { get; set; }
}

public sealed class SavedGroupUpdateRequest
{    public string Name { get; set; } = string.Empty;

    public string Accent { get; set; } = "accent";

    public string? Description { get; set; }
}

public sealed class SavedGroupItemsRequest
{
    public List<string> ProductIds { get; set; } = [];

    public int Quantity { get; set; } = 1;
}

public sealed class SearchHistoryRequest
{
    public HistorySearchFilters? Filters { get; set; }

    /// <summary>
    /// Existing search entry to reuse when a search is re-run from the
    /// history page. New searches leave this null.
    /// </summary>
    public string? EntryId { get; set; }
}

public sealed class HistoryProductRequest
{
    public string Asin { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public HistoryProductSnapshot Snapshot { get; set; } = new();
}

