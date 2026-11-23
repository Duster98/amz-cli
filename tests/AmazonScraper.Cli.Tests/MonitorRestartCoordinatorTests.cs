using System.Diagnostics;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorRestartCoordinatorTests
{
    [Fact]
    public void Detached_monitor_with_args_is_restartable()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var info = MonitorInfo("detach", Path.Combine(install, "amz-cli"), temp.Root);
        info.Arguments = ["monitor", "--save-path", temp.Root];
        registry.Register(info);
        Assert.True(MonitorProcessInspector.IsRunning(info));
        Assert.Single(registry.ReadAll());

        var inspection = MonitorRestartCoordinator.Inspect(install, registry, startups);

        var item = Assert.Single(inspection.Items);
        Assert.True(item.CanRestart);
        Assert.False(inspection.HasForeground);
        Assert.False(inspection.HasUnrestartable);
    }

    [Fact]
    public void Detached_monitor_without_args_blocks_auto_update()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var info = MonitorInfo("detach", Path.Combine(install, "amz-cli"), temp.Root);
        info.Arguments.Clear();
        registry.Register(info);

        var inspection = MonitorRestartCoordinator.Inspect(install, registry, startups);

        Assert.True(inspection.HasUnrestartable);
        Assert.False(Assert.Single(inspection.Items).CanRestart);
        Assert.False(string.IsNullOrWhiteSpace(inspection.BlockReason));
    }

    [Fact]
    public void Foreground_monitor_blocks_auto_update()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var info = MonitorInfo("foreground", Path.Combine(install, "amz-cli"), temp.Root);
        registry.Register(info);

        var inspection = MonitorRestartCoordinator.Inspect(install, registry, startups);

        Assert.True(inspection.HasForeground);
        Assert.False(Assert.Single(inspection.Items).CanRestart);
    }

    [Fact]
    public void Inspect_can_filter_by_save_path_and_file()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var running = MonitorInfo("detach", Path.Combine(install, "amz-cli"), temp.Root);
        registry.Register(running);

        var other = MonitorInfo(
            "detach",
            Path.Combine(install, "amz-cli"),
            Path.Combine(temp.Root, "other"));
        other.Pid = int.MaxValue;
        registry.Register(other);

        var filtered = MonitorRestartCoordinator.Inspect(
            install,
            registry,
            startups,
            savePath: other.SavePath,
            saveFile: other.SaveFile);

        Assert.Empty(filtered.Items);

        var matching = MonitorRestartCoordinator.Inspect(
            install,
            registry,
            startups,
            savePath: running.SavePath,
            saveFile: running.SaveFile);

        Assert.Single(matching.Items);
    }

    [Fact]
    public void Monitor_from_another_installation_is_ignored()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var info = MonitorInfo(
            "detach",
            Path.Combine(temp.Root, "other", "amz-cli"),
            temp.Root);
        info.Arguments = ["monitor"];
        registry.Register(info);

        var inspection = MonitorRestartCoordinator.Inspect(install, registry, startups);

        Assert.Empty(inspection.Items);
    }

    [Fact]
    public void Startup_monitor_is_restartable_from_registry()
    {
        using var temp = new TempDirectory();
        var install = InstallDir();
        var registry = new MonitorRegistry(Path.Combine(temp.Root, "monitors"));
        var startups = new StartupRegistry(Path.Combine(temp.Root, "startups"));
        var definition = new StartupDefinition
        {
            Id = "startup123",
            Name = "amz-monitor-startup123.service",
            Kind = "systemd-user",
            Executable = Path.Combine(install, "amz-cli"),
            WorkingDirectory = temp.Root,
            SavePath = temp.Root,
            SaveFile = "monitor",
            Arguments = ["monitor", "--startup-run", "startup123"],
        };
        startups.Save(definition);
        var info = MonitorInfo("startup", Path.Combine(install, "amz-cli"), temp.Root);
        info.StartupId = definition.Id;
        info.Arguments = [];
        registry.Register(info);

        var inspection = MonitorRestartCoordinator.Inspect(install, registry, startups);

        var item = Assert.Single(inspection.Items);
        Assert.True(item.CanRestart);
        Assert.NotNull(item.Startup);
    }

    private static MonitorPidInfo MonitorInfo(
        string mode,
        string executable,
        string savePath)
    {
        return new MonitorPidInfo
        {
            Id = "test-" + Guid.NewGuid().ToString("N"),
            Pid = Environment.ProcessId,
            ProcessStartedAt = CurrentProcessStartedAt(),
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            Mode = mode,
            Executable = executable,
            Arguments = ["monitor"],
            WorkingDirectory = savePath,
            SavePath = savePath,
            SaveFile = "monitor",
            LogPath = Path.Combine(savePath, "monitor.log"),
            PidFile = Path.Combine(savePath, "monitor.pid"),
        };
    }

    private static string InstallDir()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path)
            ? Path.GetTempPath()
            : Path.GetDirectoryName(Path.GetFullPath(path))
                ?? Path.GetTempPath();
    }

    private static DateTimeOffset CurrentProcessStartedAt()
    {
        using var process = Process.GetCurrentProcess();
        return new DateTimeOffset(
            process.StartTime.ToUniversalTime(),
            TimeSpan.Zero);
    }
}
