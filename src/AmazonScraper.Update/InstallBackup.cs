using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public sealed class BackupFileInfo
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "0644";
}

public sealed class BackupMetadata
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("install_dir")]
    public string InstallDir { get; set; } = string.Empty;

    [JsonPropertyName("archive_path")]
    public string ArchivePath { get; set; } = string.Empty;

    [JsonPropertyName("archive_sha256")]
    public string ArchiveSha256 { get; set; } = string.Empty;

    [JsonPropertyName("archive_size")]
    public long ArchiveSize { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("files")]
    public List<BackupFileInfo> Files { get; set; } = [];
}

public sealed record BackupResult(
    string ArchivePath,
    string MetadataPath,
    string Sha256,
    long Size,
    string Version,
    string Platform,
    DateTimeOffset CreatedAt,
    string InstallDir);

public sealed record BackupSummary(
    string ArchivePath,
    string MetadataPath,
    string Version,
    string Platform,
    DateTimeOffset CreatedAt,
    long Size,
    string Sha256,
    string InstallDir);

public sealed record BackupDeleteResult(int Removed, long Bytes, int Failed);

public static class InstallBackup
{
    private static readonly string[] ExcludedRoots =
    [
        "data",
        "config",
        "updates",
        "notifications",
    ];

    public static async Task<BackupResult> CreateAsync(
        string installDir,
        string version,
        string platform,
        string? backupDirectory = null,
        CancellationToken cancellationToken = default)
    {
        backupDirectory ??= UpdatePaths.BackupsDirectory();
        Directory.CreateDirectory(backupDirectory);

        var createdAt = DateTimeOffset.UtcNow;
        var stem = $"amz-cli-{version}-{platform}-{createdAt:yyyyMMdd-HHmmss}";
        var archivePath = Path.Combine(backupDirectory, stem + ".tar.gz");
        if (File.Exists(archivePath))
        {
            archivePath = Path.Combine(
                backupDirectory,
                stem + "-" + Guid.NewGuid().ToString("N")[..8] + ".tar.gz");
        }
        var metadataPath = archivePath + ".json";

        var files = EnumerateFiles(installDir)
            .Select(path =>
            {
                var relative = ToRelative(installDir, path);
                var info = new FileInfo(path);
                return new BackupFileInfo
                {
                    Path = relative,
                    Size = info.Length,
                    Sha256 = FileHash.Sha256Async(path, cancellationToken)
                        .GetAwaiter()
                        .GetResult(),
                    Mode = IsExecutable(relative) ? "0755" : "0644",
                };
            })
            .ToList();

        if (files.Count == 0)
        {
            throw new UpdateException("No files to include in the backup.");
        }

        try
        {
            await using (var archive = new FileStream(
                             archivePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 128 * 1024,
                             useAsync: true))
            {
                await using var gzip = new GZipStream(
                    archive,
                    CompressionLevel.Fastest,
                    leaveOpen: true);
                await using var writer = new TarWriter(
                    gzip,
                    TarEntryFormat.Pax,
                    leaveOpen: true);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath = PathSafety.CombineSafe(installDir, file.Path);
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, file.Path)
                    {
                        Mode = ParseMode(file.Mode),
                        ModificationTime = File.GetLastWriteTimeUtc(fullPath),
                    };
                    await using var data = new FileStream(
                        fullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 128 * 1024,
                        useAsync: true);
                    entry.DataStream = data;
                    await writer.WriteEntryAsync(entry, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var archiveInfo = new FileInfo(archivePath);
            var archiveHash = await FileHash
                .Sha256Async(archivePath, cancellationToken)
                .ConfigureAwait(false);
            var metadata = new BackupMetadata
            {
                Version = version,
                Platform = platform,
                InstallDir = installDir,
                ArchivePath = archivePath,
                ArchiveSha256 = archiveHash,
                ArchiveSize = archiveInfo.Length,
                CreatedAt = createdAt,
                Files = files,
            };
            WriteMetadata(metadataPath, metadata);

            await VerifyAsync(
                    archivePath,
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);

            return new BackupResult(
                archivePath,
                metadataPath,
                archiveHash,
                archiveInfo.Length,
                version,
                platform,
                createdAt,
                installDir);
        }
        catch (Exception ex)
        {
            TryDelete(archivePath);
            TryDelete(metadataPath);
            if (ex is UpdateException)
            {
                throw;
            }
            throw new UpdateException($"Creazione del backup non riuscita: {ex.Message}", ex);
        }
    }

    public static async Task VerifyAsync(
        string archivePath,
        BackupMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists)
        {
            throw new UpdateException($"Backup not found: {archivePath}");
        }
        if (metadata.ArchiveSize > 0 && archiveInfo.Length != metadata.ArchiveSize)
        {
            throw new UpdateException("Invalid backup size.");
        }
        if (!string.IsNullOrWhiteSpace(metadata.ArchiveSha256))
        {
            var actual = await FileHash
                .Sha256Async(archivePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    actual,
                    metadata.ArchiveSha256,
                    StringComparison.Ordinal))
            {
                throw new UpdateException("Invalid backup hash.");
            }
        }

