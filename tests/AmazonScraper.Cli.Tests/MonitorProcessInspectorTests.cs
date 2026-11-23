using System.Diagnostics;
using AmazonScraper.Cli.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorProcessInspectorTests
{
    [Fact]
    public void A_reused_pid_with_a_different_start_time_is_not_running()
    {
        var info = new MonitorPidInfo
        {
            Pid = Environment.ProcessId,
            ProcessStartedAt = new DateTimeOffset(
                2000,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero),
        };

        Assert.False(MonitorProcessInspector.IsRunning(info));
    }

    [Fact]
    public void A_reused_pid_with_a_different_executable_is_not_running()
    {
        string? currentPath;
        try
        {
            using var process = Process.GetCurrentProcess();
            currentPath = process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        var info = new MonitorPidInfo
        {
            Pid = Environment.ProcessId,
            ProcessStartedAt = null,
            Executable = currentPath + ".missing",
        };

        Assert.False(MonitorProcessInspector.IsRunning(info));
    }
}
