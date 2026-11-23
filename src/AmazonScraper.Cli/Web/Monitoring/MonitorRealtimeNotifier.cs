using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web.Monitoring;

public sealed class MonitorChangedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "monitor_changed";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("changed_at")]
    public string ChangedAt { get; set; } = string.Empty;
}

public interface IMonitorRealtimeNotifier
{
    Task NotifyChangedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

public sealed class MonitorRealtimeNotifier : IMonitorRealtimeNotifier
{
    private readonly IHubContext<LibraryHub> _hub;

    public MonitorRealtimeNotifier(IHubContext<LibraryHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyChangedAsync(
        long revision,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "MonitorChanged",
            new MonitorChangedEvent
            {
                Revision = revision,
                ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken);
}
