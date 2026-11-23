using System.Text;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public async Task Applies_a_staged_executable_and_marks_the_update_complete()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var staging = Path.Combine(temp.Path, "staging");
        var journalPath = Path.Combine(temp.Path, "journal.json");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);

        var oldPath = Path.Combine(install, "amz-cli");
        File.WriteAllText(oldPath, "#!/bin/sh\necho old\n");
        File.SetUnixFileMode(
            oldPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var installJson = Path.Combine(install, "install.json");
        File.WriteAllText(
            installJson,
            """
            {
              "schema": 1,
              "product": "amz-cli",
              "version": "1.0.0",
              "platform": "linux-x64",
              "channel": "stable",
              "install_dir": "/tmp/install",
              "installed_at": "2026-01-01T00:00:00Z",
              "installer_version": "1.0.0",
              "files": ["amz-cli"],
              "custom": "keep"
            }
            """);

        const string newContent = "#!/bin/sh\nexit 0\n";
        var newPath = Path.Combine(staging, "amz-cli");
        await File.WriteAllTextAsync(newPath, newContent);
        File.SetUnixFileMode(
            newPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var plan = BuildPlan(temp, install, staging, newPath, newContent);
        var journal = new UpdateJournal
        {
            Phase = UpdatePhase.Applying,
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Platform = "linux-x64",
            Channel = "stable",
            InstallDir = install,
        };

        var verify = await UpdateInstaller.ApplyAsync(
            plan,
            journal,
            journalPath: journalPath);

        Assert.True(verify.Success);
        Assert.Equal(newContent, await File.ReadAllTextAsync(oldPath));
        Assert.True(File.Exists(Path.Combine(install, InstallState.FileName)));

        using var manifest = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(installJson));
        Assert.Equal(
            "2.0.0",
            manifest.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            "1.0.0",
            manifest.RootElement.GetProperty("installer_version").GetString());
        Assert.Equal(
            "2026-01-01T00:00:00Z",
            manifest.RootElement.GetProperty("installed_at").GetString());
        Assert.Equal(
            "keep",
            manifest.RootElement.GetProperty("custom").GetString());
        Assert.Equal(
            "amz-cli",
            manifest.RootElement.GetProperty("files")[0].GetString());
        Assert.True(
            manifest.RootElement.TryGetProperty("updated_at", out _));
        var saved = UpdateJournalStore.Read(journalPath);
        Assert.NotNull(saved);
        Assert.Equal(UpdatePhase.Completed, saved.Phase);
    }

    [Fact]
    public async Task Rolls_back_when_the_new_executable_fails_its_test()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var temp = new TempDirectory();
        var install = Path.Combine(temp.Path, "install");
        var staging = Path.Combine(temp.Path, "staging");
        var journalPath = Path.Combine(temp.Path, "journal.json");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);

        const string oldContent = "#!/bin/sh\nexit 0\n";
        var oldPath = Path.Combine(install, "amz-cli");
        File.WriteAllText(oldPath, oldContent);
        File.SetUnixFileMode(
            oldPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        const string newContent = "#!/bin/sh\nexit 1\n";
        var newPath = Path.Combine(staging, "amz-cli");
        await File.WriteAllTextAsync(newPath, newContent);
        File.SetUnixFileMode(
            newPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var plan = BuildPlan(temp, install, staging, newPath, newContent);
        var journal = new UpdateJournal
        {
            Phase = UpdatePhase.Applying,
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Platform = "linux-x64",
            Channel = "stable",
            InstallDir = install,
        };

        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.ApplyAsync(plan, journal, journalPath: journalPath));

        Assert.Equal(oldContent, await File.ReadAllTextAsync(oldPath));
        var saved = UpdateJournalStore.Read(journalPath);
        Assert.NotNull(saved);
        Assert.Equal(UpdatePhase.RolledBack, saved.Phase);
    }

    private static UpdateApplyPlan BuildPlan(
        TempDirectory temp,
        string install,
        string staging,
        string source,
        string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new UpdateApplyPlan
        {
            InstallDir = install,
            Platform = "linux-x64",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Channel = "stable",
            Manifest = "{\"schema\":1}",
            Signature = "dGVzdA==",
            BackupPath = Path.Combine(temp.Path, "backup.tar.gz"),
            BackupSha256 = new string('a', 64),
            StagingDir = staging,
            RollbackDir = Path.Combine(temp.Path, "rollback"),
            Files =
            [
                new UpdateApplyFile
                {
                    Path = "amz-cli",
                    SourcePath = source,
                    Mode = "0755",
                    Size = bytes.Length,
                    Sha256 = FileHash.Sha256(bytes),
                },
            ],
        };
    }
}
