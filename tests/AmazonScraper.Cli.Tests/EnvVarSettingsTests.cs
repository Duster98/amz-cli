using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class EnvVarSettingsTests
{
    [Fact]
    public void Install_is_valid()
    {
        var settings = new EnvVarSettings { Install = true };
        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void System_install_is_valid()
    {
        var settings = new EnvVarSettings { Install = true, System = true };
        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void One_action_is_required()
    {
        Assert.False(new EnvVarSettings().Validate().Successful);
    }

    [Fact]
    public void Actions_are_mutually_exclusive()
    {
        Assert.False(new EnvVarSettings
        {
            Install = true,
            Status = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Status_does_not_accept_system()
    {
        Assert.False(new EnvVarSettings
        {
            Status = true,
            System = true,
        }.Validate().Successful);
    }
}
