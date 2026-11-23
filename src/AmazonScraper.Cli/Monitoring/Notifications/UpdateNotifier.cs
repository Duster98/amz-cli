using System.Diagnostics;
using System.Text.Json;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Monitoring.Notifications;

/// <summary>
/// Sends the post-update toast from a detached helper process. The updater
/// helper must not wait for a click (Linux notifications can wait for an
/// action), so the update lock is released immediately.
/// </summary>
public static class UpdateNotifier
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static Task TryNotifyAsync(
        UpdateJournal journal,
        string? installDir = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(journal.AutoTrigger))
        {
            return Task.CompletedTask;
        }

        var config = AutoUpdateConfig.Load();
        if (!config.Notify)
        {
            return Task.CompletedTask;
        }

        var (title, body, severity) = Describe(journal);
        bool? monitorsRestarted = journal.MonitorsPlanned.Count == 0
            ? null
            : journal.MonitorsRestarted.Count > 0;
        var payload = MonitorNotificationPayloadFactory.Update(
            title,
            body,
            journal.Phase,
            journal.FromVersion,
            journal.ToVersion,
            monitorsRestarted,
            journal.BackupPath,
            UpdatePaths.LogPath(),
            journal.Error,
            journal.AutoTrigger,
            DateTimeOffset.UtcNow,
            journal.Notes);

        return Task.Run(
            () => StartDetached(payload, installDir, cancellationToken),
            cancellationToken);
    }

    internal static INotificationBackend? CreateBackend(string? installDir)
    {
        if (OperatingSystem.IsWindows())
        {
            var native = !string.IsNullOrWhiteSpace(installDir)
                ? WindowsNativeNotificationBackend.Detect(installDir)
                : WindowsNativeNotificationBackend.Detect();
            if (native is not null)
            {
                return native;
            }
            return WindowsNotificationBackend.Detect();
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxNotificationBackend.Detect();
        }

        return null;
    }

    internal static async Task SendAsync(
        MonitorNotificationPayload payload,
        string? installDir,
        CancellationToken cancellationToken)
    {
        var backend = CreateBackend(installDir);
        if (backend is null)
        {
            return;
        }

        var severity = payload.UpdateStatus switch
        {
            "rolled_back" or "failed" => MonitorNotificationSeverity.Error,
            "deferred_foreground" => MonitorNotificationSeverity.Warning,
            _ => MonitorNotificationSeverity.Info,
        };
        await backend.SendAsync(
                new MonitorNotificationMessage(
                    payload.Title,
                    payload.Body,
                    severity,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void StartDetached(
        MonitorNotificationPayload payload,
        string? installDir,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.Combine(
                UpdatePaths.UpdatesDirectory(),
                "notifications");
            Directory.CreateDirectory(directory);
            CleanupOldPayloads(directory);
            var payloadPath = Path.Combine(
                directory,
                $"update-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.WriteAllText(
                payloadPath,
                JsonSerializer.Serialize(payload, PayloadOptions));

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            var prefix = new List<string>();
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var assembly = typeof(UpdateNotifier).Assembly.Location;
                if (string.IsNullOrWhiteSpace(assembly))
                {
                    return;
                }
                prefix.Add("exec");
                prefix.Add(Path.GetFullPath(assembly));
            }

            var arguments = new List<string>(prefix)
            {
                "__update-notify",
                "--payload",
                payloadPath,
            };
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                arguments.Add("--install-dir");
                arguments.Add(installDir);
            }

            var setsid = OperatingSystem.IsWindows() ? null : FindSetsid();
            var startInfo = new ProcessStartInfo
            {
                FileName = setsid ?? processPath,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (setsid is not null)
            {
                startInfo.ArgumentList.Add(processPath);
            }
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.Environment["AMZ_UPDATE_NOTIFY"] = "1";
            using var process = Process.Start(startInfo);
            process?.Dispose();
        }
        catch (Exception ex)
        {
            Log($"Update notification not started: {ex.Message}");
        }
    }

    private static void CleanupOldPayloads(string directory)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-2);
            foreach (var file in Directory.EnumerateFiles(directory, "update-*.json"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static string? FindSetsid()
    {
        foreach (var candidate in new[] { "/usr/bin/setsid", "/bin/setsid" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static (string Title, string Body, MonitorNotificationSeverity Severity)
        Describe(UpdateJournal journal)
    {
        var version = journal.ToVersion ?? "?";
        return journal.Phase switch
        {
            UpdatePhase.Completed => (
                $"AMZ CLI updated to {version}",
                journal.MonitorsRestarted.Count > 0
                    ? "Update completed; monitors restarted."
                    : "Update completed.",
                MonitorNotificationSeverity.Info),
            UpdatePhase.RolledBack => (
                "Update rolled back",
                $"Restored version {journal.FromVersion ?? "previous"}. " +
                "Monitors were restarted with the previous version.",
                MonitorNotificationSeverity.Error),
            UpdatePhase.DeferredForeground => (
                "Update deferred",
                journal.DeferredReason
                    ?? "A foreground monitor is active. Close it to update.",
                MonitorNotificationSeverity.Warning),
            _ => (
                "Update failed",
                journal.Error ?? "Check the journal with amz-cli update --status.",
                MonitorNotificationSeverity.Error),
        };
    }

    private static void Log(string message)
    {
        try
        {
            UpdatePaths.EnsureCreated();
            File.AppendAllText(
                UpdatePaths.LogPath(),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
