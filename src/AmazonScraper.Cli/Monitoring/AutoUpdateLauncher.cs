using System.Diagnostics;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Starts the detached auto-update coordinator. It never blocks the command
/// that triggered it; the coordinator writes its result to the update journal.
/// </summary>
public static class AutoUpdateLauncher
{
    private static readonly HashSet<string> EnabledCommands =
        new(StringComparer.Ordinal)
        {
            "search",
            "details",
            "deals",
        };

    public static bool TryScheduleStartup(IReadOnlyList<string> args)
    {
        if (!TryGetCommand(args, out var command)
            || !EnabledCommands.Contains(command))
        {
            return false;
        }

        return TrySchedule(
            trigger: "startup",
            parentProcessId: Environment.ProcessId,
            quiet: true);
    }

    public static bool TryScheduleMonitor()
    {
        return TrySchedule(
            trigger: "monitor",
            parentProcessId: null,
            quiet: true);
    }

    private static bool TrySchedule(
        string trigger,
        int? parentProcessId,
        bool quiet)
    {
        if (Environment.GetEnvironmentVariable("AMZ_UPDATE_AUTO") == "1"
            || Environment.GetEnvironmentVariable("AMZ_NO_AUTO_UPDATE") == "1")
        {
            return false;
        }

        var config = AutoUpdateConfig.Load();
        if (!config.Enabled || !AutoUpdateState.IsDue(config))
        {
            return false;
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            var prefix = new List<string>();
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var assembly = typeof(AutoUpdateLauncher).Assembly.Location;
                if (string.IsNullOrWhiteSpace(assembly))
                {
                    return false;
                }
                prefix.Add("exec");
                prefix.Add(Path.GetFullPath(assembly));
            }

            var arguments = new List<string>(prefix)
            {
                "__update-auto",
                "--trigger",
                trigger,
                "--channel",
                config.Channel,
            };
            if (parentProcessId is { } pid)
            {
                arguments.Add("--parent-pid");
                arguments.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (quiet)
            {
                arguments.Add("--quiet");
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
            startInfo.Environment["AMZ_UPDATE_AUTO"] = "1";

            using var process = Process.Start(startInfo);
            process?.Dispose();
            return process is not null;
        }
        catch (Exception ex)
        {
            TryLog($"Auto-update not started: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetCommand(
        IReadOnlyList<string> args,
        out string command)
    {
        foreach (var argument in args)
        {
            if (argument.StartsWith("-", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            command = argument;
            return true;
        }

        command = string.Empty;
        return false;
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

    private static void TryLog(string message)
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
