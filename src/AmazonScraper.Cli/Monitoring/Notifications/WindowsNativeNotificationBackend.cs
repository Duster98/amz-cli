using System.Text.Json;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

/// <summary>
/// Uses the Windows-only AMZ CLI notification helper. The helper owns the toast
/// identity, so Windows attributes the notification to AMZ CLI rather
/// than PowerShell.
/// </summary>
public sealed class WindowsNativeNotificationBackend : INotificationBackend
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _path;

    private WindowsNativeNotificationBackend(string path)
    {
        _path = path;
    }

    public string Name => "windows/native";

    public static WindowsNativeNotificationBackend? Detect(string? baseDirectory = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        baseDirectory ??= AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "amz-notify.exe"),
            Path.Combine(baseDirectory, "notify", "amz-notify.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new WindowsNativeNotificationBackend(candidate);
            }
        }

        return null;
    }

    public async Task<NotificationSendResult> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(_path);
        startInfo.Environment["AMZ_MONITOR_NOTIFY_TITLE"] = message.Title;
        startInfo.Environment["AMZ_MONITOR_NOTIFY_BODY"] = message.Body;
        startInfo.Environment["AMZ_MONITOR_NOTIFY_SEVERITY"] =
            message.Severity.ToString();
        if (message.Payload is not null
            && TryWritePayload(message.Payload) is { } payloadPath)
        {
            startInfo.Environment["AMZ_MONITOR_NOTIFY_PAYLOAD"] = payloadPath;
        }

        var result = await NotificationProcess.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(15),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Success && string.IsNullOrWhiteSpace(result.Detail)
            ? result with { Detail = "amz-notify" }
            : result;
    }

    private static string? TryWritePayload(MonitorNotificationPayload payload)
    {
        try
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            var directory = Path.Combine(
                root,
                "AmzCLI",
                "notifications");
            Directory.CreateDirectory(directory);
            CleanupOldPayloads(directory);

            var path = Path.Combine(
                directory,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(payload, PayloadJsonOptions));
            return path;
        }
        catch (Exception)
        {
            // The toast can still be shown without the click-through payload.
            return null;
        }
    }

    private static void CleanupOldPayloads(string directory)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
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
}
