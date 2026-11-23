using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

public sealed class HistoryChangedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "history_changed";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("changed_at")]
    public string ChangedAt { get; set; } = string.Empty;
}

public interface IHistoryRealtimeNotifier
{
    Task NotifyHistoryChangedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

public sealed class HistoryRealtimeNotifier : IHistoryRealtimeNotifier
{
    private readonly IHubContext<LibraryHub> _hub;

    public HistoryRealtimeNotifier(IHubContext<LibraryHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyHistoryChangedAsync(
        long revision,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "HistoryChanged",
            new HistoryChangedEvent
            {
                Revision = revision,
                ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken);
}
