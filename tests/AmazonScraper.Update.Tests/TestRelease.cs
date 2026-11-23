using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

internal static class TestRelease
{
    public const string KeyId = "test-key";

    public static (string Manifest, string Signature, string PublicKeyPem) Create(
        string version = "2.0.0",
        string platform = "linux-x64",
        string channel = "stable",
        string? minSupported = "1.0.0",
        string? content = null)
    {
        content ??= "new-amz-cli-binary";
        var raw = Encoding.UTF8.GetBytes(content);
        var gzip = Gzip(raw);
        var executable = platform.StartsWith("win-", StringComparison.Ordinal)
            ? "amz-cli.exe"
            : "amz-cli";
        var manifest = new ReleaseManifest
        {
            Schema = 1,
            Version = version,
            Platform = platform,
            Channel = channel,
            MinSupportedVersion = minSupported,
            PublishedAt = DateTimeOffset.UtcNow,
            Notes = "test release",
            Files =
            [
                new ReleaseFile
                {
                    Path = executable,
                    Mode = "0755",
                    Size = raw.Length,
                    Sha256 = FileHash.Sha256(raw),
                    DownloadPath = $"{channel}/{platform}/{version}/{executable}.gz",
                    DownloadSize = gzip.Length,
                    DownloadSha256 = FileHash.Sha256(gzip),
                    Compression = "gzip",
                },
            ],
        };

        var json = JsonSerializer.Serialize(manifest, ReleaseJson.Options);
        using var rsa = RSA.Create(2048);
        var signature = Convert.ToBase64String(
            rsa.SignData(
                Encoding.UTF8.GetBytes(json),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        return (json, signature, publicKey);
    }


    public static (string Manifest, string Signature, string PublicKey, byte[] Package) CreatePackage(
        string version = "2.0.0",
        string platform = "linux-x64",
        string channel = "stable",
        string? minSupported = "1.0.0",
        string? content = null)
    {
        content ??= "new-amz-cli-binary";
        var raw = Encoding.UTF8.GetBytes(content);
        var executable = platform.StartsWith("win-", StringComparison.Ordinal)
            ? "amz-cli.exe"
            : "amz-cli";
        var packageName = $"amz-cli-{version}-{platform}.zip";
        using var packageStream = new MemoryStream();
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(executable, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(raw);
        }
        var package = packageStream.ToArray();
        var manifest = new ReleaseManifest
        {
            Schema = 2,
            Version = version,
            Platform = platform,
            Channel = channel,
            MinSupportedVersion = minSupported,
            PublishedAt = DateTimeOffset.UtcNow,
            Notes = "test release",
            Files =
            [
                new ReleaseFile
                {
                    Path = executable,
                    Mode = "0755",
                    Size = raw.Length,
                    Sha256 = FileHash.Sha256(raw),
                    Compression = "none",
                },
            ],
            Package = new ReleasePackage
            {
                Name = packageName,
                Size = package.Length,
                Sha256 = FileHash.Sha256(package),
                DownloadPath = $"{channel}/{platform}/{version}/{packageName}",
            },
        };

        var json = JsonSerializer.Serialize(manifest, ReleaseJson.Options);
        using var rsa = RSA.Create(2048);
        var signature = Convert.ToBase64String(
            rsa.SignData(
                Encoding.UTF8.GetBytes(json),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        return (json, signature, publicKey, package);
    }

    public static ReleaseEnvelope Envelope(
        string manifest,
        string signature,
        string version = "2.0.0",
        string platform = "linux-x64",
        string channel = "stable") =>
        new(
            Guid.NewGuid(),
            version,
            platform,
            channel,
            manifest,
            signature,
            KeyId,
            DateTimeOffset.UtcNow);

    public static IReadOnlyDictionary<string, string> Keys(string publicKey) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyId] = publicKey,
        };

    public static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(raw);
        }
        return output.ToArray();
    }
}
