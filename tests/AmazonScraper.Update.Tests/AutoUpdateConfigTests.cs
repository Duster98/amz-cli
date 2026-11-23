using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class AutoUpdateConfigTests
{
    [Fact]
    public void Defaults_are_enabled_hourly_and_stable()
    {
        var config = new AutoUpdateConfig();

        Assert.True(config.Enabled);
        Assert.Equal("stable", config.Channel);
        Assert.Equal(3600, config.IntervalSeconds);
        Assert.True(config.AutoApply);
        Assert.True(config.RestartMonitors);
        Assert.True(config.DeferForegroundMonitor);
        Assert.True(config.Notify);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        var config = new AutoUpdateConfig
        {
            Enabled = false,
            Channel = "beta",
            IntervalSeconds = 120,
            AutoApply = false,
            RestartMonitors = false,
            DeferForegroundMonitor = false,
        };

        config.Save(path);
        var loaded = AutoUpdateConfig.Load(path);

        Assert.False(loaded.Enabled);
        Assert.Equal("beta", loaded.Channel);
        Assert.Equal(120, loaded.IntervalSeconds);
        Assert.False(loaded.AutoApply);
        Assert.False(loaded.RestartMonitors);
        Assert.False(loaded.DeferForegroundMonitor);
    }

    [Fact]
    public void Invalid_file_falls_back_to_defaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        File.WriteAllText(path, "{not-json");

        var config = AutoUpdateConfig.Load(path);

        Assert.True(config.Enabled);
        Assert.Equal("stable", config.Channel);
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    public void Rejects_invalid_channel(string channel)
    {
        var config = new AutoUpdateConfig { Channel = channel };

        Assert.Throws<UpdateException>(() => config.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(604801)]
    public void Rejects_invalid_interval(int seconds)
    {
        var config = new AutoUpdateConfig { IntervalSeconds = seconds };

        Assert.Throws<UpdateException>(() => config.Validate());
    }

    [Fact]
    public void State_is_due_when_missing_and_not_due_after_checked()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "last-check.json");
        var config = new AutoUpdateConfig { IntervalSeconds = 3600 };

        Assert.True(AutoUpdateState.IsDue(config, path: path));

        var result = new UpdateCheckResult(
            UpdateStatus.UpToDate,
            "2.26.3",
            "linux-x64",
            "stable",
            null,
            new ReleaseManifest
            {
                Version = "2.26.3",
                Platform = "linux-x64",
                Channel = "stable",
            },
            "ok");
        AutoUpdateState.MarkChecked(result, "startup", path);

        Assert.False(AutoUpdateState.IsDue(config, path: path));
        var state = AutoUpdateState.Read(path);
        Assert.Equal("2.26.3", state.LatestVersion);
        Assert.Equal("UpToDate", state.LastStatus);
        Assert.Equal("startup", state.LastTrigger);
    }

    [Fact]
    public void Zero_interval_is_always_due()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "last-check.json");
        var config = new AutoUpdateConfig { IntervalSeconds = 0 };
        var result = new UpdateCheckResult(
            UpdateStatus.UpToDate,
            "2.26.3",
            "linux-x64",
            "stable",
            null,
            null,
            "ok");
        AutoUpdateState.MarkChecked(result, "startup", path);

        Assert.True(AutoUpdateState.IsDue(config, path: path));
    }

    [Fact]
    public void Failed_check_is_cached_to_avoid_hammering()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "last-check.json");
        var config = new AutoUpdateConfig { IntervalSeconds = 3600 };

        AutoUpdateState.MarkFailed("startup", "offline", path);

        Assert.False(AutoUpdateState.IsDue(config, path: path));
        var state = AutoUpdateState.Read(path);
        Assert.StartsWith("error:", state.LastStatus);
    }
}
