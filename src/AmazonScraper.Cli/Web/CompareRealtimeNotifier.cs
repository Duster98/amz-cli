using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

public sealed class CompareChangedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "compare_changed";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("changed_at")]
    public string ChangedAt { get; set; } = string.Empty;
}

public interface ICompareRealtimeNotifier
{
    Task NotifyCompareChangedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

public sealed class CompareRealtimeNotifier : ICompareRealtimeNotifier
{
    private readonly IHubContext<LibraryHub> _hub;

    public CompareRealtimeNotifier(IHubContext<LibraryHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyCompareChangedAsync(
        long revision,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "CompareChanged",
            new CompareChangedEvent
            {
                Revision = revision,
                ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken);
}
