using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.History;

public static class HistoryKind
{
    public const string Search = "search";
    public const string Product = "product";
}

public static class HistorySource
{
    public const string Web = "web";
    public const string Cli = "cli";
    public const string Agent = "agent";
}

public static class HistoryStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";
}

public sealed class HistoryDocument
{
    public int Schema { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<HistoryEntry> Entries { get; set; } = [];
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = HistoryKind.Search;

    public string Source { get; set; } = HistorySource.Web;

    public string Status { get; set; } = HistoryStatus.Succeeded;

    public DateTimeOffset At { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Query { get; set; }

    public string? Domain { get; set; }

    public List<string> Domains { get; set; } = [];

    public HistorySearchFilters Filters { get; set; } = new();

    public int? ResultCount { get; set; }

    public string? Error { get; set; }

    public string? Asin { get; set; }

    public HistoryProductSnapshot? Snapshot { get; set; }
}

public sealed class HistorySearchFilters
{
    public bool NoAds { get; set; } = true;

    public bool Delivery { get; set; } = true;

    public bool Enrich { get; set; }

    public double MinRating { get; set; }

    public decimal? PriceMin { get; set; }

    public decimal? PriceMax { get; set; }

    public bool OnlyDeals { get; set; }

    public bool FreeShipping { get; set; }

    public bool AmazonChoice { get; set; }

    public string Sort { get; set; } = "relevance";
}

public sealed class HistoryProductSnapshot
{
    public string Title { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    public string Price { get; set; } = string.Empty;

    public decimal? PriceAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string OriginalPrice { get; set; } = string.Empty;

    public string Discount { get; set; } = string.Empty;

    public string Rating { get; set; } = string.Empty;

    public string Reviews { get; set; } = string.Empty;

    public string Availability { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public sealed record HistorySearchInput(
    string Query,
    string Domain,
    IReadOnlyList<string> Domains,
    HistorySearchFilters Filters,
    string Source,
    string? EntryId = null);

public sealed record HistoryProductInput(
    string Asin,
    string Domain,
    HistoryProductSnapshot Snapshot,
    string Source);

public sealed class HistoryStore
{
    public const int CurrentSchema = 1;
    private const int LockAttempts = 100;
    private const int LockDelayMilliseconds = 25;

    private readonly string _path;
    private readonly string _lockPath;

    public HistoryStore(
        string? path = null,
        string? lockPath = null)
    {
        _path = Path.GetFullPath(path ?? UserStatePaths.HistoryPath());
        _lockPath = Path.GetFullPath(lockPath ?? $"{_path}.lock");
    }

    public string FilePath => _path;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public bool Contains(string id)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        return document.Entries.Any(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    public HistoryDocument Read()
    {
        using var gate = AcquireLock();
        return ReadUnlocked();
    }

    public HistoryEntry StartSearch(HistorySearchInput input)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var now = DateTimeOffset.UtcNow;
        var entry = new HistoryEntry
        {
            Id = NewId(),
            Kind = HistoryKind.Search,
            Source = NormalizeSource(input.Source),
            Status = HistoryStatus.Running,
            At = now,
            StartedAt = now,
            Query = input.Query.Trim(),
            Domain = input.Domain.Trim().ToLowerInvariant(),
            Domains = NormalizeDomains(input.Domains, input.Domain),
            Filters = NormalizeFilters(input.Filters),
        };
        document.Entries.Add(entry);
        Write(document);
        return entry;
    }

    public HistoryEntry? RestartSearch(
        string id,
        HistorySearchFilters filters)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var entry = document.Entries.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal)
            && item.Kind == HistoryKind.Search);
        if (entry is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        entry.Status = HistoryStatus.Running;
        entry.At = now;
        entry.StartedAt = now;
        entry.FinishedAt = null;
        entry.ResultCount = null;
        entry.Error = null;
        entry.Filters = NormalizeFilters(filters);
        Write(document);
        return entry;
    }

    public HistoryDocument CompleteSearch(
        string id,
        string status,
        int? resultCount,
        string? error)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var entry = document.Entries.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return document;
        }

        entry.Status = NormalizeStatus(status);
        entry.FinishedAt = DateTimeOffset.UtcNow;
        entry.ResultCount = resultCount;
        entry.Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        Write(document);
        return document;
    }

    public HistoryEntry AddProduct(HistoryProductInput input)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var now = DateTimeOffset.UtcNow;
        var entry = new HistoryEntry
        {
            Id = NewId(),
            Kind = HistoryKind.Product,
            Source = NormalizeSource(input.Source),
            Status = HistoryStatus.Succeeded,
            At = now,
            Asin = input.Asin.Trim().ToUpperInvariant(),
            Domain = input.Domain.Trim().ToLowerInvariant(),
            Snapshot = NormalizeSnapshot(input.Snapshot),
        };
        document.Entries.Add(entry);
        Write(document);
        return entry;
    }

