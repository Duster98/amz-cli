using System.Text;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

public sealed class WindowsNotificationBackend : INotificationBackend
{
    private const string ToastScript = """
        $ErrorActionPreference = 'Stop'
        $title = $env:AMZ_MONITOR_NOTIFY_TITLE
        $body = $env:AMZ_MONITOR_NOTIFY_BODY

        try {
            if (Get-Module -ListAvailable -Name BurntToast) {
                Import-Module BurntToast -ErrorAction Stop
                New-BurntToastNotification -Text $title, $body | Out-Null
                Write-Output 'burnttoast'
                exit 0
            }
        } catch {
            # Fall through to the WinRT toast.
        }

        try {
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
            $texts = $template.GetElementsByTagName('text')
            $texts.Item(0).AppendChild($template.CreateTextNode($title)) | Out-Null
            $texts.Item(1).AppendChild($template.CreateTextNode($body)) | Out-Null
            $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
            $aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
            $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid)
            $notifier.Show($toast)
            Write-Output 'winrt'
            exit 0
        } catch {
            try {
                $message = $title + "`n" + $body
                & msg.exe $env:USERNAME /TIME:10 $message 2>$null
                if ($LASTEXITCODE -eq 0) {
                    Write-Output 'msg'
                    exit 0
                }
            } catch {
                # No fallback available.
            }

            Write-Error $_.Exception.Message
            exit 1
        }
        """;

    private readonly string _path;

    private WindowsNotificationBackend(string path)
    {
        _path = path;
    }

    public string Name => "windows/powershell";

    public static WindowsNotificationBackend? Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var powershell = NotificationExecutable.Find("powershell.exe");
        if (powershell is null)
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                var candidate = Path.Combine(
                    systemRoot,
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe");
                if (File.Exists(candidate))
                {
                    powershell = candidate;
                }
            }
        }

        powershell ??= NotificationExecutable.Find("pwsh.exe");
        return powershell is null
            ? null
            : new WindowsNotificationBackend(powershell);
    }

    public async Task<NotificationSendResult> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(ToastScript));
        var startInfo = new System.Diagnostics.ProcessStartInfo(_path);
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Sta");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);
        startInfo.Environment["AMZ_MONITOR_NOTIFY_TITLE"] = message.Title;
        startInfo.Environment["AMZ_MONITOR_NOTIFY_BODY"] = message.Body;

        var result = await NotificationProcess.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(15),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Success && string.IsNullOrWhiteSpace(result.Detail)
            ? result with { Detail = "powershell" }
            : result;
    }
}
