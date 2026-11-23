using AmazonScraper.Cli;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Tests;

public class VersionReportTests
{
    [Fact]
    public void Reads_version_channel_and_release_date_from_install_json()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(directory.Root, "install.json"),
            """
            {
              "schema": 1,
              "product": "amz-cli",
              "version": "2.34.0",
              "platform": "linux-x64",
              "channel": "stable",
              "install_dir": "/tmp/amz-cli",
              "installed_at": "2026-11-01T10:00:00Z",
              "updated_at": "2026-11-20T12:00:00Z",
              "release_published_at": "2026-11-20T11:30:00Z"
            }
            """);

        var report = VersionReport.Read(directory.Root);

        Assert.Equal("2.34.0", report.Version);
        Assert.Equal("stable", report.Channel);
        Assert.Equal("2026-11-20", report.ReleasedDate);
        Assert.Equal("amz-cli 2.34.0 (stable, 2026-11-20)", report.Display);
        Assert.Equal("amz-cli 2.34.0", report.ShortDisplay);
        Assert.Equal("install.json", report.Source);
    }

    [Fact]
    public void Falls_back_to_the_signed_update_manifest()
    {
        using var directory = new TempDirectory();
        var manifest = """
            {
              "schema": 1,
              "version": "2.34.0",
              "platform": "linux-x64",
              "channel": "beta",
              "published_at": "2026-11-20T11:30:00Z",
              "files": [
                {
                  "path": "amz-cli",
                  "mode": "0755",
                  "size": 1,
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "download_path": "beta/linux-x64/2.34.0/amazon.gz",
                  "download_size": 1,
                  "download_sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "compression": "gzip"
                }
              ]
            }
            """;
        InstallStateStore.Write(
            directory.Root,
            "2.34.0",
            "linux-x64",
            manifest,
            "dGVzdA==");

        var report = VersionReport.Read(directory.Root);

        Assert.Equal("2.34.0", report.Version);
        Assert.Equal("beta", report.Channel);
        Assert.Equal("2026-11-20", report.ReleasedDate);
        Assert.Equal(".amz-cli-install.json", report.Source);
    }

    [Fact]
    public void Falls_back_to_the_current_binary_for_a_source_run()
    {
        using var directory = new TempDirectory();

        var report = VersionReport.Read(directory.Root);

        Assert.Equal(VersionInfo.Informational, report.Version);
        Assert.Equal("dev", report.Channel);
        Assert.Null(report.ReleasedDate);
        Assert.Equal("amz-cli " + VersionInfo.Informational + " (dev)", report.Display);
        Assert.Equal("build", report.Source);
    }
}
