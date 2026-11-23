using System.Text;

namespace AmazonScraper.Cli.Web;

public sealed class WebOptions
{
    public int Port { get; init; }

    public bool Remote { get; init; }

    public bool Open { get; init; }

    public bool Desktop { get; init; }

    public bool Development { get; init; }

    public string? WebDirectory { get; init; }

    public string LogPath { get; init; } = WebPaths.LogPath();

    public int IdleMinutes { get; init; } = 30;

    public bool Quiet { get; init; }
}

public sealed class WebSessionState
{
    private long _lastActivityTicks = DateTime.UtcNow.Ticks;

    public WebSessionState(
        string? instanceId = null,
        string? token = null,
        DateTimeOffset? startedAt = null)
    {
        InstanceId = string.IsNullOrWhiteSpace(instanceId)
            ? WebSecurity.CreateSecret(24)
            : instanceId;
        Token = string.IsNullOrWhiteSpace(token)
            ? WebSecurity.CreateSecret(32)
            : token;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public string InstanceId { get; }

    public string Token { get; }

    public DateTimeOffset StartedAt { get; }

    public int Port { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public string LogPath { get; set; } = WebPaths.LogPath();

    public string Url => $"http://{Host}:{Port}/#t={Token}";

    public string LocalUrl => $"http://127.0.0.1:{Port}/#t={Token}";

    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartedAt;

    public DateTimeOffset LastActivity
    {
        get => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);
    }

    public void Touch() =>
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
}
