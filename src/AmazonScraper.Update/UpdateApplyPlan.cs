using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public sealed class UpdateApplyFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "0644";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class UpdateApplyPlan
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("install_dir")]
    public string InstallDir { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("from_version")]
    public string FromVersion { get; set; } = string.Empty;

    [JsonPropertyName("to_version")]
    public string ToVersion { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("release_published_at")]
    public DateTimeOffset? ReleasePublishedAt { get; set; }

    [JsonPropertyName("manifest")]
    public string Manifest { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("backup_path")]
    public string BackupPath { get; set; } = string.Empty;

    [JsonPropertyName("backup_sha256")]
    public string BackupSha256 { get; set; } = string.Empty;

    [JsonPropertyName("staging_dir")]
    public string StagingDir { get; set; } = string.Empty;

    [JsonPropertyName("rollback_dir")]
    public string RollbackDir { get; set; } = string.Empty;

    [JsonPropertyName("require_curl")]
    public bool RequireCurl { get; set; }

    [JsonPropertyName("parent_process_id")]
    public int ParentProcessId { get; set; }

    [JsonPropertyName("restart_monitors")]
    public bool RestartMonitors { get; set; }

    [JsonPropertyName("defer_foreground_monitor")]
    public bool DeferForegroundMonitor { get; set; } = true;

    [JsonPropertyName("force_monitor_restart")]
    public bool ForceMonitorRestart { get; set; }

    [JsonPropertyName("restart_web")]
    public bool RestartWeb { get; set; }

    [JsonPropertyName("web_resume_path")]
    public string? WebResumePath { get; set; }

    [JsonPropertyName("wait_for_process_ids")]
    public List<int> WaitForProcessIds { get; set; } = [];

    [JsonPropertyName("auto_trigger")]
    public string? AutoTrigger { get; set; }

    [JsonPropertyName("files")]
    public List<UpdateApplyFile> Files { get; set; } = [];
}

public static class UpdateApplyPlanStore
{
    public static void Write(string path, UpdateApplyPlan plan)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(plan, UpdateJson.Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static UpdateApplyPlan Read(string path)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<UpdateApplyPlan>(
                File.ReadAllText(path),
                UpdateJson.Options);
            return plan ?? throw new UpdateException("Empty update plan.");
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateException($"Invalid update plan: {ex.Message}", ex);
        }
    }
}
