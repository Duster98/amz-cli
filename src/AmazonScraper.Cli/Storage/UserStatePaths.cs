namespace AmazonScraper.Cli.Storage;

/// <summary>
/// Per-user directories shared by the monitor registry, startup registry and
/// systemd user units. Keep path selection in one place so Linux and Windows
/// follow the same conventions everywhere.
/// </summary>
public static class UserStatePaths
{
    public static string Root()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(local)
                ? Path.Combine(Path.GetTempPath(), "AmzCLI")
                : Path.Combine(local, "AmzCLI");
        }

        if (OperatingSystem.IsLinux())
        {
            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrWhiteSpace(stateHome))
            {
                return Path.Combine(stateHome, "amz-cli");
            }

            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, ".local", "state", "amz-cli");
            }
        }

        return Path.Combine(Path.GetTempPath(), "amz-cli");
    }

    public static string MonitorDirectory() =>
        Path.Combine(Root(), "monitors");

    public static string LibraryPath() =>
        Path.Combine(Root(), "library.json");

    public static string LibraryLockPath() =>
        $"{LibraryPath()}.lock";

    public static string MonitorBindingsPath() =>
        Path.Combine(Root(), "monitor-bindings.json");

    public static string HttpSettingsPath() =>
        Path.Combine(Root(), "http-settings.json");

    public static string MonitorDataRoot() =>
        Path.Combine(Root(), "monitor-data");

    public static string MonitorDataPath(string bindingId)
    {
        var safe = new string((bindingId ?? string.Empty)
            .Select(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'
                ? character
                : '_')
            .ToArray());
        if (safe.Length == 0)
        {
            safe = "monitor";
        }
        return Path.Combine(MonitorDataRoot(), safe);
    }

    public static string HistoryPath() =>
        Path.Combine(Root(), "history.json");

    public static string StartupDirectory() =>
        Path.Combine(Root(), "startups");

    public static string? SystemdUserDirectory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome))
        {
            return Path.Combine(configHome, "systemd", "user");
        }

        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? null
            : Path.Combine(home, ".config", "systemd", "user");
    }
}
