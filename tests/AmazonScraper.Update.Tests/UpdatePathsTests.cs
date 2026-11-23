using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class UpdatePathsTests
{
    [Fact]
    public void Lists_and_cleans_sibling_staging_directories()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "app");
        Directory.CreateDirectory(install);
        var staging = Path.Combine(temp.Path, ".amz-cli-update-abc");
        var rollback = Path.Combine(temp.Path, ".amz-cli-update-rollback-def");
        var restore = Path.Combine(temp.Path, ".amz-cli-restore-ghi");
        var keep = Path.Combine(temp.Path, "keep");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(rollback);
        Directory.CreateDirectory(restore);
        Directory.CreateDirectory(keep);

        var list = UpdatePaths.ListStaleSiblingDirectories(
            install,
            journal: null,
            minimumAge: TimeSpan.Zero);

        Assert.Contains(staging, list);
        Assert.Contains(rollback, list);
        Assert.Contains(restore, list);
        Assert.DoesNotContain(keep, list);

        var removed = UpdatePaths.CleanupStaleSiblingDirectories(
            install,
            journal: null,
            minimumAge: TimeSpan.Zero);

        Assert.Equal(3, removed);
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(rollback));
        Assert.False(Directory.Exists(restore));
        Assert.True(Directory.Exists(keep));
    }

    [Fact]
    public void Skips_directories_referenced_by_the_journal()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "app");
        Directory.CreateDirectory(install);
        var staging = Path.Combine(temp.Path, ".amz-cli-update-active");
        var rollback = Path.Combine(temp.Path, ".amz-cli-update-rollback-active");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(rollback);

        var journal = new UpdateJournal
        {
            StagingDir = staging,
            RollbackDir = rollback,
        };

        var list = UpdatePaths.ListStaleSiblingDirectories(
            install,
            journal,
            TimeSpan.Zero);

        Assert.DoesNotContain(staging, list);
        Assert.DoesNotContain(rollback, list);
    }

    [Fact]
    public void Skips_recent_directories_when_a_minimum_age_is_set()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "app");
        Directory.CreateDirectory(install);
        var recent = Path.Combine(temp.Path, ".amz-cli-update-recent");
        var old = Path.Combine(temp.Path, ".amz-cli-update-old");
        Directory.CreateDirectory(recent);
        Directory.CreateDirectory(old);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromHours(2));

        var list = UpdatePaths.ListStaleSiblingDirectories(
            install,
            journal: null,
            minimumAge: TimeSpan.FromHours(1));

        Assert.DoesNotContain(recent, list);
        Assert.Contains(old, list);
    }

    [Fact]
    public void MarkHidden_applies_the_attribute_on_windows()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, ".amz-cli-update-hidden");
        Directory.CreateDirectory(directory);

        UpdatePaths.MarkHidden(directory);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(
                (File.GetAttributes(directory) & FileAttributes.Hidden) != 0);
        }
        else
        {
            Assert.True(Directory.Exists(directory));
        }
    }
}
