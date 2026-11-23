using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Library;

public sealed class SavedLibraryDocument
{
    public int Schema { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SavedProduct> Products { get; set; } = [];

    public List<SavedGroup> Groups { get; set; } = [];

    public SavedLibrary ToLibrary() => new()
    {
        Products = Products,
        Groups = Groups,
    };
}

public static class SavedLibraryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>
/// Single source of truth for products and groups saved from the CLI and the
/// web interface. Every mutation is serialized with a cross-process lock and
/// written with an atomic rename.
/// </summary>
public sealed class SavedLibraryStore
{
    public const int CurrentSchema = 1;
    private const int LockAttempts = 100;
    private const int LockDelayMilliseconds = 25;

    private readonly string _path;
    private readonly string _lockPath;
    private readonly string? _legacyUiStatePath;

    public SavedLibraryStore(
        string? path = null,
        string? legacyUiStatePath = null,
        string? lockPath = null)
    {
        var usesDefaultPath = path is null;
        _path = Path.GetFullPath(path ?? UserStatePaths.LibraryPath());
        _lockPath = Path.GetFullPath(lockPath ?? $"{_path}.lock");
        _legacyUiStatePath = legacyUiStatePath
            ?? (usesDefaultPath
                ? Path.Combine(
                    UserStatePaths.Root(),
                    "web",
                    "ui-state.json")
                : null);
    }

    public string FilePath => _path;

    public string LockPath => _lockPath;

    public SavedLibraryDocument Read()
    {
        using var libraryLock = AcquireLock();
        return ReadUnlocked();
    }

    public SavedLibraryDocument EnsureMigrated()
    {
        using var libraryLock = AcquireLock();
        return ReadUnlocked();
    }

    public SavedLibraryDocument AddOrUpdateProducts(
        IEnumerable<SavedProduct> products)
    {
        var candidates = products
            .Where(product => product is not null)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new SavedLibraryException("No products were provided.");
        }

