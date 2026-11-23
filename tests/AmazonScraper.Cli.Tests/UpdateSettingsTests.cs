using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class UpdateSettingsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        var settings = new UpdateSettings();

        Assert.True(settings.Validate().Successful);
        Assert.Equal("stable", settings.Channel);
        Assert.Equal(30, settings.Timeout);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("beta")]
    public void Accepts_supported_channels(string channel)
    {
        var settings = new UpdateSettings { Channel = channel };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Rejects_an_unknown_channel()
    {
        var settings = new UpdateSettings { Channel = "nightly" };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Rejects_multiple_modes()
    {
        var settings = new UpdateSettings { Check = true, Status = true };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Rejects_yes_with_check()
    {
        var settings = new UpdateSettings { Check = true, Yes = true };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Allows_yes_with_rollback()
    {
        var settings = new UpdateSettings { Rollback = true, Yes = true };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Auto_update_controls_are_valid()
    {
        Assert.True(new UpdateSettings { EnableAuto = true }
            .Validate().Successful);
        Assert.True(new UpdateSettings { DisableAuto = true }
            .Validate().Successful);
        Assert.False(new UpdateSettings
        {
            EnableAuto = true,
            DisableAuto = true,
        }.Validate().Successful);
        Assert.False(new UpdateSettings
        {
            EnableAuto = true,
            Check = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Delete_backups_is_valid_alone()
    {
        Assert.True(new UpdateSettings { DeleteBackups = true }
            .Validate().Successful);
        Assert.True(new UpdateSettings { DeleteBackups = true, Yes = true }
            .Validate().Successful);
        Assert.True(new UpdateSettings { DeleteBackups = true, DryRun = true }
            .Validate().Successful);
    }

    [Fact]
    public void Delete_backups_conflicts_with_other_modes()
    {
        Assert.False(new UpdateSettings
        {
            DeleteBackups = true,
            Check = true,
        }.Validate().Successful);
        Assert.False(new UpdateSettings
        {
            DeleteBackups = true,
            Status = true,
        }.Validate().Successful);
        Assert.False(new UpdateSettings
        {
            DeleteBackups = true,
            Rollback = true,
        }.Validate().Successful);
        Assert.False(new UpdateSettings
        {
            DeleteBackups = true,
            EnableAuto = true,
        }.Validate().Successful);
        Assert.False(new UpdateSettings
        {
            DeleteBackups = true,
            DisableAuto = true,
        }.Validate().Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Rejects_invalid_timeout(int seconds)
    {
        var settings = new UpdateSettings { Timeout = seconds };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Verify_requires_version_and_platform()
    {
        Assert.False(new UpdateVerifySettings().Validate().Successful);
        Assert.True(new UpdateVerifySettings
        {
            ExpectedVersion = "2.26.0",
            ExpectedPlatform = "linux-x64",
        }.Validate().Successful);
    }

    [Fact]
    public void Apply_requires_a_plan()
    {
        Assert.False(new UpdateApplySettings().Validate().Successful);
        Assert.True(new UpdateApplySettings { Plan = "/tmp/plan.json" }
            .Validate().Successful);
    }

    [Fact]
    public void Rollback_helper_requires_all_paths()
    {
        Assert.False(new UpdateRollbackSettings().Validate().Successful);
        Assert.True(new UpdateRollbackSettings
        {
            Backup = "/tmp/backup.tar.gz",
            Version = "2.26.0",
            Platform = "linux-x64",
            InstallDir = "/tmp/install",
        }.Validate().Successful);
    }
}
