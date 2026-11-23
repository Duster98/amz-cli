using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmazonScraper.Cli.Web;

public sealed class WebSessionInfo
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("local_url")]
    public string LocalUrl { get; set; } = string.Empty;

    [JsonPropertyName("lan_url")]
    public string LanUrl { get; set; } = string.Empty;

    [JsonPropertyName("remote")]
    public bool Remote { get; set; }
}

public static class WebSessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static WebSessionInfo? TryRead(string? path = null)
    {
        var file = path ?? WebPaths.SessionPath();
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WebSessionInfo>(
                File.ReadAllText(file),
                Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Write(WebSessionInfo info, string? path = null)
    {
        var file = path ?? WebPaths.SessionPath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(directory, UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(info, Options),
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
            var file = path ?? WebPaths.SessionPath();
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception)
        {
            // Best effort: a stale session file is detected by the control ping.
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
            // The process umask or a read-only filesystem may reject the mode.
        }
    }
}

/// <summary>
/// Cross-process singleton lock for the local web interface. The session file
/// remains the source of connection details; this lock prevents two servers
/// from racing on the same port and state directory.
/// </summary>
public sealed class WebInstanceLock : IDisposable
{
    private readonly string _path;
    private readonly FileStream _stream;
    private bool _disposed;

    private WebInstanceLock(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static WebInstanceLock Acquire(string? path = null)
    {
        var file = path ?? WebPaths.LockPath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        directory,
                        UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }
        }

        try
        {
            var stream = new FileStream(
                file,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            stream.SetLength(0);
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       bufferSize: 1024,
                       leaveOpen: true))
            {
                writer.Write(Environment.ProcessId);
                writer.Flush();
            }
            stream.Flush();
            return new WebInstanceLock(file, stream);
        }
        catch (IOException ex)
        {
            var pid = TryRead(file);
            throw new WebException(
                pid is null
                    ? "The web interface is already running."
                    : $"The web interface is already running (PID {pid}).",
                ex);
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
