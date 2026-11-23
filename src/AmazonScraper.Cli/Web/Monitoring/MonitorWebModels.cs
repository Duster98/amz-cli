using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Web.Monitoring;

public sealed class MonitorBindingRequest
{
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

    public Dictionary<string, MonitorBindingItemRule>? ItemRules { get; set; }

    public MonitorRuntimeSettings? Runtime { get; set; }

    public bool Start { get; set; }
}

public sealed class MonitorStartRequest
{
    public int? TimerSeconds { get; set; }

    public int? RequestDelaySeconds { get; set; }

    public bool? Notify { get; set; }
}

public sealed class MonitorTargetView
{
    public string Kind { get; set; } = string.Empty;

    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Image { get; set; }

    public string? Asin { get; set; }

    public string? Domain { get; set; }

    public string? Marketplace { get; set; }

    public string? Url { get; set; }

    public List<string> Domains { get; set; } = [];

    public string? Currency { get; set; }

    public decimal? PriceLast { get; set; }

    public int Products { get; set; }

    public bool Available { get; set; } = true;

    public string? Error { get; set; }
}

public sealed class MonitorRunView
{
    public string? BindingId { get; set; }

    public string Status { get; set; } = "stopped";

    public int Pid { get; set; }

    public string? StartedAt { get; set; }

    public string? LastSeenAt { get; set; }

    public string? Mode { get; set; }

    public string? Startup { get; set; }

    public bool StartupEnabled { get; set; }

    public bool? StartupRegistered { get; set; }

    public string? StartupName { get; set; }

    public string? StartupId { get; set; }

    public string? StartupConfigPath { get; set; }

    public string? StartupCommandPreview { get; set; }

    public int Targets { get; set; }

    public int TimerSeconds { get; set; }

    public string? ScheduleMode { get; set; }

    public string? RuntimeHash { get; set; }

    public int? WindowSeconds { get; set; }

    public int? MaxRequests { get; set; }

    public int? MinGapSeconds { get; set; }

    public int? MaxGapSeconds { get; set; }

    public string? LogPath { get; set; }

    public string? PidFile { get; set; }

    public string? SavePath { get; set; }

    public string? SaveFile { get; set; }

    public List<string> DataFiles { get; set; } = [];
}

public sealed class MonitorStartupView
{
    public bool Available { get; set; }

    public string? Backend { get; set; }

    public string? Error { get; set; }

    public bool Configured { get; set; }

    public bool Enabled { get; set; }

    public bool Registered { get; set; }

    public bool Managed { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? CommandPreview { get; set; }

    public string? Message { get; set; }
}

public sealed class MonitorNotificationTestResult
{
    public bool Success { get; set; }

    public string Backend { get; set; } = "none";

    public string? Detail { get; set; }

    public string? Error { get; set; }
}

public sealed class MonitorRuntimeView
{
    public int TimerSeconds { get; set; }

    public int RequestDelaySeconds { get; set; }

    public int JitterSeconds { get; set; }

    public string SavePath { get; set; } = string.Empty;

    public string SaveFile { get; set; } = "amz-monitor";

    public string SaveFormat { get; set; } = "json";

    public string Layout { get; set; } = "auto";

    public bool Notify { get; set; }

    public List<string> NotifyOn { get; set; } = [];

    public int? NotifyCooldownSeconds { get; set; }

    public bool NoAutoUpdate { get; set; }
}

public sealed class MonitorStatsView
{
    public decimal? FirstPrice { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public decimal? LastPrice { get; set; }

    public decimal? LastTotal { get; set; }

    public decimal? MinTotal { get; set; }

    public decimal? MaxTotal { get; set; }

    public string? Currency { get; set; }

    public int Observations { get; set; }

    public int AlertedRules { get; set; }

    public string? LastSeenAt { get; set; }
}

public sealed class MonitorBindingView
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = MonitorBindingKind.Product;

    public string? Name { get; set; }

    public string? ProductId { get; set; }

    public string? GroupId { get; set; }

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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MonitorTargetView Target { get; set; } = new();

    public MonitorRuntimeView Runtime { get; set; } = new();

    public MonitorStartupView Startup { get; set; } = new();

    public MonitorRunView? Run { get; set; }

    public MonitorStatsView Stats { get; set; } = new();

    public string Status { get; set; } = "stopped";

    public bool CanStart { get; set; }

    public bool CanStop { get; set; }

    public bool RestartRequired { get; set; }

    public string? Error { get; set; }
}

public sealed class MonitorExternalRunView
{
    public MonitorRunView Run { get; set; } = new();

    public string? Title { get; set; }
}

public sealed class MonitorOverviewResponse
{
    public int Bindings { get; set; }

    public int Running { get; set; }

    public int Alerts { get; set; }

    public bool StartupAvailable { get; set; }

    public string? StartupBackend { get; set; }

    public string? StartupError { get; set; }

    public IReadOnlyList<MonitorBindingView> Monitors { get; set; } = [];

    public IReadOnlyList<MonitorExternalRunView> OtherRuns { get; set; } = [];
}

public sealed class MonitorThresholdView
{
    public string Label { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public string? Currency { get; set; }
}

public sealed class MonitorSeriesResponse
{
    public string BindingId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public IReadOnlyList<MonitorSeriesPoint> Points { get; set; } = [];

    public IReadOnlyList<MonitorSeriesAlert> Alerts { get; set; } = [];

    public IReadOnlyList<MonitorThresholdView> Thresholds { get; set; } = [];

    public int Observations { get; set; }

    public bool Truncated { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = [];

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
