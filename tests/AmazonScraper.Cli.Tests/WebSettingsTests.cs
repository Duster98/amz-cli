using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.Tests;

public class WebSettingsTests
{
    [Fact]
    public void Remote_mode_accepts_a_fixed_port()
    {
        var settings = new WebSettings
        {
            Remote = true,
            Port = 39285,
            NoOpen = true,
        };

        Assert.True(settings.Validate().Successful);
        Assert.True(settings.Remote);
        Assert.Equal(39285, settings.Port);
    }

    [Fact]
    public void Port_zero_remains_automatic()
    {
        Assert.True(new WebSettings { Port = 0 }.Validate().Successful);
    }

    [Fact]
    public void Port_outside_the_valid_range_is_rejected()
    {
        Assert.False(new WebSettings { Port = -1 }.Validate().Successful);
        Assert.False(new WebSettings { Port = 65536 }.Validate().Successful);
    }
}
