using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public sealed class ReleaseFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "0644";

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("download_path")]
    public string DownloadPath { get; init; } = string.Empty;

    [JsonPropertyName("download_size")]
    public long DownloadSize { get; init; }

    [JsonPropertyName("download_sha256")]
    public string DownloadSha256 { get; init; } = string.Empty;

    [JsonPropertyName("compression")]
    public string Compression { get; init; } = "gzip";

    public bool IsGzip => string.Equals(Compression, "gzip", StringComparison.Ordinal);
}

public sealed class ReleasePackage
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("download_path")]
    public string DownloadPath { get; init; } = string.Empty;
}

public sealed class ReleaseManifest
{
    private const long MaxUncompressedSize = 256L * 1024 * 1024;

    [JsonPropertyName("schema")]
    public int Schema { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    [JsonPropertyName("min_supported_version")]
    public string? MinSupportedVersion { get; init; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("notes_markdown")]
    public string? NotesMarkdown { get; init; }

    public string? DisplayNotes =>
        !string.IsNullOrWhiteSpace(NotesMarkdown) ? NotesMarkdown : Notes;

    public bool HasMarkdownNotes =>
        !string.IsNullOrWhiteSpace(NotesMarkdown);

    [JsonPropertyName("files")]
    public List<ReleaseFile> Files { get; init; } = [];

    [JsonPropertyName("package")]
    public ReleasePackage? Package { get; init; }

    public bool IsPackageManifest =>
        Schema == 2 && Package is not null;

    public SemanticVersion ParsedVersion => SemanticVersion.Parse(Version);

    public SemanticVersion? ParsedMinSupportedVersion =>
        string.IsNullOrWhiteSpace(MinSupportedVersion)
            ? null
            : SemanticVersion.Parse(MinSupportedVersion);

    public void Validate()
    {
        if (Schema is not (1 or 2))
        {
            throw new UpdateException($"Unsupported manifest schema: {Schema}.");
        }

        if (!SemanticVersion.TryParse(Version, out _))
        {
            throw new UpdateException($"Invalid manifest version: '{Version}'.");
        }

        if (!PlatformInfo.IsSupported(Platform))
        {
            throw new UpdateException($"Unsupported manifest platform: '{Platform}'.");
        }

        if (Channel is not ("stable" or "beta"))
        {
            throw new UpdateException($"Invalid manifest channel: '{Channel}'.");
        }

        if (!string.IsNullOrWhiteSpace(MinSupportedVersion)
            && !SemanticVersion.TryParse(MinSupportedVersion, out _))
        {
            throw new UpdateException(
                $"Invalid min_supported_version in manifest: '{MinSupportedVersion}'.");
        }

        if (PublishedAt == default)
        {
            throw new UpdateException("Missing published_at in manifest.");
        }

        if ((Notes?.Length ?? 0) > 10_000)
        {
            throw new UpdateException("Release notes are too long.");
        }

        if ((NotesMarkdown?.Length ?? 0) > 20_000)
        {
            throw new UpdateException("Markdown release notes are too long.");
        }

        if (!string.IsNullOrWhiteSpace(NotesMarkdown)
            && string.IsNullOrWhiteSpace(Notes))
        {
            throw new UpdateException(
                "notes_markdown is present without a plain notes fallback.");
        }

        if (Files.Count is 0 or > UpdateEndpoints.MaxFiles)
        {
            throw new UpdateException($"Invalid file count in manifest: {Files.Count}.");
        }

        if (Schema == 1 && Package is not null)
        {
            throw new UpdateException(
                "Schema 1 must not contain a package section.");
        }
        if (Schema == 2 && Package is null)
        {
            throw new UpdateException(
                "Schema 2 requires a package section.");
        }
        if (Package is not null)
        {
            ValidatePackage(Package);
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var downloads = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Files)
        {
            ValidateFile(file);

            if (!paths.Add(file.Path))
            {
                throw new UpdateException($"Duplicate file in manifest: '{file.Path}'.");
            }
            if (Schema == 1)
            {
                if (!downloads.Add(file.DownloadPath))
                {
                    throw new UpdateException(
                        $"Duplicate download_path in manifest: '{file.DownloadPath}'.");
                }
            }
            else if (file.Compression != "none")
            {
                throw new UpdateException(
                    $"Package file '{file.Path}' must use compression 'none'.");
            }
        }

        var executable = PlatformInfo.ExecutableName(Platform);
        var legacyExecutable = PlatformInfo.LegacyExecutableName(Platform);
        if (!Files.Any(file =>
                string.Equals(file.Path, executable, StringComparison.Ordinal)
                || (Schema == 1
                    && string.Equals(
                        file.Path,
                        legacyExecutable,
                        StringComparison.Ordinal))))
        {
            throw new UpdateException(
                $"Manifest {Platform} does not contain the executable '{executable}'.");
        }
    }

    private void ValidatePackage(ReleasePackage package)
    {
        if (!PathSafety.IsSafeRelativePath(package.Name)
            || package.Name.Contains('/', StringComparison.Ordinal)
            || package.Name.Contains('\\', StringComparison.Ordinal)
            || package.Name.StartsWith('.')
            || package.Name.Contains("/.", StringComparison.Ordinal))
        {
            throw new UpdateException(
                $"Unsafe package name: '{package.Name}'.");
        }
        if (!FileHash.LooksLikeSha256(package.Sha256))
        {
            throw new UpdateException(
                $"Invalid package hash: '{package.Name}'.");
        }
        if (package.Size is <= 0 or > UpdateEndpoints.MaxPackageDownloadSize)
        {
            throw new UpdateException(
                $"Invalid package size: '{package.Name}'.");
        }
        if (!PathSafety.IsSafeRelativePath(package.DownloadPath))
        {
            throw new UpdateException(
                $"Unsafe package download_path: '{package.DownloadPath}'.");
        }
        var prefix = $"{Channel}/{Platform}/{Version}/";
        if (!package.DownloadPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new UpdateException(
                $"Package download_path is outside the expected prefix: '{package.DownloadPath}'.");
        }
        if (!package.DownloadPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(
                $"Package download_path must end with .zip: '{package.DownloadPath}'.");
        }
    }

    private void ValidateFile(ReleaseFile file)
    {
        if (!PathSafety.IsSafeRelativePath(file.Path))
        {
            throw new UpdateException($"Unsafe file path: '{file.Path}'.");
        }
        if (file.Path.StartsWith('.') || file.Path.Contains("/.", StringComparison.Ordinal))
        {
            throw new UpdateException($"Hidden files are not allowed: '{file.Path}'.");
        }
        if (file.Mode is not ("0644" or "0755"))
        {
            throw new UpdateException($"Invalid mode for '{file.Path}': '{file.Mode}'.");
        }
        if (file.Compression is not ("gzip" or "none"))
        {
            throw new UpdateException(
                $"Invalid compression for '{file.Path}': '{file.Compression}'.");
        }
        if (!FileHash.LooksLikeSha256(file.Sha256))
        {
            throw new UpdateException($"Invalid hash for '{file.Path}'.");
        }
        if (file.Size is <= 0 or > MaxUncompressedSize)
        {
            throw new UpdateException($"Invalid size for '{file.Path}'.");
        }

        if (Schema == 1)
        {
            if (!FileHash.LooksLikeSha256(file.DownloadSha256))
            {
                throw new UpdateException($"Invalid download hash for '{file.Path}'.");
            }
            if (file.DownloadSize is <= 0 or > UpdateEndpoints.MaxDownloadSize)
            {
                throw new UpdateException(
                    $"Invalid download size for '{file.Path}'.");
            }
            if (!PathSafety.IsSafeRelativePath(file.DownloadPath))
            {
                throw new UpdateException(
                    $"Unsafe download_path: '{file.DownloadPath}'.");
            }

            var prefix = $"{Channel}/{Platform}/{Version}/";
            if (!file.DownloadPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"download_path is outside the expected prefix for '{file.Path}'.");
            }
            if (file.IsGzip && !file.DownloadPath.EndsWith(".gz", StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Gzip download_path without .gz extension for '{file.Path}'.");
            }
        }
    }

    public static ReleaseManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new UpdateException("Empty manifest.");
        }
        if (json.Length > UpdateEndpoints.MaxManifestBytes)
        {
            throw new UpdateException("Manifest is too large.");
        }

        ReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(
                json,
                ReleaseJson.Options);
        }
        catch (JsonException ex)
        {
            throw new UpdateException($"Invalid manifest JSON: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new UpdateException("Invalid manifest JSON.");
        }

        manifest.Validate();
        return manifest;
    }
}

public static class ReleaseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = false,
    };
}
