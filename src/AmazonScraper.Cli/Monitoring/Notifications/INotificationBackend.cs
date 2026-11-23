using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

public sealed record NotificationSendResult(
    bool Success,
    string? Detail = null,
    string? Error = null);

public interface INotificationBackend
{
    string Name { get; }

    Task<NotificationSendResult> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken);
}
