using System.Text.Json.Serialization;

namespace AmazonScraper.Cli.Monitoring;

public static class MonitorBindingKind
{
    public const string Product = "product";
    public const string Group = "group";
}

public sealed class MonitorBindingDocument
{
    public int Schema { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MonitorBinding> Bindings { get; set; } = [];
}

public sealed class MonitorBinding
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = MonitorBindingKind.Product;

    public string? ProductId { get; set; }

    public string? GroupId { get; set; }

    public string? Name { get; set; }

    public decimal? MaxPrice { get; set; }

    public decimal? DropPercent { get; set; }

    public decimal? RisePercent { get; set; }

    public string? Baseline { get; set; }

    public decimal? BaselinePrice { get; set; }

    public int? CooldownSeconds { get; set; }

    public decimal? TotalMax { get; set; }

    public bool? AllowIncomplete { get; set; }

    public string? Currency { get; set; }

    public Dictionary<string, decimal> Fx { get; set; } = [];

    public Dictionary<string, MonitorBindingItemRule> ItemRules { get; set; } = [];

    public MonitorRuntimeSettings? Runtime { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore]
    public bool IsGroup => string.Equals(
        Kind,
        MonitorBindingKind.Group,
        StringComparison.OrdinalIgnoreCase);

    public static string ProductBindingId(string productId) =>
        $"{MonitorBindingKind.Product}:{productId}";

    public static string GroupBindingId(string groupId) =>
        $"{MonitorBindingKind.Group}:{groupId}";
}

public sealed class MonitorBindingItemRule
{
    public decimal? MaxPrice { get; set; }

    public decimal? DropPercent { get; set; }

    public decimal? RisePercent { get; set; }

    public string? Baseline { get; set; }

    public decimal? BaselinePrice { get; set; }

    public int? CooldownSeconds { get; set; }
}


public sealed class MonitorRuntimeSettings
{
    public int? TimerSeconds { get; set; }

    public int? RequestDelaySeconds { get; set; }

    public int? JitterSeconds { get; set; }

    public string? SavePath { get; set; }

    public string? SaveFile { get; set; }

    public string? SaveFormat { get; set; }

    public string? Layout { get; set; }

    public bool? Notify { get; set; }

    public List<string> NotifyOn { get; set; } = [];

    public int? NotifyCooldownSeconds { get; set; }

    public bool? NoAutoUpdate { get; set; }
}
