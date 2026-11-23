using System.Diagnostics;
using System.Text;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Monitoring.Notifications;

public sealed class LinuxNotificationBackend : INotificationBackend
{
    private readonly string _tool;
    private readonly string _path;

    private LinuxNotificationBackend(string tool, string path)
    {
        _tool = tool;
        _path = path;
    }

    public string Name => $"linux/{_tool}";

    public static LinuxNotificationBackend? Detect()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var notifySend = NotificationExecutable.Find("notify-send");
        if (notifySend is not null)
        {
            return new LinuxNotificationBackend("notify-send", notifySend);
        }

        var zenity = NotificationExecutable.Find("zenity");
        if (zenity is not null)
        {
            return new LinuxNotificationBackend("zenity", zenity);
        }

        var kdialog = NotificationExecutable.Find("kdialog");
        return kdialog is null
            ? null
            : new LinuxNotificationBackend("kdialog", kdialog);
    }

    public Task<NotificationSendResult> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_path);
        startInfo.ArgumentList.Add("--app-name=AMZ CLI");
        switch (_tool)
        {
            case "notify-send":
                startInfo.ArgumentList.Add(
                    $"--urgency={Urgency(message.Severity)}");
                if (IsUpdate(message.Payload))
                {
                    startInfo.ArgumentList.Add("--expire-time=15000");
                    startInfo.ArgumentList.Add("--action=default=Details");
                }
                else
                {
                    startInfo.ArgumentList.Add("--expire-time=10000");
                }
                startInfo.ArgumentList.Add(
                    $"--icon={Icon(message.Severity)}");
                startInfo.ArgumentList.Add(message.Title);
                startInfo.ArgumentList.Add(
                    IsUpdate(message.Payload)
                        ? message.Body + "\nClick for details."
                        : message.Body);
                break;
            case "zenity":
                startInfo.ArgumentList.Clear();
                startInfo.ArgumentList.Add("--notification");
                startInfo.ArgumentList.Add("--text");
                startInfo.ArgumentList.Add($"{message.Title}\n{message.Body}");
                break;
            case "kdialog":
                startInfo.ArgumentList.Clear();
                startInfo.ArgumentList.Add("--title");
                startInfo.ArgumentList.Add("AMZ CLI Monitor");
                startInfo.ArgumentList.Add("--passivepopup");
                startInfo.ArgumentList.Add($"{message.Title}\n{message.Body}");
                startInfo.ArgumentList.Add("10");
                break;
            default:
                return Task.FromResult(new NotificationSendResult(
                    false,
                    Error: $"unsupported Linux backend: {_tool}"));
        }

        return SendCoreAsync(startInfo, message.Payload, cancellationToken);
    }

    private async Task<NotificationSendResult> SendCoreAsync(
        ProcessStartInfo startInfo,
        MonitorNotificationPayload? payload,
        CancellationToken cancellationToken)
    {
        var result = await NotificationProcess.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(18),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Success
            && IsUpdateAction(result.Detail))
        {
            // The detail dialog is intentionally detached from the updater.
            _ = ShowUpdateDetails(payload);
        }

        return result.Success && string.IsNullOrWhiteSpace(result.Detail)
            ? result with { Detail = _tool }
            : result;
    }

    private static bool IsUpdate(MonitorNotificationPayload? payload) =>
        string.Equals(payload?.Kind, "update", StringComparison.Ordinal);

    private static bool IsUpdateAction(string? detail)
    {
        var value = detail?.Trim().ToLowerInvariant();
        return value is "default" or "0";
    }

    private static bool ShowUpdateDetails(MonitorNotificationPayload? payload)
    {
        try
        {
            var root = UpdatePaths.UpdatesDirectory();
            Directory.CreateDirectory(root);
            var path = Path.Combine(
                root,
                "notification-details.txt");
            var builder = new StringBuilder();
            builder.AppendLine("AMZ CLI Monitor");
            builder.AppendLine();
            if (payload is null)
            {
                builder.AppendLine("Details unavailable.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(payload.Title))
                {
                    builder.AppendLine(payload.Title);
                }
                if (!string.IsNullOrWhiteSpace(payload.Body))
                {
                    builder.AppendLine(payload.Body);
                }
                if (!string.IsNullOrWhiteSpace(payload.Notes))
                {
                    builder.AppendLine();
                    builder.AppendLine("Release notes:");
                    builder.AppendLine(payload.Notes);
                }
                builder.AppendLine();
                if (!string.IsNullOrWhiteSpace(payload.FromVersion)
                    || !string.IsNullOrWhiteSpace(payload.ToVersion))
                {
                    builder.AppendLine(
                        $"Version: {payload.FromVersion ?? "?"} -> " +
                        $"{payload.ToVersion ?? "?"}");
                }
                if (!string.IsNullOrWhiteSpace(payload.UpdateStatus))
                {
                    builder.AppendLine($"Result: {payload.UpdateStatus}");
                }
                if (payload.MonitorsRestarted is { } restarted)
                {
                    builder.AppendLine(
                        $"Monitors: {(restarted ? "restarted" : "not restarted")}");
                }
                if (!string.IsNullOrWhiteSpace(payload.BackupPath))
                {
                    builder.AppendLine($"Backup: {payload.BackupPath}");
                }
                if (!string.IsNullOrWhiteSpace(payload.LogPath))
                {
                    builder.AppendLine($"Log: {payload.LogPath}");
                }
                if (!string.IsNullOrWhiteSpace(payload.Error))
                {
                    builder.AppendLine($"Error: {payload.Error}");
                }
            }
            builder.AppendLine();
            builder.AppendLine("For full status: amz-cli update --status");
            File.WriteAllText(
                path,
                builder.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var zenity = NotificationExecutable.Find("zenity");
            if (zenity is not null)
            {
                var info = new ProcessStartInfo(zenity)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                info.ArgumentList.Add("--text-info");
                info.ArgumentList.Add("--title=AMZ CLI Monitor");
                info.ArgumentList.Add("--filename");
                info.ArgumentList.Add(path);
                info.ArgumentList.Add("--width=760");
                info.ArgumentList.Add("--height=420");
                Process.Start(info)?.Dispose();
                return true;
            }

            var kdialog = NotificationExecutable.Find("kdialog");
            if (kdialog is not null)
            {
                var info = new ProcessStartInfo(kdialog)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                info.ArgumentList.Add("--title");
                info.ArgumentList.Add("AMZ CLI Monitor");
                info.ArgumentList.Add("--textbox");
                info.ArgumentList.Add(path);
                info.ArgumentList.Add("760");
                info.ArgumentList.Add("420");
                Process.Start(info)?.Dispose();
                return true;
            }
        }
        catch (Exception)
        {
            // The notification itself is still useful.
        }

        return false;
    }

    private static string Urgency(MonitorNotificationSeverity severity) =>
        severity == MonitorNotificationSeverity.Error ? "critical" : "normal";

    private static string Icon(MonitorNotificationSeverity severity) =>
        severity switch
        {
            MonitorNotificationSeverity.Error => "dialog-error",
            MonitorNotificationSeverity.Warning => "dialog-warning",
            _ => "dialog-information",
        };
}
