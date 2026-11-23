using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Monitoring;

public sealed class MonitorState
{
    public int SchemaVersion { get; set; } = 1;

    public string? ConfigHash { get; set; }

    public Dictionary<string, MonitorTargetState> Targets { get; set; } = [];

    public Dictionary<string, MonitorGroupState> Groups { get; set; } = [];

    public MonitorScheduleState? Schedule { get; set; }

    public MonitorNotificationState Notifications { get; set; } = new();
}

public sealed class MonitorNotificationState
{
    public string? LastErrorDigestKey { get; set; }

    public DateTimeOffset? LastErrorDigestAt { get; set; }

    public Dictionary<string, MonitorDomainNotificationState> Domains { get; set; } = [];
}

public sealed class MonitorDomainNotificationState
{
    public DateTimeOffset? LastBlockAt { get; set; }

    public string? LastBlockMessage { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class MonitorTargetState
{
    public decimal? FirstSeenPrice { get; set; }

    public decimal? MinSeenPrice { get; set; }

    public decimal? MaxSeenPrice { get; set; }

    public decimal? LastPrice { get; set; }

    public int Observations { get; set; }

    public string? Currency { get; set; }

    public string? GroupId { get; set; }

    public string? Label { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public Dictionary<string, MonitorRuleState> Rules { get; set; } = [];
}

public sealed class MonitorGroupState
{
    public string? Name { get; set; }

    public string? Currency { get; set; }

    public int Items { get; set; }

    public decimal? LastTotal { get; set; }

    public decimal? MinTotal { get; set; }

    public decimal? MaxTotal { get; set; }

    public int Observations { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public Dictionary<string, MonitorRuleState> Rules { get; set; } = [];
}

public sealed class MonitorRuleState
{
    public bool LastCondition { get; set; }

    public DateTimeOffset? LastAlertAt { get; set; }
}

public static class MonitorStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static MonitorState? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MonitorState>(
                File.ReadAllText(path),
                Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static MonitorState Load(string path, Action<string>? warning = null)
    {
        if (!File.Exists(path))
        {
            return new MonitorState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MonitorState>(json, Options)
                ?? new MonitorState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var backup = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            try
            {
                File.Move(path, backup, overwrite: true);
                warning?.Invoke(
                    $"State unreadable, saved to '{backup}': {ex.Message}");
            }
            catch (Exception)
            {
                warning?.Invoke($"State unreadable: {ex.Message}");
            }

            return new MonitorState();
        }
    }

    public static async Task SaveAsync(
        string path,
        MonitorState state,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(state, Options);
        await File.WriteAllTextAsync(temporary, json, cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}
