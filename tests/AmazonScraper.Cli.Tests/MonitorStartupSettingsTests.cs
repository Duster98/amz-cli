using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class MonitorStartupSettingsTests
{
    [Fact]
    public void Startup_accepts_a_config()
    {
        var settings = new MonitorSettings
        {
            Startup = true,
            Config = "/tmp/monitor.json",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_accepts_cli_targets()
    {
        var settings = new MonitorSettings
        {
            Startup = true,
            Asins = ["B000000001"],
            Timer = 3600,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_accepts_adaptive_schedule_options()
    {
        var settings = new MonitorSettings
        {
            Startup = true,
            Asins = ["B000000001"],
            Schedule = "adaptive",
            Window = 86_400,
            MaxRequests = 500,
            MinGap = 600,
            MaxGap = 1200,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_remove_does_not_require_targets()
    {
        var settings = new MonitorSettings
        {
            StartupRemove = true,
            SavePath = "/tmp/monitor",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_remove_accepts_a_config()
    {
        var settings = new MonitorSettings
        {
            StartupRemove = true,
            Config = "/tmp/monitor.json",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_and_startup_remove_are_mutually_exclusive()
    {
        var settings = new MonitorSettings
        {
            Startup = true,
            StartupRemove = true,
            Config = "/tmp/monitor.json",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Startup_rejects_control_flows()
    {
        foreach (var settings in new[]
                 {
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Detach = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Status = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Stop = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", List = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Once = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Cycles = 2 },
                     new MonitorSettings { Startup = true, Asins = ["B000000001"], HtmlFile = "/tmp/a.html" },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", DryRun = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", NotifyTest = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", ExitOnAlert = true },
                 })
        {
            Assert.False(settings.Validate().Successful);
        }
    }

    [Fact]
    public void Startup_rejects_list_output_flags()
    {
        foreach (var settings in new[]
                 {
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", All = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Prune = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Json = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", TkOpt = true },
                     new MonitorSettings { Startup = true, Config = "/tmp/m.json", Verbose = true },
                 })
        {
            Assert.False(settings.Validate().Successful);
        }
    }

    [Fact]
    public void Startup_remove_rejects_targets_and_control_flows()
    {
        Assert.False(new MonitorSettings
        {
            StartupRemove = true,
            Asins = ["B000000001"],
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            StartupRemove = true,
            AsinFile = "/tmp/asins.txt",
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            StartupRemove = true,
            Status = true,
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            StartupRemove = true,
            Detach = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Startup_requires_a_source()
    {
        var settings = new MonitorSettings { Startup = true };

        Assert.False(settings.Validate().Successful);
    }
}
