using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmazonScraper.Cli.History;

/// <summary>
/// Ephemeral search-result cache for the running web process. The directory is
/// cleared every time the web host starts.
/// </summary>
public sealed class HistoryCacheStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;

    public HistoryCacheStore(string? directory = null)
    {
        _directory = Path.GetFullPath(
            directory ?? AmazonScraper.Cli.Web.WebPaths.HistoryCacheDirectory());
    }

    public string DirectoryPath => _directory;

    public void Clear()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
            Directory.CreateDirectory(_directory);
            TrySetUnixMode(
                _directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(
                $"Unable to clear history cache '{_directory}': {ex.Message}");
        }
    }

    public bool Has(string entryId) => File.Exists(PathFor(entryId));

    public JsonObject? Read(string entryId)
    {
        var path = PathFor(entryId);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return null;
        }
    }

    public void Save(string entryId, JsonNode payload) =>
        SavePath(PathFor(entryId), payload);

    public void Delete(string entryId)
    {
        try
        {
            File.Delete(PathFor(entryId));
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private void SavePath(string path, JsonNode payload)
    {
        Directory.CreateDirectory(_directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        var document = new JsonObject
        {
            ["cached_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["result"] = payload.DeepClone(),
        };
        try
        {
            File.WriteAllText(
                temporary,
                document.ToJsonString(Options),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TrySetUnixMode(
                temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
            TrySetUnixMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception)
            {
                // Best effort.
            }
            throw new HistoryException(
                $"Unable to write history cache '{path}': {ex.Message}");
        }
    }


    private string PathFor(string entryId)
    {
        var safe = new string(entryId
            .Where(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_')
            .ToArray());
        if (safe.Length == 0)
        {
            safe = "entry";
        }
        return Path.Combine(_directory, $"{safe}.json");
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
