using AmazonScraper.Cli.Startup;

namespace AmazonScraper.Cli.Tests;

public class StartupIdentityTests
{
    [Fact]
    public void Id_is_stable_and_twelve_hex_characters()
    {
        var first = StartupIdentity.CreateId("/tmp/monitor", "amz-monitor");
        var second = StartupIdentity.CreateId("/tmp/monitor/", "amz-monitor");

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{12}$", first);
    }

    [Fact]
    public void Different_paths_or_files_get_different_ids()
    {
        var first = StartupIdentity.CreateId("/tmp/monitor-a", "amz-monitor");
        var second = StartupIdentity.CreateId("/tmp/monitor-b", "amz-monitor");
        var third = StartupIdentity.CreateId("/tmp/monitor-a", "other-monitor");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void PathsEqual_normalizes_trailing_separators()
    {
        var path = Path.Combine(Path.GetTempPath(), "startup-test");
        Assert.True(StartupIdentity.PathsEqual(path, path + Path.DirectorySeparatorChar));
        Assert.False(StartupIdentity.PathsEqual(path, path + "-other"));
    }

    [Fact]
    public void FilesEqual_compares_save_file_names()
    {
        Assert.True(StartupIdentity.FilesEqual("amz-monitor", "amz-monitor"));
        Assert.False(StartupIdentity.FilesEqual("amz-monitor", "other"));
    }
}
