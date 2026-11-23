using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

/// <summary>
/// SignalR hub used only for server-to-client library, history, and update
/// events. Commands stay on the normal authenticated HTTP API.
/// </summary>
public sealed class LibraryHub : Hub
{
    private readonly LibraryHubConnections _connections;

    public LibraryHub(LibraryHubConnections connections)
    {
        _connections = connections;
    }

    public override async Task OnConnectedAsync()
    {
        _connections.Add(Context);
        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            return;
        }
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
