using System.Net;
using System.Text;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class ReleaseDownloaderTests
{
    [Fact]
    public async Task Downloads_and_decompresses_a_signed_file()
    {
        using var temp = new TempDirectory();
        var (manifestJson, _, _) = TestRelease.Create(content: "hello update");
        var manifest = ReleaseManifest.Parse(manifestJson);
        var file = manifest.Files[0];
        var gzip = TestRelease.Gzip(Encoding.UTF8.GetBytes("hello update"));
        using var downloader = new ReleaseDownloader(
            new HttpClient(new StubHandler(gzip)));

        var result = await downloader.DownloadAsync(
            Guid.NewGuid(),
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [file.DownloadPath] = $"https://github.com/Duster98/amz-cli/releases/download/v2.0.0/{file.Path}.gz",
            },
            Path.Combine(temp.Path, "staging"),
            Path.Combine(temp.Path, "downloads"));

        var single = Assert.Single(result.Files);
        Assert.Equal("amz-cli", single.RelativePath);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(
            "hello update",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "staging", "amz-cli")));
    }


    [Fact]
    public async Task Downloads_and_extracts_a_schema_2_package()
    {
        using var temp = new TempDirectory();
        var (manifestJson, _, _, package) = TestRelease.CreatePackage(content: "package payload");
        var manifest = ReleaseManifest.Parse(manifestJson);
        using var downloader = new ReleaseDownloader(
            new HttpClient(new StubHandler(package)));

        var result = await downloader.DownloadAsync(
            Guid.NewGuid(),
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [manifest.Package!.DownloadPath] = "https://github.com/Duster98/amz-cli/releases/download/v2.0.0/amz-cli-2.0.0-linux-x64.zip",
            },
            Path.Combine(temp.Path, "staging"),
            Path.Combine(temp.Path, "downloads"));

        var single = Assert.Single(result.Files);
        Assert.Equal("amz-cli", single.RelativePath);
        Assert.Equal(
            "package payload",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "staging", "amz-cli")));
    }

    [Fact]
    public async Task Rejects_a_package_with_a_path_traversal_entry()
    {
        using var temp = new TempDirectory();
        var (manifestJson, _, _, package) = TestRelease.CreatePackage();
        using (var stream = new MemoryStream())
        {
            stream.Write(package);
        }
        // Repackage with an extra unsafe entry while keeping the signed manifest.
        using var packageStream = new MemoryStream();
        using (var source = new System.IO.Compression.ZipArchive(
                   new MemoryStream(package),
                   System.IO.Compression.ZipArchiveMode.Read))
        using (var target = new System.IO.Compression.ZipArchive(
                   packageStream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                input.CopyTo(output);
            }
            var unsafeEntry = target.CreateEntry("../evil.txt");
            using var unsafeStream = unsafeEntry.Open();
            unsafeStream.WriteByte(65);
        }

        var manifest = ReleaseManifest.Parse(manifestJson);
        using var downloader = new ReleaseDownloader(
            new HttpClient(new StubHandler(packageStream.ToArray())));

        await Assert.ThrowsAsync<UpdateException>(() =>
            downloader.DownloadAsync(
                Guid.NewGuid(),
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [manifest.Package!.DownloadPath] = "https://github.com/Duster98/amz-cli/releases/download/v2.0.0/amz-cli-2.0.0-linux-x64.zip",
                },
                Path.Combine(temp.Path, "staging"),
                Path.Combine(temp.Path, "downloads")));
    }

    [Fact]
    public async Task Skips_a_staging_file_that_already_matches()
    {
        using var temp = new TempDirectory();
        var (manifestJson, _, _) = TestRelease.Create(content: "already here");
        var manifest = ReleaseManifest.Parse(manifestJson);
        var file = manifest.Files[0];
        var staging = Path.Combine(temp.Path, "staging");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "amz-cli"),
            "already here");

        using var downloader = new ReleaseDownloader(
            new HttpClient(new ThrowingHandler()));

        var result = await downloader.DownloadAsync(
            Guid.NewGuid(),
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal),
            staging,
            Path.Combine(temp.Path, "downloads"));

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.BytesDownloaded);
    }

    [Fact]
    public async Task Rejects_a_payload_with_a_wrong_hash()
    {
        using var temp = new TempDirectory();
        var (manifestJson, _, _) = TestRelease.Create(content: "expected");
        var manifest = ReleaseManifest.Parse(manifestJson);
        var file = manifest.Files[0];
        var gzip = TestRelease.Gzip(Encoding.UTF8.GetBytes("different"));
        using var downloader = new ReleaseDownloader(
            new HttpClient(new StubHandler(gzip)));

        await Assert.ThrowsAsync<UpdateException>(() =>
            downloader.DownloadAsync(
                Guid.NewGuid(),
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [file.DownloadPath] = $"https://github.com/Duster98/amz-cli/releases/download/v2.0.0/{file.Path}.gz",
                },
                Path.Combine(temp.Path, "staging"),
                Path.Combine(temp.Path, "downloads")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StubHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
                RequestMessage = request,
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected HTTP call.");
    }
}
