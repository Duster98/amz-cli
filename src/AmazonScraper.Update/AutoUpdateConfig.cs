using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public sealed class AutoUpdateConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("interval_seconds")]
    public int IntervalSeconds { get; set; } = 3600;

    [JsonPropertyName("auto_apply")]
    public bool AutoApply { get; set; } = true;

    [JsonPropertyName("restart_monitors")]
    public bool RestartMonitors { get; set; } = true;

    [JsonPropertyName("defer_foreground_monitor")]
    public bool DeferForegroundMonitor { get; set; } = true;

    [JsonPropertyName("notify")]
    public bool Notify { get; set; } = true;

    public void Validate()
    {
        if (Channel is not ("stable" or "beta"))
        {
            throw new UpdateException("Invalid auto-update channel.");
        }
        if (IntervalSeconds is < 0 or > 604_800)
        {
            throw new UpdateException(
                "Invalid auto-update interval: use 0-604800 seconds.");
        }
    }

    public static AutoUpdateConfig Load(string? path = null)
    {
        path ??= UpdatePaths.ConfigPath();
        try
        {
            if (!File.Exists(path))
            {
                return new AutoUpdateConfig();
            }

            var config = JsonSerializer.Deserialize<AutoUpdateConfig>(
                File.ReadAllText(path),
                UpdateJson.Options) ?? new AutoUpdateConfig();
            config.Validate();
            return config;
        }
        catch (Exception)
        {
            return new AutoUpdateConfig();
        }
    }

    public void Save(string? path = null)
    {
        Validate();
        path ??= UpdatePaths.ConfigPath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(this, UpdateJson.Options));
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class AutoUpdateState
{
    [JsonPropertyName("last_check")]
    public DateTimeOffset? LastCheck { get; set; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("last_status")]
    public string? LastStatus { get; set; }

    [JsonPropertyName("last_trigger")]
    public string? LastTrigger { get; set; }

    public static AutoUpdateState Read(string? path = null)
    {
        path ??= UpdatePaths.LastCheckPath();
        try
        {
            if (!File.Exists(path))
            {
                return new AutoUpdateState();
            }

            return JsonSerializer.Deserialize<AutoUpdateState>(
                File.ReadAllText(path),
                UpdateJson.Options) ?? new AutoUpdateState();
        }
        catch (Exception)
        {
            return new AutoUpdateState();
        }
    }

    public static bool IsDue(
        AutoUpdateConfig config,
        DateTimeOffset? now = null,
        string? path = null)
    {
        if (!config.Enabled)
        {
            return false;
        }

        var state = Read(path);
        if (state.LastCheck is null || config.IntervalSeconds <= 0)
        {
            return true;
        }

        return (now ?? DateTimeOffset.UtcNow) - state.LastCheck.Value
            >= TimeSpan.FromSeconds(config.IntervalSeconds);
    }

    public static void MarkChecked(
        UpdateCheckResult result,
        string trigger,
        string? path = null)
    {
        Write(
            path,
            new AutoUpdateState
            {
                LastCheck = DateTimeOffset.UtcNow,
                LatestVersion = result.LatestVersion,
                LastStatus = result.Status.ToString(),
                LastTrigger = trigger,
            });
    }

    public static void MarkFailed(
        string trigger,
        string error,
        string? path = null)
    {
        Write(
            path,
            new AutoUpdateState
            {
                LastCheck = DateTimeOffset.UtcNow,
                LastStatus = "error: " + error,
                LastTrigger = trigger,
            });
    }

    private static void Write(string? path, AutoUpdateState state)
    {
        path ??= UpdatePaths.LastCheckPath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(state, UpdateJson.Options));
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed record AutoUpdateContext(
    string Trigger,
    IReadOnlyList<int> WaitForProcessIds,
    bool RestartMonitors,
    bool DeferForegroundMonitor);
