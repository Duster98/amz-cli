namespace AmazonScraper.Update.Tests;

public class FileSwapTests
{
    [Fact]
    public void Replaces_and_rolls_back_files()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var staging = Path.Combine(temp.Path, "staging");
        var rollback = Path.Combine(temp.Path, "rollback");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);

        var target = Path.Combine(install, "amz-cli");
        File.WriteAllText(target, "old");
        var source = Path.Combine(staging, "amz-cli");
        File.WriteAllText(source, "new");

        var staged = new StagedFile
        {
            RelativePath = "amz-cli",
            SourcePath = source,
            Mode = "0755",
            Size = 3,
            Sha256 = FileHash.Sha256("new"),
        };

        var swap = FileSwap.Apply(install, [staged], rollback);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.True(Directory.Exists(rollback));

        FileSwap.Rollback(swap);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.False(Directory.Exists(rollback));
    }

    [Fact]
    public void Commits_and_removes_the_rollback_copy()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var staging = Path.Combine(temp.Path, "staging");
        var rollback = Path.Combine(temp.Path, "rollback");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(install, "amz-cli"), "old");
        var source = Path.Combine(staging, "amz-cli");
        File.WriteAllText(source, "new");

        var swap = FileSwap.Apply(
            install,
            [
                new StagedFile
                {
                    RelativePath = "amz-cli",
                    SourcePath = source,
                    Mode = "0755",
                    Size = 3,
                    Sha256 = FileHash.Sha256("new"),
                },
            ],
            rollback);
        FileSwap.Commit(swap);

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "amz-cli")));
        Assert.False(Directory.Exists(rollback));
        Assert.True(swap.Committed);
    }

    [Fact]
    public void Rejects_a_path_outside_the_install_directory()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var staging = Path.Combine(temp.Path, "staging");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);
        var source = Path.Combine(staging, "evil");
        File.WriteAllText(source, "evil");

        Assert.Throws<UpdateException>(() =>
            FileSwap.Apply(
                install,
                [
                    new StagedFile
                    {
                        RelativePath = "../evil",
                        SourcePath = source,
                        Mode = "0644",
                        Size = 4,
                        Sha256 = FileHash.Sha256("evil"),
                    },
                ],
                Path.Combine(temp.Path, "rollback")));
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "amazon-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
