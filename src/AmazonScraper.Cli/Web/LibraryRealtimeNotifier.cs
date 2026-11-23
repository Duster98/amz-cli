using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

public sealed class LibraryChangedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "library_changed";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("changed_at")]
    public string ChangedAt { get; set; } = string.Empty;
}

public interface ILibraryRealtimeNotifier
{
    Task NotifyLibraryChangedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

public sealed class LibraryRealtimeNotifier : ILibraryRealtimeNotifier
{
    private readonly IHubContext<LibraryHub> _hub;

    public LibraryRealtimeNotifier(IHubContext<LibraryHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyLibraryChangedAsync(
        long revision,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "LibraryChanged",
            new LibraryChangedEvent
            {
                Revision = revision,
                ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken);
}
