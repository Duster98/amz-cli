using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace AmazonScraper.Update;

public sealed record UpdateProgress(
    string Stage,
    string Message,
    long Bytes = 0,
    long TotalBytes = 0)
{
    public int? Percent => TotalBytes > 0
        ? (int)Math.Clamp(Bytes * 100 / TotalBytes, 0, 100)
        : null;
}

public sealed record DownloadResult(
    IReadOnlyList<StagedFile> Files,
    long BytesDownloaded,
    int Skipped);

public sealed class ReleaseDownloader : IDisposable
{
    private const long MaxExtractedBytes = 512L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ReleaseDownloader(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<DownloadResult> DownloadAsync(
        Guid releaseId,
        ReleaseManifest manifest,
        IReadOnlyDictionary<string, string> urls,
        string stagingDirectory,
        string downloadsDirectory,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(downloadsDirectory);

        if (manifest.IsPackageManifest)
        {
            return await DownloadPackageAsync(
                    manifest,
                    urls,
                    stagingDirectory,
                    downloadsDirectory,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var staged = new List<StagedFile>();
        long bytesDownloaded = 0;
        var skipped = 0;
        var total = manifest.Files.Sum(file => file.DownloadSize);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PathSafety.CombineSafe(stagingDirectory, file.Path);
            var targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (File.Exists(target)
                && new FileInfo(target).Length == file.Size
                && string.Equals(
                    await FileHash.Sha256Async(target, cancellationToken).ConfigureAwait(false),
                    file.Sha256,
                    StringComparison.Ordinal))
            {
                skipped++;
                staged.Add(new StagedFile
                {
                    RelativePath = file.Path,
                    SourcePath = target,
                    Mode = file.Mode,
                    Size = file.Size,
                    Sha256 = file.Sha256,
                });
                continue;
            }

            if (!urls.TryGetValue(file.DownloadPath, out var url))
            {
                throw new UpdateException(
                    $"Missing download URL for '{file.Path}'.");
            }

            progress?.Report(new UpdateProgress(
                "download",
                $"Downloading {file.Path}",
                bytesDownloaded,
                total));

            var temporary = Path.Combine(
                downloadsDirectory,
                Guid.NewGuid().ToString("N") + ".part");
            var outputPart = target + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                var downloaded = await DownloadVerifiedAsync(
                        url,
                        file.DownloadSize,
                        file.DownloadSha256,
                        file.Path,
                        temporary,
                        cancellationToken,
                        progress,
                        bytesDownloaded,
                        total)
                    .ConfigureAwait(false);
                bytesDownloaded += downloaded;

                await ExtractOneAsync(
                        temporary,
                        outputPart,
                        file,
                        cancellationToken)
                    .ConfigureAwait(false);

                File.Move(outputPart, target, overwrite: true);
                TrySetMode(target, file.Mode);
                staged.Add(new StagedFile
                {
                    RelativePath = file.Path,
                    SourcePath = target,
                    Mode = file.Mode,
                    Size = file.Size,
                    Sha256 = file.Sha256,
                });
            }
            finally
            {
                TryDelete(temporary);
                TryDelete(outputPart);
            }
        }

        progress?.Report(new UpdateProgress(
            "download",
            "Download completed",
            bytesDownloaded,
            total));

        return new DownloadResult(staged, bytesDownloaded, skipped);
    }

    private async Task<DownloadResult> DownloadPackageAsync(
        ReleaseManifest manifest,
        IReadOnlyDictionary<string, string> urls,
        string stagingDirectory,
        string downloadsDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var package = manifest.Package
            ?? throw new UpdateException("Manifest is missing the package section.");
        if (!urls.TryGetValue(package.DownloadPath, out var url))
        {
            throw new UpdateException(
                $"Missing download URL for package '{package.Name}'.");
        }

        var temporary = Path.Combine(
            downloadsDirectory,
            Guid.NewGuid().ToString("N") + ".package.part");
        var extractionDirectory = Path.Combine(
            downloadsDirectory,
            Guid.NewGuid().ToString("N") + ".extract");
        try
        {
            progress?.Report(new UpdateProgress(
                "download",
                $"Downloading {package.Name}",
                0,
                package.Size));
            var downloaded = await DownloadVerifiedAsync(
                    url,
                    package.Size,
                    package.Sha256,
                    package.Name,
                    temporary,
                    cancellationToken,
                    progress,
                    0,
                    package.Size)
                .ConfigureAwait(false);

            progress?.Report(new UpdateProgress(
                "extract",
                $"Extracting {package.Name}",
                downloaded,
                package.Size));
            Directory.CreateDirectory(extractionDirectory);
            var staged = await ExtractPackageAsync(
                    temporary,
                    extractionDirectory,
                    stagingDirectory,
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new UpdateProgress(
                "download",
                "Download completed",
                downloaded,
                package.Size));
            return new DownloadResult(staged, downloaded, 0);
        }
        finally
        {
            TryDelete(temporary);
            TryDeleteDirectory(extractionDirectory);
        }
    }

    private static async Task<IReadOnlyList<StagedFile>> ExtractPackageAsync(
        string packagePath,
        string extractionDirectory,
        string stagingDirectory,
        ReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }
            if (!PathSafety.IsSafeRelativePath(name)
                || name.StartsWith('.')
                || name.Contains("/.", StringComparison.Ordinal))
            {
                throw new UpdateException($"Unsafe file in package: '{name}'.");
            }
            entries[name] = entry;
        }

