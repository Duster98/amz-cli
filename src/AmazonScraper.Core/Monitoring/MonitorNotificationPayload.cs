namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Structured data stored next to a native Windows toast. The notification
/// helper reads it when the toast is clicked and renders the detail window.
/// </summary>
public sealed class MonitorNotificationPayload
{
    public string Kind { get; set; } = "alert";

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string CreatedAt { get; set; } = string.Empty;

    public string? GroupId { get; set; }

    public string? GroupName { get; set; }

    public decimal? GroupTotal { get; set; }

    public decimal? GroupThreshold { get; set; }

    public string? Currency { get; set; }

    public string? UpdateStatus { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public bool? MonitorsRestarted { get; set; }

    public string? BackupPath { get; set; }

    public string? LogPath { get; set; }

    public string? Trigger { get; set; }

    public string? Error { get; set; }

    public List<MonitorNotificationItem> Items { get; set; } = [];
}

public sealed class MonitorNotificationItem
{
    public string? Domain { get; set; }

    public string? Asin { get; set; }

    public string? Title { get; set; }

    public string? Url { get; set; }

    public string? Type { get; set; }

    public string? Message { get; set; }

    public decimal? Threshold { get; set; }

    public decimal? Observed { get; set; }

    public int? Quantity { get; set; }

    public decimal? LineTotal { get; set; }

    public string? Currency { get; set; }

    public string? Price { get; set; }

    public string? OriginalPrice { get; set; }

    public string? Discount { get; set; }

    public string? Coupon { get; set; }

    public string? Deal { get; set; }

    public string? DealId { get; set; }

    public string? DealEndsText { get; set; }

    public string? DealEndsOn { get; set; }

    public string? Error { get; set; }
}
