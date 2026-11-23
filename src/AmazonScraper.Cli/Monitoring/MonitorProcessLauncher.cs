using System.Diagnostics;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Starts a library monitor as a detached process using an explicit command
/// line. Unlike the legacy --detach path, this does not copy the arguments of
/// the current process, so it can be used safely from the Web host.
/// </summary>
public static class MonitorProcessLauncher
{
    public static async Task<MonitorPidInfo> StartDetachedAsync(
        string bindingId,
        MonitorPaths paths,
        string logPath,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        int requestDelaySeconds,
        int jitterSeconds,
        bool notify,
        IReadOnlyList<string> notifyOn,
        int? notifyCooldownSeconds,
        bool noAutoUpdate,
        string? runtimeHash = null)
    {
        Directory.CreateDirectory(paths.SavePath);
        var logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        MonitorPidInfo? existing;
        try
        {
            existing = MonitorPidInfo.TryLoad(paths.PidPath);
        }
        catch (MonitorException)
        {
            TryDelete(paths.PidPath);
            existing = null;
        }
        if (existing is not null && MonitorProcessInspector.IsRunning(existing))
        {
            throw new MonitorException(
                $"Monitor already running (PID {existing.Pid}). Use --status or --stop.");
        }
        if (existing is not null)
        {
            TryDelete(paths.PidPath);
        }

        var executable = Environment.ProcessPath
            ?? throw new MonitorException(
                "Could not determine the executable for the monitor.");
        var isDotnet = Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var command = executable;
        var prefixArguments = new List<string>();
        if (isDotnet)
        {
            var assembly = typeof(MonitorProcessLauncher).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assembly))
            {
                throw new MonitorException(
                    "Detached monitors are not available in this mode; use the published binary.");
            }
            prefixArguments.Add("exec");
            prefixArguments.Add(assembly);
        }

        var arguments = new List<string>
        {
            "monitor",
            "--binding", bindingId,
            "--timer", timerSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--request-delay", requestDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--jitter", Math.Max(0, jitterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--save-path", paths.SavePath,
            "--save-file", paths.BaseName,
            "--save-format", format == MonitorFormat.Csv ? "csv" : "json",
            "--layout", LayoutName(layout),
            "--log-path", Path.GetFullPath(logPath),
        };
        if (notify)
        {
            arguments.Add("--notify");
            if (notifyOn.Count > 0)
            {
                arguments.Add("--notify-on");
                arguments.Add(string.Join(',', notifyOn));
            }
            if (notifyCooldownSeconds is { } cooldown)
            {
                arguments.Add("--notify-cooldown");
                arguments.Add(cooldown.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        if (noAutoUpdate)
        {
            arguments.Add("--no-auto-update");
        }

        var setsid = OperatingSystem.IsWindows() ? null : FindSetsid();
        var startInfo = new ProcessStartInfo
        {
            FileName = setsid ?? command,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (setsid is not null)
        {
            startInfo.ArgumentList.Add(command);
        }
        foreach (var argument in prefixArguments.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["AMZ_MONITOR_DETACHED"] = "1";
        startInfo.Environment["AMZ_MONITOR_BINDING_ID"] = bindingId;

        using var process = Process.Start(startInfo)
            ?? throw new MonitorException("Failed to start the monitor in the background.");
        await Task.Delay(900).ConfigureAwait(false);
        if (process.HasExited)
        {
            TryDelete(paths.PidPath);
            throw new MonitorException(
                $"The background monitor exited immediately with code {process.ExitCode}." +
                Environment.NewLine + ReadLogTail(logPath));
        }

        DateTimeOffset? processStarted = null;
        try
        {
            processStarted = new DateTimeOffset(
                process.StartTime.ToUniversalTime(),
                TimeSpan.Zero);
        }
        catch (Exception)
        {
            // Not available on every platform.
        }

        var resolvedLayout = MonitorOutput.ResolveLayout(layout, format, plan);
        var info = new MonitorPidInfo
        {
            BindingId = bindingId,
            Pid = process.Id,
            StartedAt = MonitorObservationFactory.FormatTimestamp(DateTimeOffset.UtcNow),
            ProcessStartedAt = processStarted,
            LogPath = Path.GetFullPath(logPath),
            DataFiles = MonitorOutput.DescribeFiles(paths, format, layout, plan).ToList(),
            Targets = plan.AllTargets.Count(),
            TimerSeconds = timerSeconds,
            RuntimeHash = runtimeHash ?? MonitorRuntimeSignature.Compute(
                timerSeconds,
                requestDelaySeconds,
                jitterSeconds,
                format,
                resolvedLayout,
                notify,
                notifyOn,
                notifyCooldownSeconds,
                noAutoUpdate,
                paths.SavePath,
                paths.BaseName),
            Domains = plan.Domains.ToList(),
            SavePath = paths.SavePath,
            SaveFile = paths.BaseName,
            PidFile = paths.PidPath,
            Mode = "detach",
            Executable = command,
            PrefixArguments = prefixArguments,
            Arguments = arguments,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        MonitorPidInfo.Save(paths.PidPath, info);
        return info;
    }

    private static string LayoutName(MonitorLayout layout) =>
        layout switch
        {
            MonitorLayout.PerGroup => "per-group",
            MonitorLayout.Combined => "combined",
            MonitorLayout.Single => "single",
            _ => "auto",
        };

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

    private static string ReadLogTail(string path)
    {
        try
        {
            return File.Exists(path)
                ? string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(20))
                : "No log available.";
        }
        catch (Exception)
        {
            return "Log unreadable.";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
