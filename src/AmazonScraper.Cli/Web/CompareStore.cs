using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Cli.Web;

public class CompareException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class CompareFullException(string message)
    : CompareException(message);

public sealed class CompareItem
{
    public string Id { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string Asin { get; set; } = string.Empty;

    public string Source { get; set; } = "web";

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CompareDocument
{
    public int Schema { get; set; } = CompareStore.CurrentSchema;

    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CompareItem> Items { get; set; } = [];
}

public sealed record CompareTarget(string Domain, string Asin)
{
    public string Id => $"{Domain}:{Asin}";
}

/// <summary>
/// Ephemeral side-by-side comparison tabs shared by the Web UI and the CLI.
/// It stores only marketplace/ASIN pairs; product details are fetched by the
/// Web client and remain in the existing product cache.
/// </summary>
public sealed class CompareStore
{
    public const int CurrentSchema = 1;
    public const int MaxItems = 6;

    private const int LockAttempts = 100;
    private const int LockDelayMilliseconds = 25;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly string _lockPath;

    public CompareStore(string? path = null, string? lockPath = null)
    {
        _path = Path.GetFullPath(path ?? WebPaths.ComparePath());
        _lockPath = Path.GetFullPath(lockPath ?? $"{_path}.lock");
    }

    public string FilePath => _path;

    public string LockPath => _lockPath;

    public CompareDocument Read()
    {
        using var compareLock = AcquireLock();
        return ReadUnlocked();
    }

    public CompareDocument Add(
        IEnumerable<CompareTarget> targets,
        string source = "web")
    {
        var normalized = NormalizeTargets(targets);
        if (normalized.Count == 0)
        {
            throw new CompareException("No products were provided.");
        }

        return Mutate(document =>
        {
            var existing = document.Items
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var additions = normalized
                .Where(target => !existing.Contains(target.Id))
                .ToList();
            if (document.Items.Count + additions.Count > MaxItems)
            {
                throw new CompareFullException(
                    $"The comparison can contain at most {MaxItems} products. " +
                    "Close a tab before adding another product.");
            }
            if (additions.Count == 0)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var canonicalSource = NormalizeSource(source);
            foreach (var target in additions)
            {
                document.Items.Add(new CompareItem
                {
                    Id = target.Id,
                    Domain = target.Domain,
                    Asin = target.Asin,
                    Source = canonicalSource,
                    AddedAt = now,
                });
            }
            return true;
        });
    }

    public CompareDocument Remove(string? domain, string? asin)
    {
        var target = NormalizeTarget(domain, asin);
        return Mutate(document =>
        {
            var removed = document.Items.RemoveAll(item =>
                string.Equals(item.Id, target.Id, StringComparison.Ordinal));
            return removed > 0;
        });
    }

    public CompareDocument Clear() =>
        Mutate(document =>
        {
            if (document.Items.Count == 0)
            {
                return false;
            }
            document.Items.Clear();
            return true;
        });

    public static string NormalizeAsin(string? value)
    {
        var asin = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (asin.Length != 10
            || !asin.All(character => char.IsAsciiLetterOrDigit(character)))
        {
            throw new CompareException(
                "Invalid ASIN: 10 alphanumeric characters are required.");
        }
        return asin;
    }

    public static CompareTarget NormalizeTarget(string? domain, string? asin)
    {
        if (!MarketplaceRegistry.TryResolve(domain, out var marketplace))
        {
            throw new CompareException(
                $"Unsupported marketplace: '{domain}'.");
        }
        return new CompareTarget(marketplace.Id, NormalizeAsin(asin));
    }

    private static List<CompareTarget> NormalizeTargets(
        IEnumerable<CompareTarget> targets)
    {
        var results = new List<CompareTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in targets ?? [])
        {
            if (candidate is null)
            {
                continue;
            }
            var normalized = NormalizeTarget(candidate.Domain, candidate.Asin);
            if (seen.Add(normalized.Id))
            {
                results.Add(normalized);
            }
        }
        return results;
    }

    private static string NormalizeSource(string? source)
    {
        var value = (source ?? string.Empty).Trim().ToLowerInvariant();
        return value is "web" or "cli" or "agent" ? value : "web";
    }

    private CompareDocument Mutate(Func<CompareDocument, bool> mutation)
    {
        using var compareLock = AcquireLock();
        var document = ReadUnlocked();
        if (!mutation(document))
        {
            return document;
        }

        document.Schema = CurrentSchema;
        document.Revision++;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        Write(document);
        return document;
    }

    private CompareDocument ReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return new CompareDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<CompareDocument>(
                File.ReadAllText(_path),
                Options);
            return NormalizeDocument(document ?? new CompareDocument());
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            // A damaged comparison file must not prevent the Web UI from
            // starting. The next mutation replaces it.
            return new CompareDocument();
        }
    }

    private static CompareDocument NormalizeDocument(CompareDocument document)
    {
        var results = new List<CompareItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.Items ?? [])
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.Domain)
                || string.IsNullOrWhiteSpace(item.Asin))
            {
                continue;
            }
            CompareTarget normalized;
            try
            {
                normalized = NormalizeTarget(item.Domain, item.Asin);
            }
            catch (CompareException)
            {
                continue;
            }
            if (!seen.Add(normalized.Id))
            {
                continue;
            }
            results.Add(new CompareItem
            {
                Id = normalized.Id,
                Domain = normalized.Domain,
                Asin = normalized.Asin,
                Source = NormalizeSource(item.Source),
                AddedAt = item.AddedAt == default
                    ? DateTimeOffset.UtcNow
                    : item.AddedAt,
            });
            if (results.Count >= MaxItems)
            {
                break;
            }
        }

        return new CompareDocument
        {
            Schema = CurrentSchema,
            Revision = Math.Max(0, document.Revision),
            UpdatedAt = document.UpdatedAt == default
                ? DateTimeOffset.UtcNow
                : document.UpdatedAt,
            Items = results,
        };
    }

    private void Write(CompareDocument document)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, Options),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TrySetUnixMode(
                temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, overwrite: true);
            TrySetUnixMode(
                _path,
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
            throw new CompareException(
                $"Unable to write the comparison '{_path}': {ex.Message}. " +
                "The previous file was left unchanged.",
                ex);
        }
    }

    private FileStream AcquireLock()
    {
        var directory = Path.GetDirectoryName(_lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                TrySetUnixMode(
                    _lockPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return stream;
            }
            catch (IOException) when (attempt + 1 < LockAttempts)
            {
                Thread.Sleep(LockDelayMilliseconds);
            }
            catch (IOException ex)
            {
                throw new CompareException(
                    $"The comparison is busy: {_lockPath}",
                    ex);
            }
        }

        throw new CompareException(
            $"The comparison is busy: {_lockPath}");
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
            // Best effort: the process umask or a read-only filesystem may
            // reject the mode.
        }
    }
}
