using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class AppPathCatalogTests
{
    [Fact]
    public void Catalog_includes_install_state_and_monitor_entries()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli"), "binary");
        var stateRoot = fixture.CreateDirectory("state");
        var monitorRoot = fixture.CreateDirectory("monitor-data");
        var monitorDir = Path.Combine(stateRoot, "monitors");
        var startupDir = Path.Combine(stateRoot, "startups");
        fixture.CreateFile(Path.Combine(stateRoot, "updates", "config.json"), "{}");
        fixture.CreateFile(Path.Combine(stateRoot, "updates", "update.log"), "log");

        var info = new MonitorPidInfo
        {
            Id = "monitor-1",
            Pid = 999_999,
            SavePath = monitorRoot,
            SaveFile = "amz-monitor",
            PidFile = Path.Combine(monitorRoot, "amz-monitor.pid"),
            LogPath = Path.Combine(monitorRoot, "amz-monitor.log"),
            DataFiles = [Path.Combine(monitorRoot, "amz-monitor.items.jsonl")],
            Domains = ["it"],
            Targets = 1,
            Arguments = ["monitor", "--cookie", "secret-cookie"],
        };
        new MonitorRegistry(monitorDir).Register(info);
        fixture.CreateFile(
            Path.Combine(monitorRoot, "amz-monitor.state.json"),
            "{}");
        fixture.CreateFile(
            Path.Combine(monitorRoot, "amz-monitor.log"),
            "log");
        fixture.CreateFile(
            Path.Combine(monitorRoot, "amz-monitor.items.jsonl"),
            "{}");

        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = monitorDir,
            StartupRegistryDirectory = startupDir,
            SystemdDirectory = Path.Combine(stateRoot, "systemd"),
            IncludeMissing = true,
            IsWindows = false,
        });

        Assert.Contains(result.Groups, group => group.Id == "install");
        Assert.Contains(result.Groups, group => group.Id == "state");
        var monitorGroup = Assert.Single(
            result.Groups,
            group => group.Id.StartsWith("monitor:", StringComparison.Ordinal));
        Assert.Equal("stale", monitorGroup.Status);
        Assert.Contains(
            monitorGroup.Entries,
            entry => entry.Path.EndsWith(
                "amz-monitor.state.json",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Groups.SelectMany(group => group.Entries),
            entry => entry.Path.EndsWith(
                "updates/update.log",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Groups.SelectMany(group => group.Entries),
            entry => entry.Path.EndsWith(
                "http-settings.json",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Groups.SelectMany(group => group.Entries),
            entry => entry.Path.EndsWith(
                "web/compare.json",
                StringComparison.Ordinal));

        var json = PathTreeRenderer.ToJson(result);
        Assert.DoesNotContain("secret-cookie", json);
        Assert.DoesNotContain("--cookie", json);
    }

    [Fact]
    public void Missing_paths_are_hidden_without_all_and_included_with_all()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        var stateRoot = fixture.CreateDirectory("state");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli"), "binary");

        var options = new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = Path.Combine(stateRoot, "monitors"),
            StartupRegistryDirectory = Path.Combine(stateRoot, "startups"),
            SystemdDirectory = Path.Combine(stateRoot, "systemd"),
            IsWindows = false,
        };

        var normal = AppPathCatalog.Build(options);
        var all = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = options.StateRoot,
            InstallDir = options.InstallDir,
            MonitorRegistryDirectory = options.MonitorRegistryDirectory,
            StartupRegistryDirectory = options.StartupRegistryDirectory,
            SystemdDirectory = options.SystemdDirectory,
            IsWindows = false,
            IncludeMissing = true,
        });

        Assert.DoesNotContain(
            normal.Groups.SelectMany(group => group.Entries),
            entry => entry.Status == "missing");
        Assert.Contains(
            all.Groups.SelectMany(group => group.Entries),
            entry => entry.Status == "missing");
    }

    [Fact]
    public void Install_flag_expands_installation_files()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli"), "binary");
        fixture.CreateFile(Path.Combine(installDir, "libcurl-impersonate.dll"), "dll");
        var stateRoot = fixture.CreateDirectory("state");

        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = Path.Combine(stateRoot, "monitors"),
            StartupRegistryDirectory = Path.Combine(stateRoot, "startups"),
            SystemdDirectory = Path.Combine(stateRoot, "systemd"),
            IncludeInstall = true,
            IsWindows = false,
        });

        var install = Assert.Single(
            result.Groups,
            group => group.Id == "install");
        Assert.Contains(
            install.Entries,
            entry => entry.Path.EndsWith(
                "libcurl-impersonate.dll",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_catalog_lists_task_scheduler_registrations()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        var stateRoot = fixture.CreateDirectory("state");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli.exe"), "binary");
        var startup = new StartupDefinition
        {
            Id = "abc123",
            Kind = "windows-task",
            Name = "AmzCLI-abc123",
            SavePath = Path.Combine(stateRoot, "monitor"),
            SaveFile = "amz-monitor",
            Domains = ["it"],
            Targets = 1,
        };
        new StartupRegistry(Path.Combine(stateRoot, "startups")).Save(startup);

        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = Path.Combine(stateRoot, "monitors"),
            StartupRegistryDirectory = Path.Combine(stateRoot, "startups"),
            IsWindows = true,
            IncludeMissing = true,
        });

        var external = Assert.Single(
            result.Groups,
            group => group.Id == "external");
        Assert.Contains(
            external.Entries,
            entry => entry.Path == "AmzCLI-abc123"
                && entry.Status == "configured");
        Assert.Contains(
            result.Groups.SelectMany(group => group.Entries),
            entry => entry.Path.EndsWith(
                "amz-cli.exe",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Additional_save_path_finds_an_unregistered_pidfile()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        var stateRoot = fixture.CreateDirectory("state");
        var monitorDir = fixture.CreateDirectory("monitor");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli"), "binary");
        MonitorPidInfo.Save(
            Path.Combine(monitorDir, "amz-monitor.pid"),
            new MonitorPidInfo
            {
                Id = "external",
                Pid = 999_999,
                SavePath = monitorDir,
                SaveFile = "amz-monitor",
                PidFile = Path.Combine(monitorDir, "amz-monitor.pid"),
                DataFiles = [],
            });

        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = Path.Combine(stateRoot, "monitors"),
            StartupRegistryDirectory = Path.Combine(stateRoot, "startups"),
            SystemdDirectory = Path.Combine(stateRoot, "systemd"),
            AdditionalSavePaths = [monitorDir],
            IsWindows = false,
        });

        Assert.Contains(
            result.Groups,
            group => group.Id.StartsWith("monitor:", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_and_startup_with_same_schedule_are_merged()
    {
        using var fixture = new CatalogFixture();
        var installDir = fixture.CreateDirectory("install");
        var stateRoot = fixture.CreateDirectory("state");
        fixture.CreateFile(Path.Combine(installDir, "amz-cli"), "binary");
        var savePath = fixture.CreateDirectory("monitor");
        var monitorDir = Path.Combine(stateRoot, "monitors");
        var startupDir = Path.Combine(stateRoot, "startups");
        var info = new MonitorPidInfo
        {
            Id = "same",
            Pid = 999_999,
            SavePath = savePath,
            SaveFile = "amz-monitor",
            PidFile = Path.Combine(savePath, "amz-monitor.pid"),
            DataFiles = [],
        };
        new MonitorRegistry(monitorDir).Register(info);
        new StartupRegistry(startupDir).Save(new StartupDefinition
        {
            Id = "same-startup",
            Kind = "systemd-user",
            Name = "amz-monitor-same.service",
            SavePath = savePath,
            SaveFile = "amz-monitor",
            Domains = ["it"],
        });

        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            StateRoot = stateRoot,
            InstallDir = installDir,
            MonitorRegistryDirectory = monitorDir,
            StartupRegistryDirectory = startupDir,
            SystemdDirectory = Path.Combine(stateRoot, "systemd"),
            IncludeMissing = true,
            IsWindows = false,
        });

        Assert.Single(
            result.Groups,
            group => group.Title.StartsWith("Monitor ", StringComparison.Ordinal));
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"amazon-paths-{Guid.NewGuid():N}");

        public CatalogFixture()
        {
            Directory.CreateDirectory(_root);
        }

        public string CreateDirectory(string relative)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
