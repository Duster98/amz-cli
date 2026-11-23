using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed record MonitorRestartItem(
    MonitorPidInfo Info,
    StartupDefinition? Startup,
    bool CanRestart,
    string? BlockReason);

public sealed record MonitorRestartInspection(
    IReadOnlyList<MonitorRestartItem> Items,
    bool HasForeground,
    bool HasUnrestartable,
    string? BlockReason);

public sealed class MonitorRestartResult
{
    public List<string> Stopped { get; } = [];

    public List<string> Restarted { get; } = [];

    public List<string> Skipped { get; } = [];

    public List<string> Errors { get; } = [];
}

/// <summary>
/// Stops and restarts only the monitors that belong to the current
/// installation. A foreground monitor is never stopped automatically because
/// it cannot be resumed in its original terminal.
/// </summary>
public static class MonitorRestartCoordinator
{
    public static MonitorRestartInspection Inspect(
        string installDir,
        MonitorRegistry? registry = null,
        StartupRegistry? startupRegistry = null,
        string? savePath = null,
        string? saveFile = null)
    {
        var items = new List<MonitorRestartItem>();
        var seenPids = new HashSet<int>();
        startupRegistry ??= new StartupRegistry();
        registry ??= new MonitorRegistry();

        foreach (var info in registry.ReadAll())
        {
            if (!MonitorProcessInspector.IsRunning(info) || !seenPids.Add(info.Pid))
            {
                continue;
            }

            if ((savePath is not null || saveFile is not null)
                && (!StartupIdentity.PathsEqual(info.SavePath, savePath)
                    || !StartupIdentity.FilesEqual(info.SaveFile, saveFile)))
            {
                continue;
            }

            var startup = info.StartupId is null
                ? null
                : startupRegistry.FindById(info.StartupId);
            if (!BelongsToInstallation(info, startup, installDir))
            {
                continue;
            }

            var mode = info.Mode ?? "foreground";
            var canRestart = mode switch
            {
                "startup" => startup is not null
                    && !string.IsNullOrWhiteSpace(startup.Executable),
                "detach" => !string.IsNullOrWhiteSpace(info.Executable)
                    && info.Arguments.Count > 0
                    && !string.IsNullOrWhiteSpace(info.SavePath)
                    && !string.IsNullOrWhiteSpace(info.SaveFile),
                _ => false,
            };
            string? itemBlockReason = null;
            if (mode == "detach" && !canRestart)
            {
                itemBlockReason =
                    "detached monitor without restart metadata: " +
                    "restart it manually once to enable auto-update";
            }
            else if (mode == "startup" && !canRestart)
            {
                itemBlockReason =
                    "startup monitor without a valid startup definition";
            }

            items.Add(new MonitorRestartItem(info, startup, canRestart, itemBlockReason));
        }

        var foreground = items.Any(item =>
            string.Equals(item.Info.Mode, "foreground", StringComparison.Ordinal));
        var unrestartable = items.Any(item => !item.CanRestart
            && !string.Equals(item.Info.Mode, "foreground", StringComparison.Ordinal));
        var blockReason = foreground
            ? "a foreground monitor is active: close it to allow auto-update"
            : unrestartable
                ? "one or more monitors lack the metadata required for restart"
                : null;

        return new MonitorRestartInspection(
            items,
            foreground,
            unrestartable,
            blockReason);
    }

    public static async Task<MonitorRestartResult> StopAllAsync(
        MonitorRestartInspection inspection,
        CancellationToken cancellationToken = default)
    {
        var result = new MonitorRestartResult();
        var startupManager = TryCreateStartupManager();

        foreach (var item in inspection.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (item.Startup is not null && startupManager is not null)
                {
                    startupManager.Stop(item.Startup);
                }

                var paths = ResolvePaths(item.Info);
                await MonitorBackground.StopAsync(paths).ConfigureAwait(false);
                result.Stopped.Add(item.Info.Id);
                new MonitorRegistry().Unregister(item.Info.Id);
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"{item.Info.Id} (PID {item.Info.Pid}): {ex.Message}");
            }
        }

        return result;
    }

    public static async Task<MonitorRestartResult> RestartAsync(
        MonitorRestartInspection inspection,
        IReadOnlyCollection<string> stoppedIds,
        CancellationToken cancellationToken = default)
    {
        var result = new MonitorRestartResult();
        var stopped = stoppedIds.ToHashSet(StringComparer.Ordinal);
        var startupManager = TryCreateStartupManager();

        foreach (var item in inspection.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stopped.Contains(item.Info.Id))
            {
                continue;
            }

            try
            {
                var mode = item.Info.Mode ?? "foreground";
                if (!item.CanRestart
                    || mode is not ("startup" or "detach"))
                {
                    result.Skipped.Add(
                        $"{item.Info.Id} (PID {item.Info.Pid}) was stopped " +
                        "and must be restarted manually.");
                    continue;
                }

                if (mode == "startup")
                {
                    if (item.Startup is null || startupManager is null)
                    {
                        throw new MonitorException(
                            "Startup definition unavailable.");
                    }

                    startupManager.Start(item.Startup, restart: true);
                    await WaitForRunningAsync(
                            item.Startup.SavePath,
                            item.Startup.SaveFile,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (mode == "detach")
                {
                    await MonitorBackground
                        .RestartDetachedAsync(item.Info)
                        .ConfigureAwait(false);
                }
                result.Restarted.Add(item.Info.Id);
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"{item.Info.Id} (PID {item.Info.Pid}): {ex.Message}");
            }
        }

        return result;
    }

    private static StartupManager? TryCreateStartupManager()
    {
        try
        {
            return StartupManager.Create();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MonitorPaths ResolvePaths(MonitorPidInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.SavePath)
            || string.IsNullOrWhiteSpace(info.SaveFile))
        {
            throw new MonitorException(
                "The monitor does not expose save-path/save-file for stopping.");
        }

        return MonitorPaths.Resolve(info.SavePath, info.SaveFile);
    }

    private static async Task WaitForRunningAsync(
        string savePath,
        string saveFile,
        CancellationToken cancellationToken)
    {
        var paths = MonitorPaths.Resolve(savePath, saveFile);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = MonitorPidInfo.TryLoad(paths.PidPath);
            if (info is not null && MonitorProcessInspector.IsRunning(info))
            {
                return;
            }
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new MonitorException(
            "The monitor did not come back up within 15 seconds.");
    }

    private static bool BelongsToInstallation(
        MonitorPidInfo info,
        StartupDefinition? startup,
        string installDir)
    {
        var executable = info.Executable;
        if (string.IsNullOrWhiteSpace(executable)
            && startup is not null)
        {
            executable = startup.Executable;
        }
        if (string.IsNullOrWhiteSpace(executable))
        {
            // Old detached/foreground entries have no executable. Treat them
            // as current only when they are not restartable; the inspection
            // will block the update instead of touching another installation.
            return true;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(installDir)),
                comparison);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
