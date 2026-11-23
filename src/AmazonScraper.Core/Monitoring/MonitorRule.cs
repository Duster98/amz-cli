namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Thresholds evaluated for one product. All rules are optional and can be
/// active at the same time.
/// </summary>
public sealed class MonitorRule
{
    public decimal? MaxPrice { get; init; }

    public decimal? DropPercent { get; init; }

    public decimal? RisePercent { get; init; }

    public PriceBaseline Baseline { get; init; } = PriceBaseline.Original;

    public decimal? BaselinePrice { get; init; }

    public int CooldownSeconds { get; init; } = 21_600;

    public bool HasThreshold =>
        MaxPrice is not null || DropPercent is not null || RisePercent is not null;
}
