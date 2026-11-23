namespace AmazonScraper.Cli.Monitoring;

public sealed class MonitorListEntry
{
    public string? Id { get; set; }

    public string? BindingId { get; set; }

    public int Pid { get; set; }

    public string Status { get; set; } = "stale";

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

    public List<string> Domains { get; set; } = [];

    public int Targets { get; set; }

    public int TimerSeconds { get; set; }

    public string? ScheduleMode { get; set; }

    public string? RuntimeHash { get; set; }

    public int? WindowSeconds { get; set; }

    public int? MaxRequests { get; set; }

    public int? MinGapSeconds { get; set; }

    public int? MaxGapSeconds { get; set; }

    public string? SavePath { get; set; }

    public string? SaveFile { get; set; }

    public List<string> DataFiles { get; set; } = [];

    public string? LogPath { get; set; }

    public string? PidFile { get; set; }
}

public sealed record MonitorDiscoveryResult(
    IReadOnlyList<MonitorListEntry> Entries,
    int StaleCount,
    int PrunedCount);
