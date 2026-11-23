using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AmazonScraper.Cli.Web;

/// <summary>
/// Tracks the long-lived SignalR connections so shutdown can abort them
/// immediately instead of waiting for Kestrel's graceful connection drain.
/// </summary>
public sealed class LibraryHubConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _contexts =
        new(StringComparer.Ordinal);
    private volatile bool _stopping;

    public bool Stopping => _stopping;

    public void Add(HubCallerContext context)
    {
        if (_stopping)
        {
            context.Abort();
            return;
        }
        _contexts[context.ConnectionId] = context;
    }

    public void Remove(string connectionId) =>
        _contexts.TryRemove(connectionId, out _);

    public void AbortAll()
    {
        _stopping = true;
        foreach (var pair in _contexts)
        {
            try
            {
                pair.Value.Abort();
            }
            catch (Exception)
            {
                // The connection may have closed between enumeration and abort.
            }
        }
        _contexts.Clear();
    }
}