    public HistoryDocument Delete(string id)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var removed = document.Entries.RemoveAll(entry =>
            string.Equals(entry.Id, id, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            Write(document);
        }
        return document;
    }

    public HistoryDocument Clear()
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        if (document.Entries.Count == 0)
        {
            return document;
        }
        document.Entries.Clear();
        Write(document);
        return document;
    }

    public HistoryDocument MarkStaleRunning(TimeSpan maxAge)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var threshold = DateTimeOffset.UtcNow - maxAge;
        var changed = false;
        foreach (var entry in document.Entries.Where(entry =>
                     entry.Kind == HistoryKind.Search
                     && entry.Status == HistoryStatus.Running
                     && entry.StartedAt is { } started
                     && started < threshold))
        {
            entry.Status = HistoryStatus.Interrupted;
            entry.FinishedAt = DateTimeOffset.UtcNow;
            entry.Error = "The search was interrupted before completion.";
            changed = true;
        }
        if (changed)
        {
            Write(document);
        }
        return document;
    }

    private HistoryDocument ReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return new HistoryDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<HistoryDocument>(
                File.ReadAllText(_path),
                JsonOptions) ?? new HistoryDocument();
            if (document.Schema > CurrentSchema)
            {
                throw new HistoryException(
                    $"History schema {document.Schema} is newer than the " +
                    $"supported schema {CurrentSchema}.");
            }
            document.Schema = CurrentSchema;
            document.Revision = Math.Max(0, document.Revision);
            document.Entries ??= [];
            document.Entries = document.Entries
                .Where(entry => entry is not null)
                .Select(NormalizeEntry)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .OrderByDescending(entry => entry.At)
                .ToList();
            return document;
        }
        catch (HistoryException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            throw new HistoryException(
                $"Unable to read history '{_path}': {ex.Message}");
        }
    }

    private static HistoryEntry? NormalizeEntry(HistoryEntry entry)
    {
        var kind = (entry.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not (HistoryKind.Search or HistoryKind.Product))
        {
            return null;
        }
        var source = NormalizeSource(entry.Source);
        var status = NormalizeStatus(entry.Status);
        var at = entry.At == default ? DateTimeOffset.UtcNow : entry.At;
        var id = string.IsNullOrWhiteSpace(entry.Id) ? NewId() : entry.Id.Trim();
        if (kind == HistoryKind.Search)
        {
            var query = (entry.Query ?? string.Empty).Trim();
            var domain = (entry.Domain ?? string.Empty).Trim().ToLowerInvariant();
            if (query.Length == 0 || domain.Length == 0)
            {
                return null;
            }
            return new HistoryEntry
            {
                Id = id,
                Kind = kind,
                Source = source,
                Status = status,
                At = at,
                StartedAt = entry.StartedAt ?? at,
                FinishedAt = entry.FinishedAt,
                Query = query,
                Domain = domain,
                Domains = NormalizeDomains(entry.Domains, domain),
                Filters = NormalizeFilters(entry.Filters),
                ResultCount = entry.ResultCount is >= 0 ? entry.ResultCount : null,
                Error = string.IsNullOrWhiteSpace(entry.Error)
                    ? null
                    : entry.Error.Trim(),
            };
        }

        var asin = (entry.Asin ?? string.Empty).Trim().ToUpperInvariant();
        var productDomain = (entry.Domain ?? string.Empty).Trim().ToLowerInvariant();
        if (asin.Length == 0 || productDomain.Length == 0)
        {
            return null;
        }
        return new HistoryEntry
        {
            Id = id,
            Kind = kind,
            Source = source,
            Status = HistoryStatus.Succeeded,
            At = at,
            Asin = asin,
            Domain = productDomain,
            Snapshot = NormalizeSnapshot(entry.Snapshot),
        };
    }

    private void Write(HistoryDocument document)
    {
        document.Revision++;
        document.UpdatedAt = DateTimeOffset.UtcNow;
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
                JsonSerializer.Serialize(document, JsonOptions),
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
            throw new HistoryException(
                $"Unable to write history '{_path}': {ex.Message}");
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
                throw new HistoryException(
                    $"History is busy: {_lockPath}");
            }
        }
        throw new HistoryException($"History is busy: {_lockPath}");
    }

    private static List<string> NormalizeDomains(
        IEnumerable<string>? domains,
        string fallback)
    {
        var result = (domains ?? [])
            .Select(domain => (domain ?? string.Empty).Trim().ToLowerInvariant())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (result.Count == 0)
        {
            var fallbackDomain = fallback.Trim().ToLowerInvariant();
            if (fallbackDomain.Length > 0)
            {
                result.Add(fallbackDomain);
            }
        }
        return result;
    }

    private static HistorySearchFilters NormalizeFilters(
        HistorySearchFilters? filters)
    {
        var source = filters ?? new HistorySearchFilters();
        var sort = (source.Sort ?? string.Empty).Trim().ToLowerInvariant();
        if (sort is not ("relevance" or "price-asc" or "price-desc"
            or "reviews" or "newest" or "bestsellers"))
        {
            sort = "relevance";
        }
        return new HistorySearchFilters
        {
            NoAds = source.NoAds,
            Delivery = source.Delivery,
            Enrich = source.Enrich,
            MinRating = source.MinRating is >= 0 and <= 5
                ? source.MinRating
                : 0,
            PriceMin = source.PriceMin is >= 0 ? source.PriceMin : null,
            PriceMax = source.PriceMax is >= 0 ? source.PriceMax : null,
            OnlyDeals = source.OnlyDeals,
            FreeShipping = source.FreeShipping,
            AmazonChoice = source.AmazonChoice,
            Sort = sort,
        };
    }

    private static HistoryProductSnapshot NormalizeSnapshot(
        HistoryProductSnapshot? snapshot)
    {
        var source = snapshot ?? new HistoryProductSnapshot();
        return new HistoryProductSnapshot
        {
            Title = (source.Title ?? string.Empty).Trim(),
            Image = (source.Image ?? string.Empty).Trim(),
            Price = (source.Price ?? string.Empty).Trim(),
            PriceAmount = source.PriceAmount is >= 0
                ? source.PriceAmount
                : null,
            Currency = (source.Currency ?? string.Empty).Trim().ToUpperInvariant(),
            OriginalPrice = (source.OriginalPrice ?? string.Empty).Trim(),
            Discount = (source.Discount ?? string.Empty).Trim(),
            Rating = (source.Rating ?? string.Empty).Trim(),
            Reviews = (source.Reviews ?? string.Empty).Trim(),
            Availability = (source.Availability ?? string.Empty).Trim(),
            Url = (source.Url ?? string.Empty).Trim(),
        };
    }

    private static string NormalizeSource(string? source)
    {
        var value = (source ?? string.Empty).Trim().ToLowerInvariant();
        return value is HistorySource.Web or HistorySource.Cli or HistorySource.Agent
            ? value
            : HistorySource.Cli;
    }

    private static string NormalizeStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim().ToLowerInvariant();
        return value is HistoryStatus.Running
            or HistoryStatus.Succeeded
            or HistoryStatus.Failed
            or HistoryStatus.Cancelled
            or HistoryStatus.Interrupted
            ? value
            : HistoryStatus.Succeeded;
    }

    private static string NewId() =>
        $"h_{Guid.NewGuid():N}"[..14];

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

public sealed class HistoryException(string message)
    : Exception(message);
