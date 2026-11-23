using System.Text.Json;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class InstallManifestUpdaterTests
{
    [Fact]
    public void SyncCurrent_updates_an_older_installer_record()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "install.json");
        File.WriteAllText(
            path,
            """
            {
              "schema": 1,
              "product": "amz-cli",
              "version": "2.30.0",
              "platform": "win-x64",
              "channel": "stable",
              "installed_at": "2026-11-01T00:00:00Z",
              "installer_version": "1.0.0",
              "files": ["amz-cli.exe", "amz-notify.exe"]
            }
            """);

        var changed = InstallManifestUpdater.SyncCurrent(
            temp.Path,
            "2.32.1",
            "win-x64");

        Assert.True(changed);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            "2.32.1",
            document.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            "win-x64",
            document.RootElement.GetProperty("platform").GetString());
        Assert.Equal(
            "1.0.0",
            document.RootElement.GetProperty("installer_version").GetString());
        Assert.Equal(
            "amz-cli.exe",
            document.RootElement.GetProperty("files")[0].GetString());

        Assert.False(InstallManifestUpdater.SyncCurrent(
            temp.Path,
            "2.32.1",
            "win-x64"));
    }

    [Fact]
    public void SyncCurrent_recovers_the_release_date_from_the_update_manifest()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "install.json"),
            """
            {
              "schema": 1,
              "product": "amz-cli",
              "version": "2.33.0",
              "platform": "linux-x64",
              "channel": "stable",
              "installed_at": "2026-11-01T00:00:00Z",
              "installer_version": "1.0.0",
              "files": ["amz-cli"]
            }
            """);
        InstallStateStore.Write(
            temp.Path,
            "2.33.0",
            "linux-x64",
            """
            {
              "schema": 1,
              "version": "2.33.0",
              "platform": "linux-x64",
              "channel": "stable",
              "published_at": "2026-11-20T11:30:00Z",
              "files": [
                {
                  "path": "amz-cli",
                  "mode": "0755",
                  "size": 1,
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "download_path": "stable/linux-x64/2.33.0/amazon.gz",
                  "download_size": 1,
                  "download_sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "compression": "gzip"
                }
              ]
            }
            """,
            "dGVzdA==");

        var changed = InstallManifestUpdater.SyncCurrent(
            temp.Path,
            "2.33.0",
            "linux-x64");

        Assert.True(changed);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(temp.Path, "install.json")));
        Assert.Equal(
            "2026-11-20T11:30:00.0000000+00:00",
            document.RootElement
                .GetProperty("release_published_at")
                .GetDateTimeOffset()
                .ToString("O"));
    }

    [Fact]
    public void SyncCurrent_without_install_json_is_a_no_op()
    {
        using var temp = new TempDirectory();

        Assert.False(InstallManifestUpdater.SyncCurrent(
            temp.Path,
            "2.32.1",
            "win-x64"));
        Assert.False(File.Exists(Path.Combine(temp.Path, "install.json")));
    }
}
