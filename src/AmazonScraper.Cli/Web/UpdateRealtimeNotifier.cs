using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

public sealed class UpdateChangedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "update_changed";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "idle";

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class UpdateProgressEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "update_progress";

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "preparing";

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("percent")]
    public int? Percent { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("total_bytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

public interface IUpdateRealtimeNotifier
{
    Task NotifyChangedAsync(
        UpdateChangedEvent change,
        CancellationToken cancellationToken = default);

    Task NotifyProgressAsync(
        UpdateProgressEvent progress,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateRealtimeNotifier : IUpdateRealtimeNotifier
{
    private readonly IHubContext<LibraryHub> _hub;

    public UpdateRealtimeNotifier(IHubContext<LibraryHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyChangedAsync(
        UpdateChangedEvent change,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "UpdateChanged",
            change,
            cancellationToken);

    public Task NotifyProgressAsync(
        UpdateProgressEvent progress,
        CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            "UpdateProgress",
            progress,
            cancellationToken);
}
