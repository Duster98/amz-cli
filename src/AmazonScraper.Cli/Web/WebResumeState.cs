using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Cli.Web;

public sealed class WebResumeState
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("log_path")]
    public string LogPath { get; set; } = WebPaths.LogPath();

    [JsonPropertyName("desktop")]
    public bool Desktop { get; set; }

    [JsonPropertyName("remote")]
    public bool Remote { get; set; }

    [JsonPropertyName("development")]
    public bool Development { get; set; }

    [JsonPropertyName("web_directory")]
    public string? WebDirectory { get; set; }

    [JsonPropertyName("idle_minutes")]
    public int IdleMinutes { get; set; } = 30;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class WebResumeStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static WebResumeState? TryRead(string? path = null)
    {
        var file = path ?? WebPaths.UpdateResumePath();
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }
            return JsonSerializer.Deserialize<WebResumeState>(
                File.ReadAllText(file),
                Options);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Write(WebResumeState state, string? path = null)
    {
        var file = path ?? WebPaths.UpdateResumePath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(state, Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TrySetUnixMode(
            temporary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, file, overwrite: true);
        TrySetUnixMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void Delete(string? path = null)
    {
        try
        {
            File.Delete(path ?? WebPaths.UpdateResumePath());
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void TrySetUnixMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
