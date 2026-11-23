using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

/// <summary>
/// Per-user list of auto-start definitions. The unit/task only stores the id;
/// this file stores the complete command and metadata.
/// </summary>
public sealed class StartupRegistry
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public StartupRegistry(string? directory = null)
    {
        DirectoryPath = directory ?? UserStatePaths.StartupDirectory();
    }

    public string DirectoryPath { get; }

    public string FilePath(string id) =>
        Path.Combine(DirectoryPath, $"{SafeName(id)}.json");

    public void Save(StartupDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new MonitorException("Startup without an id.");
        }

        Directory.CreateDirectory(DirectoryPath);
        SetDirectoryMode();
        var path = FilePath(definition.Id);
        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(definition, Options));
            File.Move(temporary, path, overwrite: true);
            SetFileMode(path);
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

            throw new MonitorException(
                $"Could not save startup '{definition.Id}': {ex.Message}");
        }
    }

    public void Delete(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        try
        {
            File.Delete(FilePath(id));
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    public IReadOnlyList<StartupDefinition> ReadAll()
    {
        var entries = new List<StartupDefinition>();
        if (!Directory.Exists(DirectoryPath))
        {
            return entries;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            {
                try
                {
                    var entry = TryLoad(file);
                    if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
                    {
                        entries.Add(entry);
                    }
                }
                catch (MonitorException)
                {
                    // Ignore malformed startup entries.
                }
            }
        }
        catch (Exception)
        {
            // Best effort: an unreadable directory must not break --list.
        }

        return entries;
    }

    public StartupDefinition? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var path = FilePath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var entry = TryLoad(path);
        if (entry is null)
        {
            return null;
        }

        if (!string.Equals(entry.Id, id, StringComparison.Ordinal))
        {
            throw new MonitorException(
                $"Invalid startup '{path}': id mismatch.");
        }

        return entry;
    }

    public StartupDefinition? FindBySchedule(string savePath, string saveFile) =>
        ReadAll().FirstOrDefault(entry => MatchesSchedule(
            entry,
            savePath,
            saveFile));

    public static bool MatchesSchedule(
        StartupDefinition entry,
        string? savePath,
        string? saveFile) =>
        StartupIdentity.PathsEqual(entry.SavePath, savePath)
        && StartupIdentity.FilesEqual(entry.SaveFile, saveFile);

    private static StartupDefinition? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<StartupDefinition>(
                File.ReadAllText(path),
                Options);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            throw new MonitorException(
                $"Invalid startup '{path}': {ex.Message}");
        }
    }

    private void SetDirectoryMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                DirectoryPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void SetFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_');
        }

        return builder.Length == 0 ? "startup" : builder.ToString();
    }
}
