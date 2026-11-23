namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Persistent adaptive scheduler state. The anchor is fixed at the first
/// adaptive run, so windows do not depend on wall-clock alignment.
/// </summary>
public sealed class MonitorScheduleState
{
    public string? ScheduleHash { get; set; }

    public DateTimeOffset? AnchorAt { get; set; }

    public long WindowIndex { get; set; }

    public int WindowSeconds { get; set; }

    public DateTimeOffset? WindowStart { get; set; }

    public DateTimeOffset? WindowEnd { get; set; }

    public int SpentRequests { get; set; }

    public double Tokens { get; set; }

    public DateTimeOffset? LastRefillAt { get; set; }

    public Dictionary<string, MonitorUnitState> Units { get; set; } = [];
}

public sealed class MonitorUnitState
{
    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset? NextDueAt { get; set; }

    public int Runs { get; set; }

    public int Skipped { get; set; }

    public long? LastSkippedWindow { get; set; }

    public int LastCost { get; set; }
}
