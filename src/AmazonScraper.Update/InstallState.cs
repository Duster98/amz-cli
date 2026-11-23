using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Update;

public sealed class InstallState
{
    public const string FileName = ".amz-cli-install.json";

    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("manifest")]
    public string Manifest { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("installed_at")]
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class InstallStateStore
{
    public static string PathFor(string installDir) =>
        Path.Combine(installDir, InstallState.FileName);

    public static InstallState? Read(string installDir)
    {
        try
        {
            var path = PathFor(installDir);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<InstallState>(
                File.ReadAllText(path),
                ReleaseJson.Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Write(
        string installDir,
        string version,
        string platform,
        string manifest,
        string signature)
    {
        var state = new InstallState
        {
            Version = version,
            Platform = platform,
            Manifest = manifest,
            Signature = signature,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        var path = PathFor(installDir);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(state, UpdateJson.Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static IReadOnlyList<string> PreviousFiles(string installDir)
    {
        var state = Read(installDir);
        if (state is null || string.IsNullOrWhiteSpace(state.Manifest))
        {
            return [];
        }

        try
        {
            var manifest = ReleaseManifest.Parse(state.Manifest);
            return manifest.Files.Select(file => file.Path).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
