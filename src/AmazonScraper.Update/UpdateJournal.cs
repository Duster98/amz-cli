using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public static class UpdatePhase
{
    public const string Checking = "checking";
    public const string AutoCheck = "auto_check";
    public const string Preparing = "preparing";
    public const string Available = "available";
    public const string Downloading = "downloading";
    public const string BackedUp = "backed_up";
    public const string Staging = "staging";
    public const string WaitingForCli = "waiting_for_cli";
    public const string StoppingMonitors = "stopping_monitors";
    public const string Applying = "applying";
    public const string VerifyingInstall = "verifying_install";
    public const string RestartingMonitors = "restarting_monitors";
    public const string Completed = "completed";
    public const string RolledBack = "rolled_back";
    public const string DeferredForeground = "deferred_foreground";
    public const string Failed = "failed";
}

public sealed class UpdateJournal
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = UpdatePhase.Checking;

    [JsonPropertyName("from_version")]
    public string? FromVersion { get; set; }

    [JsonPropertyName("to_version")]
    public string? ToVersion { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("install_dir")]
    public string? InstallDir { get; set; }

    [JsonPropertyName("backup_path")]
    public string? BackupPath { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("backup_sha256")]
    public string? BackupSha256 { get; set; }

    [JsonPropertyName("staging_dir")]
    public string? StagingDir { get; set; }

    [JsonPropertyName("rollback_dir")]
    public string? RollbackDir { get; set; }

    [JsonPropertyName("helper_path")]
    public string? HelperPath { get; set; }

    [JsonPropertyName("helper_process_id")]
    public int? HelperProcessId { get; set; }

    [JsonPropertyName("auto_trigger")]
    public string? AutoTrigger { get; set; }

    [JsonPropertyName("monitors_planned")]
    public List<string> MonitorsPlanned { get; set; } = [];

    [JsonPropertyName("monitors_stopped")]
    public List<string> MonitorsStopped { get; set; } = [];

    [JsonPropertyName("monitors_restarted")]
    public List<string> MonitorsRestarted { get; set; } = [];

    [JsonPropertyName("restart_errors")]
    public List<string> RestartErrors { get; set; } = [];

    [JsonPropertyName("web_pid")]
    public int? WebPid { get; set; }

    [JsonPropertyName("web_restart_error")]
    public string? WebRestartError { get; set; }

    [JsonPropertyName("web_restart_attempted_at")]
    public DateTimeOffset? WebRestartAttemptedAt { get; set; }

    [JsonPropertyName("deferred_reason")]
    public string? DeferredReason { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class UpdateJournalStore
{
    public static UpdateJournal? Read(string? path = null)
    {
        path ??= UpdatePaths.JournalPath();
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdateJournal>(json, UpdateJson.Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Write(UpdateJournal journal, string? path = null)
    {
        path ??= UpdatePaths.JournalPath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        journal.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(journal, UpdateJson.Options);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static void Delete(string? path = null)
    {
        try
        {
            File.Delete(path ?? UpdatePaths.JournalPath());
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