        var expected = metadata.Files.ToDictionary(
            file => file.Path,
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var executable = PlatformInfo.ExecutableName(metadata.Platform);
        var executableSeen = false;

        await using var archive = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(
                   copyData: false,
                   cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType != TarEntryType.RegularFile)
            {
                throw new UpdateException(
                    $"The backup contains an irregular entry: '{entry.Name}'.");
            }
            if (!PathSafety.IsSafeRelativePath(entry.Name))
            {
                throw new UpdateException(
                    $"The backup contains an unsafe path: '{entry.Name}'.");
            }
            if (!expected.TryGetValue(entry.Name, out var expectedFile))
            {
                throw new UpdateException(
                    $"Il backup contiene un file non previsto: '{entry.Name}'.");
            }
            if (!seen.Add(entry.Name))
            {
                throw new UpdateException(
                    $"Il backup contiene un file duplicato: '{entry.Name}'.");
            }
            if (entry.DataStream is null)
            {
                throw new UpdateException(
                    $"Il backup non contiene i dati di '{entry.Name}'.");
            }

            await using var buffered = entry.DataStream;
            var hash = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(buffered, cancellationToken)
                    .ConfigureAwait(false));
            if (!string.Equals(hash, expectedFile.Sha256, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Invalid backup hash for '{entry.Name}'.");
            }
            if (string.Equals(entry.Name, executable, StringComparison.Ordinal))
            {
                executableSeen = true;
            }
        }

