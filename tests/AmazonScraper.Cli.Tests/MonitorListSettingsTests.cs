using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class MonitorListSettingsTests
{
    [Fact]
    public void List_does_not_require_targets()
    {
        var settings = new MonitorSettings { List = true };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void List_accepts_save_path_and_output_flags()
    {
        var settings = new MonitorSettings
        {
            List = true,
            SavePath = "/tmp/monitor",
            All = true,
            Json = true,
            Verbose = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void List_rejects_status_and_stop()
    {
        Assert.False(new MonitorSettings
        {
            List = true,
            Status = true,
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            List = true,
            Stop = true,
        }.Validate().Successful);
    }

    [Fact]
    public void List_rejects_detach_and_single_run_flags()
    {
        foreach (var settings in new[]
                 {
                     new MonitorSettings { List = true, Detach = true },
                     new MonitorSettings { List = true, Once = true },
                     new MonitorSettings { List = true, DryRun = true },
                     new MonitorSettings { List = true, Cycles = 2 },
                     new MonitorSettings { List = true, HtmlFile = "/tmp/item.html" },
                 })
        {
            Assert.False(settings.Validate().Successful);
        }
    }

    [Fact]
    public void List_rejects_targets()
    {
        Assert.False(new MonitorSettings
        {
            List = true,
            Asins = ["B000000001"],
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            List = true,
            AsinFile = "/tmp/asins.txt",
        }.Validate().Successful);
    }

    [Fact]
    public void List_rejects_notify_test()
    {
        var settings = new MonitorSettings
        {
            List = true,
            NotifyTest = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void List_flags_require_list()
    {
        foreach (var settings in new[]
                 {
                     new MonitorSettings { All = true },
                     new MonitorSettings { Prune = true },
                     new MonitorSettings { Json = true },
                     new MonitorSettings { TkOpt = true },
                     new MonitorSettings { Verbose = true },
                 })
        {
            Assert.False(settings.Validate().Successful);
        }
    }

    [Fact]
    public void Stats_and_restart_are_standalone_commands()
    {
        Assert.True(new MonitorSettings { Stats = true }.Validate().Successful);
        Assert.True(new MonitorSettings { Restart = true }.Validate().Successful);
        Assert.True(new MonitorSettings
        {
            Stats = true,
            Json = true,
        }.Validate().Successful);
        Assert.True(new MonitorSettings
        {
            Restart = true,
            Json = true,
        }.Validate().Successful);

        Assert.False(new MonitorSettings
        {
            Stats = true,
            Restart = true,
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            Stats = true,
            List = true,
        }.Validate().Successful);
        Assert.False(new MonitorSettings
        {
            Restart = true,
            List = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Verify_requires_list()
    {
        Assert.False(new MonitorSettings
        {
            Verify = true,
        }.Validate().Successful);
        Assert.True(new MonitorSettings
        {
            List = true,
            Verify = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Json_and_tkopt_are_mutually_exclusive()
    {
        var settings = new MonitorSettings
        {
            List = true,
            Json = true,
            TkOpt = true,
        };

        Assert.False(settings.Validate().Successful);
    }
}
