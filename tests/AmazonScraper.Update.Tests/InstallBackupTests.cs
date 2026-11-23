using System.Text;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class InstallBackupTests
{
    [Fact]
    public async Task Creates_a_verified_backup_and_excludes_user_data()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(install, "data"));
        Directory.CreateDirectory(Path.Combine(install, "config"));
        File.WriteAllText(Path.Combine(install, "amz-cli"), "binary");
        File.WriteAllText(Path.Combine(install, "data", "state.json"), "user");
        File.WriteAllText(Path.Combine(install, "config", "settings.json"), "user");

        var result = await InstallBackup.CreateAsync(
            install,
            "1.0.0",
            "linux-x64",
            backups);

        Assert.True(File.Exists(result.ArchivePath));
        Assert.True(File.Exists(result.MetadataPath));
        Assert.True(result.Size > 0);
        Assert.Equal(64, result.Sha256.Length);

        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(
            File.ReadAllText(result.MetadataPath),
            ReleaseJson.Options);
        Assert.NotNull(metadata);
        Assert.Contains(metadata.Files, file => file.Path == "amz-cli");
        Assert.DoesNotContain(metadata.Files, file => file.Path.StartsWith("data/"));
        Assert.DoesNotContain(metadata.Files, file => file.Path.StartsWith("config/"));
    }

    [Fact]
    public async Task Lists_and_prunes_old_backups()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "amz-cli"), "v1");

        var first = await InstallBackup.CreateAsync(install, "1.0.0", "linux-x64", backups);
        File.WriteAllText(Path.Combine(install, "amz-cli"), "v2");
        var second = await InstallBackup.CreateAsync(install, "2.0.0", "linux-x64", backups);
        File.WriteAllText(Path.Combine(install, "amz-cli"), "v3");
        var third = await InstallBackup.CreateAsync(install, "3.0.0", "linux-x64", backups);

        var list = InstallBackup.List(backups);
        Assert.Equal(3, list.Count);

        InstallBackup.Prune(backups, 2);
        var pruned = InstallBackup.List(backups);
        Assert.Equal(2, pruned.Count);
        Assert.DoesNotContain(pruned, item => item.ArchivePath == first.ArchivePath);
        Assert.Contains(pruned, item => item.ArchivePath == second.ArchivePath);
        Assert.Contains(pruned, item => item.ArchivePath == third.ArchivePath);
    }

    [Fact]
    public async Task Filters_backups_by_installation_directory()
    {
        using var temp = new TempDirectory();
        var installA = Path.Combine(temp.Path, "install-a");
        var installB = Path.Combine(temp.Path, "install-b");
        var backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(installA);
        Directory.CreateDirectory(installB);
        File.WriteAllText(Path.Combine(installA, "amz-cli"), "a");
        File.WriteAllText(Path.Combine(installB, "amz-cli"), "b");

        var backupA = await InstallBackup.CreateAsync(
            installA,
            "1.0.0",
            "linux-x64",
            backups);
        var backupB = await InstallBackup.CreateAsync(
            installB,
            "2.0.0",
            "linux-x64",
            backups);

        var listA = InstallBackup.List(backups, installA);
        Assert.Single(listA);
        Assert.Equal(backupA.ArchivePath, listA[0].ArchivePath);
        Assert.Equal(installA, listA[0].InstallDir);

        InstallBackup.Prune(backups, 1, installB);

        Assert.True(File.Exists(backupA.ArchivePath));
        Assert.True(File.Exists(backupB.ArchivePath));
    }

    [Fact]
    public async Task Deletes_only_backups_of_the_requested_installation()
    {
        using var temp = new TempDirectory();
        var installA = Path.Combine(temp.Path, "install-a");
        var installB = Path.Combine(temp.Path, "install-b");
        var backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(installA);
        Directory.CreateDirectory(installB);
        File.WriteAllText(Path.Combine(installA, "amz-cli"), "a");
        File.WriteAllText(Path.Combine(installB, "amz-cli"), "b");

        var backupA = await InstallBackup.CreateAsync(
            installA,
            "1.0.0",
            "linux-x64",
            backups);
        var backupB = await InstallBackup.CreateAsync(
            installB,
            "2.0.0",
            "linux-x64",
            backups);

        var result = InstallBackup.DeleteAll(backups, installA);

        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Failed);
        Assert.True(result.Bytes > 0);
        Assert.False(File.Exists(backupA.ArchivePath));
        Assert.False(File.Exists(backupA.MetadataPath));
        Assert.True(File.Exists(backupB.ArchivePath));
        Assert.True(File.Exists(backupB.MetadataPath));
        Assert.Single(InstallBackup.List(backups, installB));
    }

    [Fact]
    public async Task Detects_a_corrupted_backup()
    {
        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "amz-cli"), "binary");

        var result = await InstallBackup.CreateAsync(
            install,
            "1.0.0",
            "linux-x64",
            backups);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(
            File.ReadAllText(result.MetadataPath),
            ReleaseJson.Options)!;

        var bytes = await File.ReadAllBytesAsync(result.ArchivePath);
        await File.WriteAllBytesAsync(
            result.ArchivePath,
            bytes[..(bytes.Length / 2)]);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            InstallBackup.VerifyAsync(result.ArchivePath, metadata));
    }
}
