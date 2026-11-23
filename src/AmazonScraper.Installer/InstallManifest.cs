using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Installer;

public sealed class InstallManifest
{
    public const string ProductName = "amz-cli";

    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("product")]
    public string Product { get; set; } = ProductName;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("install_dir")]
    public string InstallDir { get; set; } = string.Empty;

    [JsonPropertyName("installed_at")]
    public string InstalledAt { get; set; } = string.Empty;

    [JsonPropertyName("release_published_at")]
    public DateTimeOffset? ReleasePublishedAt { get; set; }

    [JsonPropertyName("installer_version")]
    public string InstallerVersion { get; set; } = InstallerInfo.Version;

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static InstallManifest? TryRead(string installDir)
    {
        var path = Path.Combine(installDir, "install.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(
                File.ReadAllText(path),
                Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Write(string installDir)
    {
        Directory.CreateDirectory(installDir);
        var path = Path.Combine(installDir, "install.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static InstallManifest Create(
        string installDir,
        string version,
        string platform,
        string channel,
        IEnumerable<string> files,
        DateTimeOffset? releasePublishedAt = null) =>
        new()
        {
            Version = version,
            Platform = platform,
            Channel = channel,
            InstallDir = installDir,
            InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
            ReleasePublishedAt = releasePublishedAt,
            InstallerVersion = InstallerInfo.Version,
            Files = files.ToList(),
        };
}

public static class InstallerInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public static string Display => $"amz-cli-installer {Version}";
}
