using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed class MonitorPidInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("binding_id")]
    public string? BindingId { get; set; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("startup")]
    public string? Startup { get; set; }

    [JsonPropertyName("startup_id")]
    public string? StartupId { get; set; }

    [JsonPropertyName("startup_name")]
    public string? StartupName { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = string.Empty;

    [JsonPropertyName("process_started_at")]
    public DateTimeOffset? ProcessStartedAt { get; set; }

    [JsonPropertyName("log_path")]
    public string LogPath { get; set; } = string.Empty;

    [JsonPropertyName("data_files")]
    public List<string> DataFiles { get; set; } = [];

    [JsonPropertyName("targets")]
    public int Targets { get; set; }

    [JsonPropertyName("timer_seconds")]
    public int TimerSeconds { get; set; }

    [JsonPropertyName("schedule_mode")]
    public string? ScheduleMode { get; set; }

    [JsonPropertyName("runtime_hash")]
    public string? RuntimeHash { get; set; }

    [JsonPropertyName("window_seconds")]
    public int? WindowSeconds { get; set; }

    [JsonPropertyName("max_requests")]
    public int? MaxRequests { get; set; }

    [JsonPropertyName("min_gap_seconds")]
    public int? MinGapSeconds { get; set; }

    [JsonPropertyName("max_gap_seconds")]
    public int? MaxGapSeconds { get; set; }

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = [];

    [JsonPropertyName("save_path")]
    public string? SavePath { get; set; }

    [JsonPropertyName("save_file")]
    public string? SaveFile { get; set; }

    [JsonPropertyName("pid_file")]
    public string? PidFile { get; set; }

    [JsonPropertyName("executable")]
    public string? Executable { get; set; }

    [JsonPropertyName("prefix_arguments")]
    public List<string> PrefixArguments { get; set; } = [];

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = [];

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static MonitorPidInfo? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MonitorPidInfo>(
                File.ReadAllText(path),
                Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new MonitorException($"Invalid pidfile '{path}': {ex.Message}");
        }
    }

    public static void Save(string path, MonitorPidInfo info)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(temporary, JsonSerializer.Serialize(info, Options));
        File.Move(temporary, path, overwrite: true);
    }
}

public static class MonitorBackground
{
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private const int MaxStopWaitSeconds = 30;

