using AmazonScraper.Installer;
using AmazonScraper.Update;

namespace AmazonScraper.Installer.Tests;

public class InstallServiceTests
{
    [Fact]
    public async Task Install_from_staging_creates_files_and_install_manifest()
    {
        using var fixture = new InstallFixture();
        var source = fixture.WriteSource("amz-cli", "#!/bin/sh\nexit 0\n");
        var staged = new StagedFile
        {
            RelativePath = "amz-cli",
            SourcePath = source,
            Mode = "0755",
            Size = new FileInfo(source).Length,
            Sha256 = await FileHash.Sha256Async(source, default),
        };
        var manifest = Manifest("2.29.2", "linux-x64", "amz-cli");

        var result = await fixture.Service.InstallFromStagingAsync(
            manifest,
            [staged],
            fixture.Target,
            fixture.Options,
            verify: false,
            stagingDirectory: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(fixture.Target, "amz-cli")));
        var installed = InstallManifest.TryRead(fixture.Target);
        Assert.NotNull(installed);
        Assert.Equal("2.29.2", installed.Version);
        Assert.Equal(["amz-cli"], installed.Files);
    }

    [Fact]
    public async Task Install_preserves_unknown_files_in_target()
    {
        using var fixture = new InstallFixture();
        var unknown = Path.Combine(fixture.Target, "keep-me.txt");
        Directory.CreateDirectory(fixture.Target);
        File.WriteAllText(unknown, "keep");
        var source = fixture.WriteSource("amz-cli", "binary");
        var staged = new StagedFile
        {
            RelativePath = "amz-cli",
            SourcePath = source,
            Mode = "0755",
            Size = new FileInfo(source).Length,
            Sha256 = await FileHash.Sha256Async(source, default),
        };

        await fixture.Service.InstallFromStagingAsync(
            Manifest("2.29.2", "linux-x64", "amz-cli"),
            [staged],
            fixture.Target,
            fixture.Options,
            verify: false,
            stagingDirectory: null,
            CancellationToken.None);

        Assert.Equal("keep", File.ReadAllText(unknown));
    }

    [Fact]
    public async Task Uninstall_with_dir_removes_installed_files()
    {
        using var fixture = new InstallFixture();
        var source = fixture.WriteSource("amz-cli", "binary");
        var staged = new StagedFile
        {
            RelativePath = "amz-cli",
            SourcePath = source,
            Mode = "0755",
            Size = new FileInfo(source).Length,
            Sha256 = await FileHash.Sha256Async(source, default),
        };
        await fixture.Service.InstallFromStagingAsync(
            Manifest("2.29.2", "linux-x64", "amz-cli"),
            [staged],
            fixture.Target,
            fixture.Options,
            verify: false,
            stagingDirectory: null,
            CancellationToken.None);
        var unknown = Path.Combine(fixture.Target, "keep-me.txt");
        File.WriteAllText(unknown, "keep");

        var options = InstallerOptions.Parse(
            ["--uninstall", "--dir", fixture.Target, "--no-path", "--yes"]);
        var result = await fixture.Service.UninstallAsync(
            options,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(fixture.Target, "amz-cli")));
        Assert.False(File.Exists(Path.Combine(fixture.Target, "install.json")));
        Assert.True(File.Exists(unknown));
    }

    [Fact]
    public void Install_manifest_round_trips()
    {
        using var fixture = new InstallFixture();
        var manifest = InstallManifest.Create(
            fixture.Target,
            "2.29.2",
            "linux-x64",
            "stable",
            ["amz-cli", "amz-notify.exe"]);
        manifest.Write(fixture.Target);

        var loaded = InstallManifest.TryRead(fixture.Target);

        Assert.NotNull(loaded);
        Assert.Equal("2.29.2", loaded.Version);
        Assert.Equal("linux-x64", loaded.Platform);
        Assert.Equal(["amz-cli", "amz-notify.exe"], loaded.Files);
    }

    [Fact]
    public void Options_parse_flags_and_reject_invalid_values()
    {
        var options = InstallerOptions.Parse(
            ["--dir", "/tmp/amz-cli", "--channel", "beta", "--no-path", "--yes"]);

        Assert.Equal("/tmp/amz-cli", options.Directory);
        Assert.Equal("beta", options.Channel);
        Assert.True(options.NoPath);
        Assert.True(options.Yes);

        Assert.Throws<ArgumentException>(() =>
            InstallerOptions.Parse(["--channel", "nightly"]));
        Assert.Throws<ArgumentException>(() =>
            InstallerOptions.Parse(["--purge"]));
    }

    private static ReleaseManifest Manifest(
        string version,
        string platform,
        params string[] files) =>
        new()
        {
            Schema = 1,
            Version = version,
            Platform = platform,
            Channel = "stable",
            PublishedAt = DateTimeOffset.UtcNow,
            Files = files.Select(path => new ReleaseFile
            {
                Path = path,
                Mode = "0755",
                Size = 1,
                Sha256 = new string('a', 64),
                DownloadPath = $"stable/{platform}/{version}/{path}.gz",
                DownloadSize = 1,
                DownloadSha256 = new string('b', 64),
            }).ToList(),
        };

    private sealed class InstallFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"amz-cli-installer-tests-{Guid.NewGuid():N}");

        public InstallFixture()
        {
            Directory.CreateDirectory(_root);
            Target = Path.Combine(_root, "install");
        }

        public string Target { get; }

        public InstallService Service { get; } = new();

        public InstallerOptions Options { get; } =
            InstallerOptions.Parse(["--no-path", "--yes"]);

        public string WriteSource(string name, string content)
        {
            var sourceDir = Path.Combine(_root, "source");
            Directory.CreateDirectory(sourceDir);
            var path = Path.Combine(sourceDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
