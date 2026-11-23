using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class PathsSettingsTests
{
    [Fact]
    public void Default_settings_are_valid()
    {
        Assert.True(new PathsSettings().Validate().Successful);
    }

    [Fact]
    public void Empty_save_path_is_rejected()
    {
        var settings = new PathsSettings
        {
            SavePaths = [""],
        };

        Assert.False(settings.Validate().Successful);
    }
}
