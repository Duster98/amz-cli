using System.Diagnostics;

namespace AmazonScraper.Cli.Monitoring;

public static class MonitorProcessInspector
{
    public static bool IsRunning(MonitorPidInfo info) =>
        IsRunningCore(info.Pid, info.ProcessStartedAt, info.Executable);

    public static bool IsRunning(int pid, DateTimeOffset? processStartedAt) =>
        IsRunningCore(pid, processStartedAt, executable: null);

    private static bool IsRunningCore(
        int pid,
        DateTimeOffset? processStartedAt,
        string? executable)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            var startTimeChecked = false;
            if (processStartedAt is { } expected)
            {
                try
                {
                    var actual = new DateTimeOffset(
                        process.StartTime.ToUniversalTime(),
                        TimeSpan.Zero);
                    startTimeChecked = true;
                    if (Math.Abs((actual - expected).TotalSeconds) > 5)
                    {
                        return false;
                    }
                }
                catch (Exception)
                {
                    // StartTime is not available for every process.
                }
            }

            // Old entries may not contain process_started_at. In that case a
            // recycled PID could look alive; compare the executable path when
            // we have one. Access can be denied for another user's process.
            if (!startTimeChecked && !string.IsNullOrWhiteSpace(executable))
            {
                try
                {
                    var actualPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(actualPath))
                    {
                        var comparison = OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal;
                        if (!string.Equals(
                                Path.GetFullPath(actualPath),
                                Path.GetFullPath(executable),
                                comparison))
                        {
                            return false;
                        }
                    }
                }
                catch (Exception)
                {
                    // Fall back to the PID check.
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
