using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorDiscoveryTests
{
    [Fact]
    public void Active_registry_entry_is_listed()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(RunningInfo("active"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: false, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal(Environment.ProcessId, entry.Pid);
        Assert.Equal(["it"], entry.Domains);
        Assert.Equal(2, entry.Targets);
        Assert.Equal(3600, entry.TimerSeconds);
        Assert.Equal(0, result.StaleCount);
        Assert.Equal(0, result.PrunedCount);
    }

    [Fact]
    public void Stale_entries_are_hidden_by_default()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(StaleInfo("stale"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var activeOnly = discovery.Discover(includeStale: false, prune: false);

        Assert.Empty(activeOnly.Entries);
        Assert.Equal(1, activeOnly.StaleCount);
        Assert.Equal(0, activeOnly.PrunedCount);
    }

    [Fact]
    public void All_includes_stale_entries_after_active_ones()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        var stale = StaleInfo("stale");
        stale.StartedAt = "2026-11-15T23:00:00.000Z";
        stale.SavePath = "/tmp/other-monitor";
        stale.SaveFile = "other-monitor";
        stale.PidFile = "/tmp/other-monitor/other-monitor.pid";
        registry.Register(stale);
        registry.Register(RunningInfo("active"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: false);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("running", result.Entries[0].Status);
        Assert.Equal("stale", result.Entries[1].Status);
        Assert.Equal(1, result.StaleCount);
    }

    [Fact]
    public void All_hides_a_stale_run_from_the_same_monitor()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(StaleInfo("old-run"));
        registry.Register(RunningInfo("new-run"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal(Environment.ProcessId, entry.Pid);
        Assert.Equal(0, result.StaleCount);
    }

    [Fact]
    public void Prune_removes_a_superseded_stale_run()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(StaleInfo("old-run"));
        registry.Register(RunningInfo("new-run"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: true);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal(1, result.PrunedCount);
        var remaining = Assert.Single(registry.ReadAll());
        Assert.Equal("new-run", remaining.Id);
    }

    [Fact]
    public void Duplicate_pid_prefers_the_live_process()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        var running = RunningInfo("a-running");
        running.Pid = Environment.ProcessId;
        running.ProcessStartedAt = null;
        running.StartedAt = "2026-11-17T22:00:00.000Z";
        registry.Register(running);

        var stale = StaleInfo("z-stale");
        stale.Pid = Environment.ProcessId;
        stale.StartedAt = "2026-11-15T22:00:00.000Z";
        stale.SavePath = "/tmp/other-monitor";
        stale.SaveFile = "other-monitor";
        stale.PidFile = "/tmp/other-monitor/other-monitor.pid";
        registry.Register(stale);

        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal("2026-11-17T22:00:00.000Z", entry.StartedAt);
    }

    [Fact]
    public void Prune_removes_the_registry_entry_and_pidfile()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var pidFile = Path.Combine(scanDirectory.Root, "amz-monitor.pid");
        var info = StaleInfo("stale");
        info.PidFile = pidFile;
        MonitorPidInfo.Save(pidFile, info);
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(info);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: true);

        Assert.Empty(result.Entries);
        Assert.Equal(1, result.StaleCount);
        Assert.Equal(1, result.PrunedCount);
        Assert.Empty(registry.ReadAll());
        Assert.False(File.Exists(pidFile));
    }

    [Fact]
    public void Pidfile_is_discovered_without_a_registry_entry()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var pidFile = Path.Combine(scanDirectory.Root, "amz-monitor.pid");
        var info = RunningInfo("pidfile");
        info.Id = string.Empty;
        info.RunId = null;
        info.PidFile = null;
        info.SavePath = null;
        info.SaveFile = null;
        MonitorPidInfo.Save(pidFile, info);
        var registry = new MonitorRegistry(registryDirectory.Root);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: false, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal(scanDirectory.Root, entry.SavePath);
        Assert.Equal("amz-monitor", entry.SaveFile);
        Assert.Equal(pidFile, entry.PidFile);
    }

    [Fact]
    public void Registry_metadata_wins_over_a_pidfile_for_the_same_pid()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var pidFile = Path.Combine(scanDirectory.Root, "amz-monitor.pid");
        var pidfileInfo = RunningInfo("pidfile");
        pidfileInfo.Domains = ["de"];
        pidfileInfo.SavePath = scanDirectory.Root;
        pidfileInfo.SaveFile = "amz-monitor";
        MonitorPidInfo.Save(pidFile, pidfileInfo);

        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(RunningInfo("registry"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: false, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(["it"], entry.Domains);
    }

    [Fact]
    public async Task Last_seen_comes_from_the_state_file()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        using var stateDirectory = new TempDirectory();
        var state = new MonitorState();
        state.Targets["it:B000000001"] = new MonitorTargetState
        {
            LastSeenAt = new DateTimeOffset(
                2026,
                11,
                15,
                22,
                1,
                0,
                TimeSpan.Zero),
        };
        await MonitorStateStore.SaveAsync(
            Path.Combine(stateDirectory.Root, "amz-monitor.state.json"),
            state);

        var registry = new MonitorRegistry(registryDirectory.Root);
        var info = RunningInfo("state");
        info.SavePath = stateDirectory.Root;
        info.SaveFile = "amz-monitor";
        registry.Register(info);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: false, prune: false);

        Assert.Equal("2026-11-15T22:01:00.000Z", Assert.Single(result.Entries).LastSeenAt);
    }

    [Fact]
    public void Prune_does_not_delete_a_pidfile_reused_by_another_monitor()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var pidFile = Path.Combine(scanDirectory.Root, "amz-monitor.pid");
        var stale = StaleInfo("old-run");
        stale.PidFile = pidFile;
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(stale);
        MonitorPidInfo.Save(pidFile, RunningInfo("new-run"));
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: true);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal(pidFile, entry.PidFile);
        Assert.Equal(1, result.PrunedCount);
        Assert.Empty(registry.ReadAll());
        Assert.True(File.Exists(pidFile));
        Assert.Equal("new-run", MonitorPidInfo.TryLoad(pidFile)!.Id);
    }

    [Fact]
    public void Prune_does_not_unregister_a_registry_entry_with_the_same_id()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        var pidFile = Path.Combine(scanDirectory.Root, "amz-monitor.pid");
        var registry = new MonitorRegistry(registryDirectory.Root);
        registry.Register(RunningInfo("same-id"));
        var stale = StaleInfo("same-id");
        stale.PidFile = pidFile;
        MonitorPidInfo.Save(pidFile, stale);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: true);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Single(registry.ReadAll());
    }

    [Fact]
    public void Startup_definition_marks_a_running_entry()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        using var dataDirectory = new TempDirectory();
        var registry = new MonitorRegistry(registryDirectory.Root);
        var info = RunningInfo("startup-run");
        info.SavePath = dataDirectory.Root;
        info.Mode = "startup";
        var startup = StartupDefinitionFor(
            dataDirectory.Root,
            "amz-monitor");
        info.Startup = startup.Kind;
        info.StartupId = startup.Id;
        info.StartupName = startup.Name;
        registry.Register(info);
        var startups = StartupRegistryFor(registryDirectory);
        startups.Save(startup);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            startups);

        var result = discovery.Discover(includeStale: false, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("systemd-user", entry.Startup);
        Assert.True(entry.StartupEnabled);
        Assert.Equal(startup.Name, entry.StartupName);
        Assert.Equal(startup.Id, entry.StartupId);
    }

    [Fact]
    public void All_includes_a_configured_startup_without_a_process()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        using var dataDirectory = new TempDirectory();
        var startup = StartupDefinitionFor(
            dataDirectory.Root,
            "amz-monitor");
        var startups = StartupRegistryFor(registryDirectory);
        startups.Save(startup);
        var discovery = new MonitorDiscovery(
            new MonitorRegistry(registryDirectory.Root),
            [scanDirectory.Root],
            startups);

        var activeOnly = discovery.Discover(includeStale: false, prune: false);
        var all = discovery.Discover(includeStale: true, prune: false);

        Assert.Empty(activeOnly.Entries);
        var entry = Assert.Single(all.Entries);
        Assert.Equal("configured", entry.Status);
        Assert.Equal("startup", entry.Mode);
        Assert.Equal("systemd-user", entry.Startup);
        Assert.Equal(startup.Name, entry.StartupName);
        Assert.Equal(0, entry.Pid);
    }

    [Fact]
    public void All_does_not_duplicate_a_configured_startup_with_a_running_process()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        using var dataDirectory = new TempDirectory();
        var startup = StartupDefinitionFor(
            dataDirectory.Root,
            "amz-monitor");
        var registry = new MonitorRegistry(registryDirectory.Root);
        var info = RunningInfo("startup-run");
        info.SavePath = dataDirectory.Root;
        info.Startup = startup.Kind;
        info.StartupId = startup.Id;
        registry.Register(info);
        var startups = StartupRegistryFor(registryDirectory);
        startups.Save(startup);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            startups);

        var result = discovery.Discover(includeStale: true, prune: false);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("running", entry.Status);
        Assert.Equal("systemd-user", entry.Startup);
    }

    [Fact]
    public void Prune_keeps_a_configured_startup()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        using var dataDirectory = new TempDirectory();
        var startup = StartupDefinitionFor(
            dataDirectory.Root,
            "amz-monitor");
        var registry = new MonitorRegistry(registryDirectory.Root);
        var stale = StaleInfo("stale-run");
        stale.SavePath = dataDirectory.Root;
        stale.PidFile = null;
        registry.Register(stale);
        var startups = StartupRegistryFor(registryDirectory);
        startups.Save(startup);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            startups);

        var result = discovery.Discover(includeStale: true, prune: true);

        Assert.Empty(registry.ReadAll());
        var entry = Assert.Single(result.Entries);
        Assert.Equal("configured", entry.Status);
        Assert.Equal("systemd-user", entry.Startup);
        Assert.Single(startups.ReadAll());
    }

    [Fact]
    public void Invalid_pidfiles_are_ignored()
    {
        using var registryDirectory = new TempDirectory();
        using var scanDirectory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(scanDirectory.Root, "broken.pid"),
            "{not json");
        var registry = new MonitorRegistry(registryDirectory.Root);
        var discovery = new MonitorDiscovery(
            registry,
            [scanDirectory.Root],
            StartupRegistryFor(registryDirectory));

        var result = discovery.Discover(includeStale: true, prune: false);

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.StaleCount);
    }

    private static StartupDefinition StartupDefinitionFor(
        string savePath,
        string saveFile)
    {
        var id = StartupIdentity.CreateId(savePath, saveFile);
        var normalized = StartupIdentity.NormalizePath(savePath);
        return new StartupDefinition
        {
            Id = id,
            Kind = "systemd-user",
            Name = $"amz-monitor-{id}.service",
            Enabled = true,
            CreatedAt = "2026-11-15T22:00:00.000Z",
            Executable = "/usr/bin/amz-cli",
            WorkingDirectory = normalized,
            SavePath = normalized,
            SaveFile = saveFile,
            Domains = ["it"],
            Targets = 1,
            TimerSeconds = 3600,
            DataFiles =
            [
                Path.Combine(normalized, $"{saveFile}.items.jsonl"),
            ],
            LogPath = Path.Combine(normalized, $"{saveFile}.log"),
            Arguments = ["monitor", "--config", "/tmp/monitor.json"],
        };
    }

    private static StartupRegistry StartupRegistryFor(TempDirectory directory) =>
        new(Path.Combine(directory.Root, "startups"));

    private static MonitorPidInfo RunningInfo(string id) => new()
    {
        Id = id,
        RunId = id,
        Pid = Environment.ProcessId,
        StartedAt = "2026-11-15T22:00:00.000Z",
        Mode = "detach",
        Domains = ["it"],
        Targets = 2,
        TimerSeconds = 3600,
        SavePath = "/tmp/monitor",
        SaveFile = "amz-monitor",
        PidFile = "/tmp/monitor/amz-monitor.pid",
        DataFiles = ["/tmp/monitor/amz-monitor.items.jsonl"],
        LogPath = "/tmp/monitor/amz-monitor.log",
    };

    private static MonitorPidInfo StaleInfo(string id)
    {
        var info = RunningInfo(id);
        info.Pid = int.MaxValue;
        info.ProcessStartedAt = new DateTimeOffset(
            2000,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        return info;
    }
}
