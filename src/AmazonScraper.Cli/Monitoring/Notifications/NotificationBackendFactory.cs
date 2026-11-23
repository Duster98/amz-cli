namespace AmazonScraper.Cli.Monitoring.Notifications;

public static class NotificationBackendFactory
{
    public static INotificationBackend? Create()
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxNotificationBackend.Detect();
        }
        if (OperatingSystem.IsWindows())
        {
            var native = WindowsNativeNotificationBackend.Detect();
            var powershell = WindowsNotificationBackend.Detect();
            if (native is not null && powershell is not null)
            {
                return new WindowsCompositeNotificationBackend(native, powershell);
            }

            return native is not null ? native : powershell;
        }

        return null;
    }

    public static string GetInstallHint()
    {
        if (OperatingSystem.IsLinux())
        {
            return "Install libnotify-bin (notify-send) or zenity/kdialog.";
        }
        if (OperatingSystem.IsWindows())
        {
            return "Place amz-notify.exe next to amz-cli.exe " +
                   "or install BurntToast: Install-Module BurntToast -Scope CurrentUser";
        }

        return "Notification backend unavailable on this system.";
    }
}

internal static class NotificationExecutable
{
    public static string? Find(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