        if (!executableSeen)
        {
            throw new UpdateException(
                $"Il backup non contiene l'eseguibile '{executable}'.");
        }
        if (seen.Count != expected.Count)
        {
            throw new UpdateException("Il backup non contiene tutti i file previsti.");
        }
    }

    public static void Prune(string? backupDirectory, int keep, string? installDir = null)
    {
        if (keep < 1)
        {
            keep = 1;
        }
        backupDirectory ??= UpdatePaths.BackupsDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        var archives = Directory
            .EnumerateFiles(backupDirectory, "amz-cli-*.tar.gz")
            .Select(path => new
            {
                Path = path,
                Metadata = TryReadMetadata(path + ".json"),
                Modified = File.GetLastWriteTimeUtc(path),
            })
            .Where(item => installDir is null
                || string.Equals(
                    item.Metadata?.InstallDir,
                    installDir,
                    StringComparison.Ordinal))
            .OrderByDescending(item => item.Modified)
            .ToList();
        foreach (var archive in archives.Skip(keep))
        {
            TryDelete(archive.Path);
            TryDelete(archive.Path + ".json");
        }
    }

    public static BackupDeleteResult DeleteAll(
        string? backupDirectory = null,
        string? installDir = null)
    {
        backupDirectory ??= UpdatePaths.BackupsDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            return new BackupDeleteResult(0, 0, 0);
        }

        var removed = 0;
        var failed = 0;
        long bytes = 0;
        foreach (var archive in Directory
                     .EnumerateFiles(backupDirectory, "amz-cli-*.tar.gz")
                     .ToList())
        {
            var metadataPath = archive + ".json";
            var metadata = TryReadMetadata(metadataPath);
            if (installDir is not null
                && (metadata is null
                    || !string.Equals(
                        metadata.InstallDir,
                        installDir,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                var size = new FileInfo(archive).Length;
                File.Delete(archive);
                TryDelete(metadataPath);
                removed++;
                bytes += size;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return new BackupDeleteResult(removed, bytes, failed);
    }

    public static IReadOnlyList<BackupSummary> List(
        string? backupDirectory = null,
        string? installDir = null)
    {
        backupDirectory ??= UpdatePaths.BackupsDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        var result = new List<BackupSummary>();
        foreach (var archive in Directory
                     .EnumerateFiles(backupDirectory, "amz-cli-*.tar.gz")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var metadataPath = archive + ".json";
            try
            {
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(
                    File.ReadAllText(metadataPath),
                    UpdateJson.Options);
                if (metadata is null)
                {
                    continue;
                }
                if (installDir is not null
                    && !string.Equals(
                        metadata.InstallDir,
                        installDir,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                result.Add(new BackupSummary(
                    archive,
                    metadataPath,
                    metadata.Version,
                    metadata.Platform,
                    metadata.CreatedAt,
                    metadata.ArchiveSize,
                    metadata.ArchiveSha256,
                    metadata.InstallDir));
            }
            catch (Exception)
            {
                // Ignore malformed sidecars; the archive can still be restored
                // manually.
            }
        }
        return result;
    }

    public static async Task RestoreAsync(
        string archivePath,
        string installDir,
        string expectedVersion,
        string platform,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = archivePath + ".json";
        BackupMetadata? metadata = null;
        try
        {
            if (File.Exists(metadataPath))
            {
                metadata = JsonSerializer.Deserialize<BackupMetadata>(
                    File.ReadAllText(metadataPath),
                    UpdateJson.Options);
            }
        }
        catch (Exception)
        {
            metadata = null;
        }

        if (metadata is not null)
        {
            await VerifyAsync(archivePath, metadata, cancellationToken)
                .ConfigureAwait(false);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(installDir))
            ?? throw new UpdateException("Invalid installation directory.");
        var extraction = Path.Combine(
            parent,
            ".amz-cli-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        UpdatePaths.MarkHidden(extraction);

        try
        {
            var extracted = await ExtractAsync(
                    archivePath,
                    extraction,
                    cancellationToken)
                .ConfigureAwait(false);
            var rollback = Path.Combine(
                parent,
                ".amz-cli-restore-rollback-" + Guid.NewGuid().ToString("N"));
            SwapResult? swap = null;
            try
            {
                swap = FileSwap.Apply(
                    installDir,
                    extracted.Files,
                    rollback,
                    cancellationToken);
                var verify = await UpdateVerifier.VerifyAsync(
                        InstallLocation.ExecutablePath(installDir, platform),
                        expectedVersion,
                        platform,
                        requireCurl: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!verify.Success)
                {
                    throw new UpdateException(
                        $"Il backup ripristinato non ha superato il test: {verify.Detail}");
                }

                if (!extracted.HasInstallState)
                {
                    InstallStateStore.Write(
                        installDir,
                        expectedVersion,
                        platform,
                        string.Empty,
                        string.Empty);
                }
                FileSwap.Commit(swap);
            }
            catch (Exception)
            {
                if (swap is not null && !swap.Committed)
                {
                    FileSwap.Rollback(swap);
                }
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(extraction);
        }
    }

    private sealed record ExtractResult(
        List<StagedFile> Files,
        bool HasInstallState);

    private static async Task<ExtractResult> ExtractAsync(
        string archivePath,
        string extractionDirectory,
        CancellationToken cancellationToken)
    {
        var staged = new List<StagedFile>();
        var hasInstallState = false;
        await using var archive = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(
                   copyData: false,
                   cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (entry.EntryType != TarEntryType.RegularFile)
            {
                throw new UpdateException(
                    $"The backup contains an irregular entry: '{entry.Name}'.");
            }
            if (!PathSafety.IsSafeRelativePath(entry.Name))
            {
                throw new UpdateException(
                    $"The backup contains an unsafe path: '{entry.Name}'.");
            }
            if (entry.DataStream is null)
            {
                throw new UpdateException(
                    $"Il backup non contiene i dati di '{entry.Name}'.");
            }

            var target = PathSafety.CombineSafe(extractionDirectory, entry.Name);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await using (var output = new FileStream(
                             target,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 128 * 1024,
                             useAsync: true))
            {
                await entry.DataStream.CopyToAsync(output, cancellationToken)
                    .ConfigureAwait(false);
            }

            var mode = entry.Mode;
            var modeText = mode.HasFlag(UnixFileMode.UserExecute) ? "0755" : "0644";
            var info = new FileInfo(target);
            staged.Add(new StagedFile
            {
                RelativePath = entry.Name,
                SourcePath = target,
                Mode = modeText,
                Size = info.Length,
                Sha256 = await FileHash
                    .Sha256Async(target, cancellationToken)
                    .ConfigureAwait(false),
            });
            if (string.Equals(entry.Name, InstallState.FileName, StringComparison.Ordinal))
            {
                hasInstallState = true;
            }
        }

        if (staged.Count == 0)
        {
            throw new UpdateException("The backup is empty.");
        }
        return new ExtractResult(staged, hasInstallState);
    }

    private static IEnumerable<string> EnumerateFiles(string installDir)
    {
        foreach (var path in Directory.EnumerateFiles(
                     installDir,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = ToRelative(installDir, path);
            var root = relative.Split('/')[0];
            if (ExcludedRoots.Contains(root, StringComparer.OrdinalIgnoreCase)
                || root.StartsWith(".amz-cli-update-", StringComparison.Ordinal)
                || root.StartsWith(".amz-cli-restore-", StringComparison.Ordinal)
                || relative.EndsWith(".old", StringComparison.OrdinalIgnoreCase)
                || IsReparsePoint(path))
            {
                continue;
            }

            yield return path;
        }
    }

    private static string ToRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsExecutable(string relative)
    {
        var name = Path.GetFileName(relative);
        return name is "amz-cli" or "amz-cli.exe" or "amz-notify.exe";
    }

    private static UnixFileMode ParseMode(string mode) =>
        mode == "0755"
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteMetadata(string path, BackupMetadata metadata)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(metadata, UpdateJson.Options));
        File.Move(temporary, path, overwrite: true);
    }

    private static BackupMetadata? TryReadMetadata(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<BackupMetadata>(
                    File.ReadAllText(path),
                    UpdateJson.Options)
                : null;
        }
        catch (Exception)
        {
            return null;
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
}
