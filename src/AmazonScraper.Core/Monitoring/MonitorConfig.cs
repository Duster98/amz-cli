using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Raw JSON configuration. Keep this deliberately close to the documented
/// schema; validation and domain/currency resolution happen in
/// <see cref="MonitorPlanBuilder"/>.
/// </summary>
public sealed class MonitorConfig
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("timer_seconds")]
    public int? TimerSeconds { get; set; }

    [JsonPropertyName("request_delay_seconds")]
    public int? RequestDelaySeconds { get; set; }

    [JsonPropertyName("save")]
    public MonitorSaveConfig? Save { get; set; }

    [JsonPropertyName("notifications")]
    public MonitorNotificationConfig? Notifications { get; set; }

    [JsonPropertyName("schedule")]
    public MonitorScheduleConfig? Schedule { get; set; }

    [JsonPropertyName("items")]
    public List<MonitorItemConfig> Items { get; set; } = [];

    [JsonPropertyName("groups")]
    public List<MonitorGroupConfig> Groups { get; set; } = [];
}

public sealed class MonitorScheduleConfig
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("window_seconds")]
    public int? WindowSeconds { get; set; }

    [JsonPropertyName("max_requests")]
    public int? MaxRequests { get; set; }

    [JsonPropertyName("min_gap_seconds")]
    public int? MinGapSeconds { get; set; }

    [JsonPropertyName("max_gap_seconds")]
    public int? MaxGapSeconds { get; set; }
}

public sealed class MonitorSaveConfig
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }
}

public sealed class MonitorNotificationConfig
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("on")]
    public List<string> On { get; set; } = [];

    [JsonPropertyName("cooldown_seconds")]
    public int? CooldownSeconds { get; set; }
}

public class MonitorItemConfig
{
    [JsonPropertyName("asin")]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("max_price")]
    public decimal? MaxPrice { get; set; }

    [JsonPropertyName("drop_percent")]
    public decimal? DropPercent { get; set; }

    [JsonPropertyName("rise_percent")]
    public decimal? RisePercent { get; set; }

    [JsonPropertyName("baseline")]
    public string? Baseline { get; set; }

    [JsonPropertyName("baseline_price")]
    public decimal? BaselinePrice { get; set; }

    [JsonPropertyName("cooldown_seconds")]
    public int? CooldownSeconds { get; set; }

    [JsonPropertyName("min_gap_seconds")]
    public int? MinGapSeconds { get; set; }

    [JsonPropertyName("max_gap_seconds")]
    public int? MaxGapSeconds { get; set; }
}

public sealed class MonitorGroupItemConfig : MonitorItemConfig
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
}

public sealed class MonitorGroupConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("total_max")]
    public decimal? TotalMax { get; set; }

    [JsonPropertyName("allow_incomplete")]
    public bool? AllowIncomplete { get; set; }

    [JsonPropertyName("cooldown_seconds")]
    public int? CooldownSeconds { get; set; }

    [JsonPropertyName("min_gap_seconds")]
    public int? MinGapSeconds { get; set; }

    [JsonPropertyName("max_gap_seconds")]
    public int? MaxGapSeconds { get; set; }

    [JsonPropertyName("items")]
    public List<MonitorGroupItemConfig> Items { get; set; } = [];
}

public static class MonitorConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static MonitorConfig Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MonitorException(
                $"Unable to read configuration '{path}': {ex.Message}");
        }

        try
        {
            return JsonSerializer.Deserialize<MonitorConfig>(json, Options)
                ?? throw new MonitorException(
                    $"Configuration '{path}' is empty or invalid.");
        }
        catch (JsonException ex)
        {
            var position = ex.LineNumber is { } line
                ? $" (line {line + 1}, column {(ex.BytePositionInLine ?? 0) + 1})"
                : string.Empty;
            throw new MonitorException(
                $"Invalid configuration{position}: {ex.Message}");
        }
    }
}
