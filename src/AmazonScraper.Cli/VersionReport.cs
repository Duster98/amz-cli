using System.Globalization;
using System.Text.Json;
using AmazonScraper.Update;

namespace AmazonScraper.Cli;

public sealed record VersionReport(
    string Version,
    string Channel,
    DateTimeOffset? ReleasedAt,
    string Platform,
    string InstallDir,
    DateTimeOffset? InstalledAt,
    DateTimeOffset? UpdatedAt,
    string Source)
{
    public string ShortDisplay => $"amz-cli {Version}";

    public string Display
    {
        get
        {
            var channel = string.IsNullOrWhiteSpace(Channel) ? "dev" : Channel;
            var date = ReleasedAt is null
                ? string.Empty
                : ", " + FormatDate(ReleasedAt.Value);
            return $"amz-cli {Version} ({channel}{date})";
        }
    }

    public string? ReleasedDate => ReleasedAt is null
        ? null
        : FormatDate(ReleasedAt.Value);

    public static VersionReport Read(string? installDir = null)
    {
        var directory = installDir ?? ResolveInstallDir();
        var install = ReadInstallJson(directory);
        var state = ReadUpdateState(directory);
        var manifest = state.Manifest;
        var version = install?.Version
            ?? manifest?.Version
            ?? VersionInfo.Informational;
        var manifestMatches = manifest is not null
            && string.Equals(
                manifest.Version,
                version,
                StringComparison.Ordinal);
        var channel = install?.Channel;
        if (string.IsNullOrWhiteSpace(channel) && manifestMatches)
        {
            channel = manifest?.Channel;
        }
        channel = string.IsNullOrWhiteSpace(channel) ? "dev" : channel;

        var released = install?.ReleasePublishedAt;
        if (released is null && manifestMatches)
        {
            var published = manifest?.PublishedAt;
            released = published is { } value && value != default
                ? value
                : null;
        }

        var platform = install?.Platform;
        if (string.IsNullOrWhiteSpace(platform) && manifestMatches)
        {
            platform = manifest?.Platform;
        }
        platform = string.IsNullOrWhiteSpace(platform)
            ? PlatformInfo.Detect()
            : platform;

        var installedAt = install?.InstalledAt ?? state.InstalledAt;
        var source = install is not null
            ? "install.json"
            : manifest is not null
                ? ".amz-cli-install.json"
                : "build";

        return new VersionReport(
            version,
            channel,
            released,
            platform,
            string.IsNullOrWhiteSpace(install?.InstallDir)
                ? directory
                : install.InstallDir,
            installedAt,
            install?.UpdatedAt,
            source);
    }

    private static string ResolveInstallDir()
    {
        try
        {
            return InstallLocation.Resolve();
        }
        catch (Exception)
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(AppContext.BaseDirectory));
        }
    }

    private static InstallRecord? ReadInstallJson(string installDir)
    {
        var path = Path.Combine(installDir, "install.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new InstallRecord(
                GetString(root, "version"),
                GetString(root, "channel"),
                GetString(root, "platform"),
                GetString(root, "install_dir"),
                GetDate(root, "installed_at"),
                GetDate(root, "updated_at"),
                GetDate(root, "release_published_at"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static UpdateState ReadUpdateState(string installDir)
    {
        var state = InstallStateStore.Read(installDir);
        if (state is null || string.IsNullOrWhiteSpace(state.Manifest))
        {
            return new UpdateState(state?.InstalledAt, null);
        }

        try
        {
            return new UpdateState(
                state.InstalledAt,
                ReleaseManifest.Parse(state.Manifest));
        }
        catch (Exception)
        {
            return new UpdateState(state.InstalledAt, null);
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static DateTimeOffset? GetDate(JsonElement root, string name)
    {
        var text = GetString(root, name);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record InstallRecord(
        string? Version,
        string? Channel,
        string? Platform,
        string? InstallDir,
        DateTimeOffset? InstalledAt,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? ReleasePublishedAt);

    private sealed record UpdateState(
        DateTimeOffset? InstalledAt,
        ReleaseManifest? Manifest);
}