        return Mutate(library =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var candidate in candidates)
            {
                var existing = library.Products.FirstOrDefault(product =>
                    string.Equals(
                        product.Id,
                        CandidateId(candidate),
                        StringComparison.Ordinal));
                if (existing is not null)
                {
                    candidate.SavedAt = existing.SavedAt;
                    candidate.PriceAtSave = existing.PriceAtSave
                        ?? candidate.PriceAtSave;
                    if (candidate.LastCheckedAt is null
                        || candidate.LastCheckedAt < existing.LastCheckedAt)
                    {
                        candidate.LastCheckedAt = existing.LastCheckedAt;
                    }
                }
                candidate.UpdatedAt = now;
                var normalized = SavedLibraryNormalizer.NormalizeProduct(
                    candidate,
                    strict: true)!;
                var index = library.Products.FindIndex(product =>
                    string.Equals(product.Id, normalized.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    library.Products[index] = normalized;
                }
                else
                {
                    library.Products.Add(normalized);
                }
            }
            return true;
        });
    }

    public SavedLibraryDocument EnsureProducts(
        IEnumerable<SavedProduct> products)
    {
        var candidates = products
            .Where(product => product is not null)
            .ToList();
        if (candidates.Count == 0)
        {
            return Read();
        }

        return Mutate(library =>
        {
            var changed = false;
            foreach (var candidate in candidates)
            {
                var normalized = SavedLibraryNormalizer.NormalizeProduct(
                    candidate,
                    strict: true)!;
                if (library.Products.Any(product => string.Equals(
                        product.Id,
                        normalized.Id,
                        StringComparison.Ordinal)))
                {
                    continue;
                }
                library.Products.Add(normalized);
                changed = true;
            }
            return changed;
        });
    }

    /// <summary>
    /// Updates a saved product from a verified product detail. Only existing
    /// products are touched; the original save time and save price are kept.
    /// </summary>
    public bool TrySyncProductDetails(
        SavedProduct details,
        DateTimeOffset? checkedAt = null)
    {
        if (details is null)
        {
            return false;
        }

        var check = checkedAt
            ?? details.LastCheckedAt
            ?? DateTimeOffset.UtcNow;
        var normalizedDetails = SavedLibraryNormalizer.NormalizeProduct(
            details,
            strict: false);
        if (normalizedDetails is null)
        {
            return false;
        }
        var changed = false;
        Mutate(library =>
        {
            var existing = library.Products.FirstOrDefault(product =>
                string.Equals(
                    product.Id,
                    normalizedDetails.Id,
                    StringComparison.Ordinal));
            if (existing is null)
            {
                return false;
            }
            if (existing.LastCheckedAt is { } previous && previous > check)
            {
                return false;
            }
            if (check < existing.SavedAt)
            {
                return false;
            }

            var nextPrice = normalizedDetails.PriceLast ?? existing.PriceLast;
            var nextCurrency = string.IsNullOrWhiteSpace(
                normalizedDetails.Currency)
                ? existing.Currency
                : normalizedDetails.Currency;
            var nextSnapshot = normalizedDetails.Snapshot ?? existing.Snapshot;
            if (existing.LastCheckedAt == check
                && existing.PriceLast == nextPrice
                && string.Equals(
                    existing.Currency,
                    nextCurrency,
                    StringComparison.Ordinal)
                && string.Equals(
                    JsonSerializer.Serialize(
                        existing.Snapshot,
                        SavedLibraryJson.Options),
                    JsonSerializer.Serialize(
                        nextSnapshot,
                        SavedLibraryJson.Options),
                    StringComparison.Ordinal))
            {
                return false;
            }

            existing.PriceLast = nextPrice;
            existing.Currency = nextCurrency;
            existing.Snapshot = nextSnapshot;
            existing.LastCheckedAt = check;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            var normalized = SavedLibraryNormalizer.NormalizeProduct(
                existing,
                strict: true)!;
            existing.PriceLast = normalized.PriceLast;
            existing.Currency = normalized.Currency;
            existing.Snapshot = normalized.Snapshot;
            existing.LastCheckedAt = normalized.LastCheckedAt;
            existing.UpdatedAt = normalized.UpdatedAt;
            changed = true;
            return true;
        });
        return changed;
    }

    public SavedLibraryDocument UpdateProduct(
        string productId,
        SavedProduct update)
    {
        return Mutate(library =>
        {
            var existing = library.Products.FirstOrDefault(product =>
                string.Equals(product.Id, productId, StringComparison.Ordinal));
            if (existing is null)
            {
                throw new SavedLibraryNotFoundException(
                    $"Saved product not found: {productId}.");
            }

            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.PriceLast = update.PriceLast ?? existing.PriceLast;
            existing.Currency = string.IsNullOrWhiteSpace(update.Currency)
                ? existing.Currency
                : update.Currency.Trim().ToUpperInvariant();
            existing.Snapshot = update.Snapshot ?? existing.Snapshot;
            var normalized = SavedLibraryNormalizer.NormalizeProduct(
                existing,
                strict: true)!;
            existing.PriceLast = normalized.PriceLast;
            existing.Currency = normalized.Currency;
            existing.Snapshot = normalized.Snapshot;
            return true;
        });
    }

    public SavedLibraryDocument RemoveProduct(string productId)
    {
        return Mutate(library =>
        {
            var removed = library.Products.RemoveAll(product =>
                string.Equals(product.Id, productId, StringComparison.Ordinal)) > 0;
            if (!removed)
            {
                return false;
            }
            foreach (var group in library.Groups)
            {
                group.Items.RemoveAll(item =>
                    string.Equals(
                        item.ProductId,
                        productId,
                        StringComparison.Ordinal));
            }
            return true;
        });
    }

    public SavedLibraryDocument CreateGroup(SavedGroup group)
    {
        return Mutate(library =>
        {
            var productIds = library.Products
                .Select(product => product.Id)
                .ToHashSet(StringComparer.Ordinal);
            var normalized = SavedLibraryNormalizer.NormalizeGroup(
                group,
                productIds,
                strict: true)!;
            if (library.Groups.Any(existing =>
                    string.Equals(
                        existing.Id,
                        normalized.Id,
                        StringComparison.Ordinal)))
            {
                throw new SavedLibraryException(
                    $"A group with id '{normalized.Id}' already exists.");
            }
            normalized.CreatedAt = DateTimeOffset.UtcNow;
            normalized.UpdatedAt = normalized.CreatedAt;
            library.Groups.Add(normalized);
            return true;
        });
    }

    public SavedLibraryDocument UpdateGroup(
        string groupId,
        string name,
        string accent,
        string? description = null)
    {
        return Mutate(library =>
        {
            var group = library.Groups.FirstOrDefault(item =>
                string.Equals(item.Id, groupId, StringComparison.Ordinal));
            if (group is null)
            {
                throw new SavedLibraryNotFoundException(
                    $"Saved group not found: {groupId}.");
            }
            group.Name = name;
            group.Accent = SavedLibraryNormalizer.NormalizeAccent(accent);
            if (description is not null)
            {
                group.Description = description;
            }
            group.UpdatedAt = DateTimeOffset.UtcNow;
            var productIds = library.Products
                .Select(product => product.Id)
                .ToHashSet(StringComparer.Ordinal);
            var normalized = SavedLibraryNormalizer.NormalizeGroup(
                group,
                productIds,
                strict: true)!;
            group.Name = normalized.Name;
            group.Accent = normalized.Accent;
            group.Description = normalized.Description;
            group.UpdatedAt = normalized.UpdatedAt;
            return true;
        });
    }

    public SavedLibraryDocument DeleteGroup(string groupId)
    {
        return Mutate(library => library.Groups.RemoveAll(group =>
            string.Equals(group.Id, groupId, StringComparison.Ordinal)) > 0);
    }

    public SavedLibraryDocument AddGroupItems(
        string groupId,
        IEnumerable<SavedGroupItem> items)
    {
        var requested = items
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ProductId))
            .GroupBy(item => item.ProductId, StringComparer.Ordinal)
            .Select(group => new SavedGroupItem
            {
                ProductId = group.Key,
                Quantity = Math.Clamp(
                    group.Max(item => Math.Max(1, item.Quantity)),
                    1,
                    99),
            })
            .ToList();
        if (requested.Count == 0)
        {
            throw new SavedLibraryException("No products were provided.");
        }

        return Mutate(library =>
        {
            var group = library.Groups.FirstOrDefault(item =>
                string.Equals(item.Id, groupId, StringComparison.Ordinal));
            if (group is null)
            {
                throw new SavedLibraryNotFoundException(
                    $"Saved group not found: {groupId}.");
            }
            var productIds = library.Products
                .Select(product => product.Id)
                .ToHashSet(StringComparer.Ordinal);
            var missing = requested
                .Select(item => item.ProductId)
                .FirstOrDefault(productId => !productIds.Contains(productId));
            if (missing is not null)
            {
                throw new SavedLibraryNotFoundException(
                    $"Saved product not found: {missing}. " +
                    "Add it to the library first.");
            }

            foreach (var item in requested)
            {
                var existing = group.Items.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.ProductId,
                        item.ProductId,
                        StringComparison.Ordinal));
                if (existing is null)
                {
                    if (group.Items.Count >= SavedLibraryNormalizer.MaxItemsPerGroup)
                    {
                        throw new SavedLibraryException(
                            $"A group can contain at most " +
                            $"{SavedLibraryNormalizer.MaxItemsPerGroup} products.");
                    }
                    group.Items.Add(item);
                }
                else
                {
                    existing.Quantity = Math.Max(
                        existing.Quantity,
                        item.Quantity);
                }
            }
            group.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        });
    }

    public SavedLibraryDocument RemoveGroupItem(
        string groupId,
        string productId)
    {
        return Mutate(library =>
        {
            var group = library.Groups.FirstOrDefault(item =>
                string.Equals(item.Id, groupId, StringComparison.Ordinal));
            if (group is null)
            {
                throw new SavedLibraryNotFoundException(
                    $"Saved group not found: {groupId}.");
            }
            var removed = group.Items.RemoveAll(item =>
                string.Equals(
                    item.ProductId,
                    productId,
                    StringComparison.Ordinal)) > 0;
            if (removed)
            {
                group.UpdatedAt = DateTimeOffset.UtcNow;
            }
            return removed;
        });
    }

    private SavedLibraryDocument Mutate(Func<SavedLibrary, bool> mutation)
    {
        using var libraryLock = AcquireLock();
        var document = ReadUnlocked();
        var library = document.ToLibrary();
        if (!mutation(library))
        {
            return document;
        }

        var normalized = SavedLibraryNormalizer.Normalize(library, strict: true);
        document.Products = normalized.Products;
        document.Groups = normalized.Groups;
        document.Revision++;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        Write(document);
        return document;
    }

    private SavedLibraryDocument ReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            var migrated = TryMigrateLegacy();
            if (migrated is not null)
            {
                Write(migrated);
                return migrated;
            }
            return new SavedLibraryDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<SavedLibraryDocument>(
                File.ReadAllText(_path),
                SavedLibraryJson.Options) ?? new SavedLibraryDocument();
            if (document.Schema > CurrentSchema)
            {
                throw new SavedLibraryException(
                    $"Library schema {document.Schema} is newer than the " +
                    $"supported schema {CurrentSchema}.");
            }
            document.Schema = CurrentSchema;
            document.Revision = Math.Max(0, document.Revision);
            var normalized = SavedLibraryNormalizer.Normalize(
                document.ToLibrary(),
                strict: false);
            document.Products = normalized.Products;
            document.Groups = normalized.Groups;
            return document;
        }
        catch (SavedLibraryException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            throw new SavedLibraryException(
                $"Unable to read the saved products library '{_path}': " +
                $"{ex.Message}. The file was not modified.",
                ex);
        }
    }

    private SavedLibraryDocument? TryMigrateLegacy()
    {
        if (string.IsNullOrWhiteSpace(_legacyUiStatePath)
            || !File.Exists(_legacyUiStatePath))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(
                File.ReadAllText(_legacyUiStatePath));
            if (!json.RootElement.TryGetProperty("library", out var libraryNode)
                || libraryNode.ValueKind is JsonValueKind.Null
                    or JsonValueKind.Undefined)
            {
                return null;
            }

            var legacy = libraryNode.Deserialize<SavedLibrary>(
                SavedLibraryJson.Options) ?? new SavedLibrary();
            var normalized = SavedLibraryNormalizer.Normalize(
                legacy,
                strict: false);
            if (normalized.Products.Count == 0 && normalized.Groups.Count == 0)
            {
                return null;
            }

            var backup = $"{_legacyUiStatePath}.pre-library.bak";
            if (!File.Exists(backup))
            {
                File.Copy(_legacyUiStatePath, backup);
                TrySetUnixMode(
                    backup,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return new SavedLibraryDocument
            {
                Schema = CurrentSchema,
                Revision = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                Products = normalized.Products,
                Groups = normalized.Groups,
            };
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            throw new SavedLibraryException(
                $"Unable to migrate the saved products from " +
                $"'{_legacyUiStatePath}': {ex.Message}",
                ex);
        }
    }

    private void Write(SavedLibraryDocument document)
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
                JsonSerializer.Serialize(document, SavedLibraryJson.Options),
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
            throw new SavedLibraryException(
                $"Unable to write the saved products library '{_path}': " +
                $"{ex.Message}. The previous file was left unchanged.",
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
                throw new SavedLibraryException(
                    $"The saved products library is busy: {_lockPath}",
                    ex);
            }
        }

        throw new SavedLibraryException(
            $"The saved products library is busy: {_lockPath}");
    }

    private static string CandidateId(SavedProduct product) =>
        $"{product.Domain.Trim().ToLowerInvariant()}:" +
        $"{product.Asin.Trim().ToUpperInvariant()}";

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

public sealed class SavedLibraryNotFoundException(string message)
    : SavedLibraryException(message);
