using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmazonScraper.Update;

/// <summary>
/// Keeps the standalone installer's metadata in sync after a CLI self-update.
/// The file is optional: a manual/source install may not have it. Unknown
/// fields are preserved so the installer can evolve independently.
/// </summary>
public static class InstallManifestUpdater
{
    /// <summary>
    /// Repairs install.json when the binary is newer than the installer record
    /// or when a previous helper did not write the release date. This covers
    /// updates applied by an older helper that predates this metadata update.
    /// </summary>
    public static bool SyncCurrent(
        string installDir,
        string version,
        string platform)
    {
        var path = Path.Combine(installDir, "install.json");
        if (!File.Exists(path))
        {
            return false;
        }

        string? savedVersion;
        string? savedPlatform;
        string? savedRelease;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null)
            {
                return false;
            }

            savedVersion = node["version"]?.ToString();
            savedPlatform = node["platform"]?.ToString();
            savedRelease = node["release_published_at"]?.ToString();
        }
        catch (Exception)
        {
            return false;
        }

        string? manifestChannel = null;
        DateTimeOffset? manifestPublished = null;
        try
        {
            var state = InstallStateStore.Read(installDir);
            if (state is not null && !string.IsNullOrWhiteSpace(state.Manifest))
            {
                var manifest = ReleaseManifest.Parse(state.Manifest);
                if (string.Equals(manifest.Version, version, StringComparison.Ordinal)
                    && string.Equals(manifest.Platform, platform, StringComparison.Ordinal))
                {
                    manifestChannel = manifest.Channel;
                    manifestPublished = manifest.PublishedAt;
                }
            }
        }
        catch (Exception)
        {
            // A missing or malformed update state is not fatal.
        }

        var sameIdentity = string.Equals(
                savedVersion,
                version,
                StringComparison.Ordinal)
            && string.Equals(savedPlatform, platform, StringComparison.Ordinal);
        var releaseNeedsUpdate = manifestPublished is not null
            && (!DateTimeOffset.TryParse(
                    savedRelease,
                    out var savedDate)
                || Math.Abs((savedDate - manifestPublished.Value).TotalMinutes) > 1);

        if (sameIdentity && !releaseNeedsUpdate)
        {
            return false;
        }

        return Update(
            installDir,
            version,
            platform,
            channel: manifestChannel,
            files: null,
            releasePublishedAt: manifestPublished);
    }

    public static bool Update(
        string installDir,
        string version,
        string platform,
        string? channel = null,
        IReadOnlyList<string>? files = null,
        DateTimeOffset? releasePublishedAt = null)
    {
        var path = Path.Combine(installDir, "install.json");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null)
            {
                return false;
            }

            node["version"] = version;
            node["platform"] = platform;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                node["channel"] = channel;
            }
            if (releasePublishedAt is not null)
            {
                node["release_published_at"] = releasePublishedAt.Value
                    .ToUniversalTime()
                    .ToString("O");
            }
            if (files is not null)
            {
                var array = new JsonArray();
                foreach (var file in files)
                {
                    array.Add(file);
                }
                node["files"] = array;
            }
            node["updated_at"] = DateTimeOffset.UtcNow.ToString("O");

            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(
                temporary,
                node.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            // Metadata is informative; never fail an otherwise valid update.
            return false;
        }
    }
}
