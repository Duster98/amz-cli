using AmazonScraper.Cli.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorRegistryTests
{
    [Fact]
    public void Register_writes_an_entry_and_ReadAll_reads_it()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        var info = new MonitorPidInfo
        {
            RunId = "run-1",
            Pid = 4242,
            StartedAt = "2026-11-15T22:00:00.000Z",
            Mode = "detach",
            Domains = ["it", "de"],
            Targets = 3,
            TimerSeconds = 3600,
            SavePath = "/tmp/monitor",
            SaveFile = "amz-monitor",
            PidFile = "/tmp/monitor/amz-monitor.pid",
            DataFiles = ["/tmp/monitor/amz-monitor.items.jsonl"],
            LogPath = "/tmp/monitor/amz-monitor.log",
        };

        registry.Register(info);

        var entry = Assert.Single(registry.ReadAll());
        Assert.Equal("run-1", entry.Id);
        Assert.Equal("run-1", entry.RunId);
        Assert.Equal(4242, entry.Pid);
        Assert.Equal(["it", "de"], entry.Domains);
        Assert.Equal(3, entry.Targets);
        Assert.Equal(3600, entry.TimerSeconds);
        Assert.Equal("/tmp/monitor", entry.SavePath);
        Assert.Equal("amz-monitor", entry.SaveFile);
        Assert.Equal("/tmp/monitor/amz-monitor.pid", entry.PidFile);
        Assert.True(File.Exists(Path.Combine(directory.Root, "run-1.json")));
    }

    [Fact]
    public void Register_without_run_id_uses_the_pid_as_id()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        var info = new MonitorPidInfo { Pid = 1234 };

        registry.Register(info);

        Assert.Equal("pid-1234", info.Id);
        Assert.True(File.Exists(Path.Combine(directory.Root, "pid-1234.json")));
    }

    [Fact]
    public void Unregister_removes_the_entry()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        var info = new MonitorPidInfo { RunId = "run-2", Pid = 5678 };
        registry.Register(info);

        registry.Unregister(info.Id);

        Assert.Empty(registry.ReadAll());
        Assert.False(File.Exists(Path.Combine(directory.Root, "run-2.json")));
    }

    [Fact]
    public void Unregister_missing_id_is_a_no_op()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);

        registry.Unregister(null);
        registry.Unregister(string.Empty);
        registry.Unregister("missing");

        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void Reconcile_removes_stale_entries_of_the_same_monitor()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        var stale = new MonitorPidInfo
        {
            RunId = "old-run",
            Pid = int.MaxValue,
            SavePath = "/tmp/monitor",
            SaveFile = "amz-monitor",
            ProcessStartedAt = new DateTimeOffset(
                2000,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero),
        };
        registry.Register(stale);

        var unrelated = new MonitorPidInfo
        {
            RunId = "other-run",
            Pid = int.MaxValue - 1,
            SavePath = "/tmp/other",
            SaveFile = "other-monitor",
            ProcessStartedAt = stale.ProcessStartedAt,
        };
        registry.Register(unrelated);

        var current = new MonitorPidInfo
        {
            RunId = "new-run",
            Pid = Environment.ProcessId,
            SavePath = "/tmp/monitor",
            SaveFile = "amz-monitor",
        };

        var removed = registry.Reconcile(current);

        Assert.Equal(1, removed);
        var remaining = Assert.Single(registry.ReadAll());
        Assert.Equal("other-run", remaining.Id);
    }

    [Fact]
    public void Reconcile_removes_a_previous_run_that_reuses_the_current_pid()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        registry.Register(new MonitorPidInfo
        {
            RunId = "old-run",
            Pid = Environment.ProcessId,
            SavePath = "/tmp/monitor",
            SaveFile = "amz-monitor",
        });

        var current = new MonitorPidInfo
        {
            RunId = "new-run",
            Pid = Environment.ProcessId,
            SavePath = "/tmp/monitor",
            SaveFile = "amz-monitor",
        };

        Assert.Equal(1, registry.Reconcile(current));
        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void Reconcile_matches_a_restarted_startup_even_if_the_path_changed()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(directory.Root);
        registry.Register(new MonitorPidInfo
        {
            RunId = "old-run",
            StartupId = "startup-id",
            Pid = int.MaxValue,
            SavePath = "/tmp/old",
            SaveFile = "amz-monitor",
        });

        var current = new MonitorPidInfo
        {
            RunId = "new-run",
            StartupId = "startup-id",
            Pid = Environment.ProcessId,
            SavePath = "/tmp/new",
            SaveFile = "amz-monitor",
        };

        Assert.Equal(1, registry.Reconcile(current));
        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void ReadAll_ignores_malformed_entries()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Root, "broken.json"), "{not json");
        File.WriteAllText(Path.Combine(directory.Root, "ignored.txt"), "text");
        var registry = new MonitorRegistry(directory.Root);

        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void ReadAll_of_a_missing_directory_returns_empty()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(Path.Combine(directory.Root, "missing"));

        Assert.Empty(registry.ReadAll());
    }
}