        var staged = new List<StagedFile>();
        long total = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(file.Path, out var entry))
            {
                throw new UpdateException(
                    $"Package is missing the file '{file.Path}'.");
            }

            var extracted = PathSafety.CombineSafe(extractionDirectory, file.Path);
            var extractedDirectory = Path.GetDirectoryName(extracted);
            if (!string.IsNullOrEmpty(extractedDirectory))
            {
                Directory.CreateDirectory(extractedDirectory);
            }

            await using (var input = entry.Open())
            await using (var output = new FileStream(
                extracted,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long fileBytes = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    fileBytes += read;
                    total += read;
                    if (fileBytes > file.Size || total > MaxExtractedBytes)
                    {
                        throw new UpdateException(
                            $"Package file '{file.Path}' exceeds its signed size.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (fileBytes != file.Size)
                {
                    throw new UpdateException(
                        $"Wrong size for package file '{file.Path}': {fileBytes} instead of {file.Size}.");
                }
                var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (!string.Equals(digest, file.Sha256, StringComparison.Ordinal))
                {
                    throw new UpdateException(
                        $"Invalid hash for package file '{file.Path}'.");
                }
            }

            var target = PathSafety.CombineSafe(stagingDirectory, file.Path);
            var targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            File.Move(extracted, target, overwrite: true);
            TrySetMode(target, file.Mode);
            staged.Add(new StagedFile
            {
                RelativePath = file.Path,
                SourcePath = target,
                Mode = file.Mode,
                Size = file.Size,
                Sha256 = file.Sha256,
            });
        }

        return staged;
    }

    private async Task<long> DownloadVerifiedAsync(
        string url,
        long expectedSize,
        string expectedSha256,
        string label,
        string destination,
        CancellationToken cancellationToken,
        IProgress<UpdateProgress>? progress = null,
        long progressBase = 0,
        long progressTotal = 0)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !UpdateEndpoints.IsAllowedHost(uri.Host))
        {
            throw new UpdateException(
                $"Download URL is not allowed for '{label}'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null
            || finalUri.Scheme != Uri.UriSchemeHttps
            || !UpdateEndpoints.IsAllowedHost(finalUri.Host))
        {
            throw new UpdateException(
                $"Download redirect is not allowed for '{label}'.");
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateException(
                $"Download of '{label}' failed: HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength is { } length
            && length != expectedSize)
        {
            throw new UpdateException(
                $"Invalid declared size for '{label}'.");
        }

        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > expectedSize)
            {
                throw new UpdateException(
                    $"Download of '{label}' exceeds its signed size.");
            }
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            if (progress is not null
                && (total & 0x3FFFF) < read)
            {
                progress.Report(new UpdateProgress(
                    "download",
                    $"Downloading {label}",
                    progressBase + total,
                    progressTotal));
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (total != expectedSize)
        {
            throw new UpdateException(
                $"Wrong download size for '{label}': {total} instead of {expectedSize}.");
        }
        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(digest, expectedSha256, StringComparison.Ordinal))
        {
            throw new UpdateException(
                $"Invalid download hash for '{label}'.");
        }

        return total;
    }

    private static async Task ExtractOneAsync(
        string compressedPath,
        string outputPath,
        ReleaseFile file,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            compressedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        Stream payload = input;
        GZipStream? gzip = null;
        if (file.IsGzip)
        {
            gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            payload = gzip;
        }

        try
        {
            await using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = await payload.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > file.Size)
                {
                    throw new UpdateException(
                        $"Extracted file '{file.Path}' exceeds its signed size.");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (total != file.Size)
            {
                throw new UpdateException(
                    $"Wrong extracted size for '{file.Path}': {total} instead of {file.Size}.");
            }
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(digest, file.Sha256, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Invalid extracted file hash for '{file.Path}'.");
            }
        }
        finally
        {
            if (gzip is not null)
            {
                await gzip.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static void TrySetMode(string path, string mode)
    {
        if (OperatingSystem.IsWindows() || mode != "0755")
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception)
        {
            // The installer sets the mode again.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