    public static async Task<MonitorPidInfo> StartDetachedAsync(
        MonitorPaths paths,
        string logPath,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        string? bindingId = null,
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
                "Could not determine the executable for --detach.");
        var isDotnet = Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var command = executable;
        var commandArguments = new List<string>();
        if (isDotnet)
        {
            var assembly = typeof(MonitorBackground).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assembly))
            {
                throw new MonitorException(
                    "--detach is not available in this mode; use the published binary.");
            }

            commandArguments.Add("exec");
            commandArguments.Add(assembly);
        }

        var childArguments = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(argument => !string.Equals(argument, "--detach", StringComparison.Ordinal))
            .ToList();
        var hasLogPath = childArguments.Any(argument =>
            string.Equals(argument, "--log-path", StringComparison.Ordinal)
            || argument.StartsWith("--log-path=", StringComparison.Ordinal));
        if (!hasLogPath)
        {
            childArguments.Add("--log-path");
            childArguments.Add(logPath);
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
        foreach (var argument in commandArguments.Concat(childArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["AMZ_MONITOR_DETACHED"] = "1";
        if (!string.IsNullOrWhiteSpace(bindingId))
        {
            startInfo.Environment["AMZ_MONITOR_BINDING_ID"] = bindingId;
        }

        using var process = Process.Start(startInfo)
            ?? throw new MonitorException("Failed to start the monitor in the background.");
        await Task.Delay(900).ConfigureAwait(false);
        if (process.HasExited)
        {
            TryDelete(paths.PidPath);
            var tail = ReadLogTail(logPath);
            throw new MonitorException(
                $"The background monitor exited immediately with code " +
                $"{process.ExitCode}.{Environment.NewLine}{tail}");
        }

        DateTimeOffset? processStarted = null;
        try
        {
            processStarted = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception)
        {
            // Not available on every platform.
        }

        var info = new MonitorPidInfo
        {
            BindingId = bindingId,
            Pid = process.Id,
            StartedAt = MonitorObservationFactory.FormatTimestamp(DateTimeOffset.UtcNow),
            ProcessStartedAt = processStarted,
            LogPath = Path.GetFullPath(logPath),
            DataFiles = MonitorOutput.DescribeFiles(
                    paths,
                    format,
                    layout,
                    plan)
                .ToList(),
            Targets = plan.AllTargets.Count(),
            TimerSeconds = timerSeconds,
            RuntimeHash = runtimeHash,
            ScheduleMode = plan.Schedule is null ? null : "adaptive",
            WindowSeconds = plan.Schedule?.WindowSeconds,
            MaxRequests = plan.Schedule?.MaxRequests,
            MinGapSeconds = plan.Schedule?.DefaultMinGapSeconds,
            MaxGapSeconds = plan.Schedule?.DefaultMaxGapSeconds,
            Domains = plan.Domains.ToList(),
            SavePath = paths.SavePath,
            SaveFile = paths.BaseName,
            PidFile = paths.PidPath,
            Mode = "detach",
            Executable = command,
            PrefixArguments = commandArguments,
            Arguments = childArguments,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        MonitorPidInfo.Save(paths.PidPath, info);
        return info;
    }

    /// <summary>
    /// Restarts a detached monitor with the command line captured in its
    /// registry entry. Startup-managed monitors use StartupManager instead.
    /// </summary>
    public static async Task<MonitorPidInfo> RestartDetachedAsync(
        MonitorPidInfo previous)
    {
        if (string.IsNullOrWhiteSpace(previous.Executable)
            || string.IsNullOrWhiteSpace(previous.SavePath)
            || string.IsNullOrWhiteSpace(previous.SaveFile)
            || previous.Arguments.Count == 0)
        {
            throw new MonitorException(
                "The detached monitor does not have the metadata required for automatic " +
                "restart. Restart it manually once.");
        }

        var paths = MonitorPaths.Resolve(previous.SavePath, previous.SaveFile);
        var workingDirectory = string.IsNullOrWhiteSpace(previous.WorkingDirectory)
            ? Environment.CurrentDirectory
            : previous.WorkingDirectory;
        var setsid = OperatingSystem.IsWindows() ? null : FindSetsid();
        var startInfo = new ProcessStartInfo
        {
            FileName = setsid ?? previous.Executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (setsid is not null)
        {
            startInfo.ArgumentList.Add(previous.Executable);
        }
        foreach (var argument in previous.PrefixArguments.Concat(previous.Arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["AMZ_MONITOR_DETACHED"] = "1";
        if (!string.IsNullOrWhiteSpace(previous.BindingId))
        {
            startInfo.Environment["AMZ_MONITOR_BINDING_ID"] = previous.BindingId;
        }

        using var process = Process.Start(startInfo)
            ?? throw new MonitorException("Failed to restart the detached monitor.");
        await Task.Delay(900).ConfigureAwait(false);
        if (process.HasExited)
        {
            var tail = ReadLogTail(previous.LogPath);
            throw new MonitorException(
                $"The restarted monitor exited immediately with code " +
                $"{process.ExitCode}.{Environment.NewLine}{tail}");
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

        var info = new MonitorPidInfo
        {
            BindingId = previous.BindingId,
            Pid = process.Id,
            StartedAt = MonitorObservationFactory.FormatTimestamp(DateTimeOffset.UtcNow),
            ProcessStartedAt = processStarted,
            LogPath = previous.LogPath,
            DataFiles = previous.DataFiles,
            Targets = previous.Targets,
            TimerSeconds = previous.TimerSeconds,
            ScheduleMode = previous.ScheduleMode,
            RuntimeHash = previous.RuntimeHash,
            WindowSeconds = previous.WindowSeconds,
            MaxRequests = previous.MaxRequests,
            MinGapSeconds = previous.MinGapSeconds,
            MaxGapSeconds = previous.MaxGapSeconds,
            Domains = previous.Domains,
            SavePath = previous.SavePath,
            SaveFile = previous.SaveFile,
            PidFile = paths.PidPath,
            Mode = "detach",
            Executable = previous.Executable,
            PrefixArguments = previous.PrefixArguments,
            Arguments = previous.Arguments,
            WorkingDirectory = workingDirectory,
        };
        MonitorPidInfo.Save(paths.PidPath, info);
        return info;
    }

    public static int Status(MonitorPaths paths)
    {
        var info = MonitorPidInfo.TryLoad(paths.PidPath);
        if (info is null)
        {
            Console.Out.WriteLine("Monitor not running.");
            return 1;
        }

        if (!MonitorProcessInspector.IsRunning(info))
        {
            Console.Out.WriteLine(
                $"Monitor not running (stale pidfile, PID {info.Pid}).");
            return 1;
        }

        Console.Out.WriteLine("Monitor running.");
        Console.Out.WriteLine($"PID: {info.Pid}");
        Console.Out.WriteLine($"Started: {info.StartedAt}");
        Console.Out.WriteLine($"Target: {info.Targets}");
        if (string.Equals(info.ScheduleMode, "adaptive", StringComparison.Ordinal))
        {
            Console.Out.WriteLine(
                $"Schedule: adaptive {info.MaxRequests}/{info.WindowSeconds}s " +
                $"gap {info.MinGapSeconds}-{info.MaxGapSeconds}s");
            var state = MonitorStateStore.TryRead(
                Path.Combine(
                    info.SavePath ?? string.Empty,
                    $"{info.SaveFile ?? "amz-monitor"}.state.json"));
            if (state?.Schedule is { } schedule)
            {
                Console.Out.WriteLine(
                    $"Current budget: {schedule.SpentRequests}/" +
                    $"{info.MaxRequests ?? 0} requests");
                Console.Out.WriteLine(
                    $"Window: {schedule.WindowStart:yyyy-MM-ddTHH:mm:ssZ} → " +
                    $"{schedule.WindowEnd:yyyy-MM-ddTHH:mm:ssZ}");
            }
        }
        else
        {
            Console.Out.WriteLine($"Timer: {info.TimerSeconds}s");
        }
        Console.Out.WriteLine($"Log: {info.LogPath}");
        foreach (var file in info.DataFiles.Where(File.Exists))
        {
            Console.Out.WriteLine($"Data: {file}");
        }
        return 0;
    }

    public static async Task<int> StopAsync(
        MonitorPaths paths,
        bool quiet = false)
    {
        var info = MonitorPidInfo.TryLoad(paths.PidPath);
        if (info is null)
        {
            if (!quiet)
            {
                Console.Out.WriteLine("Monitor not running.");
            }
            return 0;
        }

        if (!MonitorProcessInspector.IsRunning(info))
        {
            TryDelete(paths.PidPath);
            if (!quiet)
            {
                Console.Out.WriteLine("Monitor not running (stale pidfile removed).");
            }
            return 0;
        }

        RequestStop(info.Pid);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(MaxStopWaitSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!MonitorProcessInspector.IsRunning(info))
            {
                TryDelete(paths.PidPath);
                if (!quiet)
                {
                    Console.Out.WriteLine($"Monitor stopped (PID {info.Pid}).");
                }
                return 0;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        ForceStop(info.Pid);
        await Task.Delay(1000).ConfigureAwait(false);
        TryDelete(paths.PidPath);
        if (!quiet)
        {
            Console.Out.WriteLine($"Monitor forcibly terminated (PID {info.Pid}).");
        }
        return MonitorProcessInspector.IsRunning(info) ? 1 : 0;
    }

    private static void RequestStop(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // The process may have exited already.
            }
            return;
        }

        _ = kill(pid, SigTerm);
    }

    private static void ForceStop(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception)
            {
                // Already exited.
            }
            return;
        }

        _ = kill(pid, SigKill);
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

    private static string ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "No log available.";
            }

            var lines = File.ReadLines(path).TakeLast(20);
            return string.Join(Environment.NewLine, lines);
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

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
