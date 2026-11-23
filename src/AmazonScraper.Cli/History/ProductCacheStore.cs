using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.History;

public sealed class ProductCacheException : Exception
{
    public ProductCacheException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed record ProductCacheHit(
    JsonObject Document,
    DateTimeOffset CachedAt);

public sealed class ProductCacheContext
{
    [JsonPropertyName("rating")]
    public bool Rating { get; set; } = true;

    [JsonPropertyName("reviews")]
    public bool Reviews { get; set; } = true;

    [JsonPropertyName("options")]
    public bool Options { get; set; } = true;

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }
}

public sealed class ProductCacheConfig
{
    public const int DefaultMaxEntries = 500;
    public const int DefaultTtlMinutes = 30;
    public const int DefaultSavedAutoRefreshMinutes = 7 * 24 * 60;
    public const int DefaultSavedGroupRefreshCooldownMinutes = 24 * 60;
    public const int DefaultSavedGroupRefreshDelaySeconds = 6;
    private const int MinMaxEntries = 1;
    private const int MaxMaxEntries = 100_000;

    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("product_cache_max_entries")]
    public int MaxEntries { get; set; } = DefaultMaxEntries;

    [JsonPropertyName("product_cache_ttl_minutes")]
    public int ProductCacheTtlMinutes { get; set; } = DefaultTtlMinutes;

    [JsonPropertyName("saved_auto_refresh_after_minutes")]
    public int SavedAutoRefreshAfterMinutes { get; set; } =
        DefaultSavedAutoRefreshMinutes;

    [JsonPropertyName("saved_group_refresh_cooldown_minutes")]
    public int SavedGroupRefreshCooldownMinutes { get; set; } =
        DefaultSavedGroupRefreshCooldownMinutes;

    [JsonPropertyName("saved_group_refresh_delay_seconds")]
    public int SavedGroupRefreshDelaySeconds { get; set; } =
        DefaultSavedGroupRefreshDelaySeconds;

    public static string DefaultPath() => WebPaths.CacheSettingsPath();

    public static void EnsureDefault(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(path))
            {
                EnsureNewFields(path);
                return;
            }
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    new ProductCacheConfig(),
                    JsonOptions));
        }
        catch (Exception)
        {
            // A missing or read-only configuration falls back to defaults.
        }
    }

    private static void EnsureNewFields(string path)
    {
        var defaults = new ProductCacheConfig();
        var node = JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
        if (node is null)
        {
            return;
        }
        var changed = false;
        changed |= AddIfMissing(node, "schema", defaults.Schema);
        changed |= AddIfMissing(
            node,
            "product_cache_ttl_minutes",
            defaults.ProductCacheTtlMinutes);
        changed |= AddIfMissing(
            node,
            "saved_auto_refresh_after_minutes",
            defaults.SavedAutoRefreshAfterMinutes);
        changed |= AddIfMissing(
            node,
            "saved_group_refresh_cooldown_minutes",
            defaults.SavedGroupRefreshCooldownMinutes);
        changed |= AddIfMissing(
            node,
            "saved_group_refresh_delay_seconds",
            defaults.SavedGroupRefreshDelaySeconds);
        if (!changed)
        {
            return;
        }
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            temporary,
            node.ToJsonString(JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool AddIfMissing(
        JsonObject node,
        string name,
        int value)
    {
        if (node.ContainsKey(name))
        {
            return false;
        }
        node[name] = value;
        return true;
    }

    public static ProductCacheConfig Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path))
            {
                return new ProductCacheConfig();
            }
            var config = JsonSerializer.Deserialize<ProductCacheConfig>(
                File.ReadAllText(path),
                JsonOptions) ?? new ProductCacheConfig();
            config.Normalize();
            return config;
        }
        catch (Exception)
        {
            return new ProductCacheConfig();
        }
    }

    public void Normalize()
    {
        if (Schema <= 0)
        {
            Schema = 1;
        }
        MaxEntries = Math.Clamp(MaxEntries, MinMaxEntries, MaxMaxEntries);
        if (ProductCacheTtlMinutes <= 0)
        {
            ProductCacheTtlMinutes = DefaultTtlMinutes;
        }
        ProductCacheTtlMinutes = Math.Clamp(
            ProductCacheTtlMinutes,
            1,
            30 * 24 * 60);
        if (SavedAutoRefreshAfterMinutes <= 0)
        {
            SavedAutoRefreshAfterMinutes = DefaultSavedAutoRefreshMinutes;
        }
        SavedAutoRefreshAfterMinutes = Math.Clamp(
            SavedAutoRefreshAfterMinutes,
            60,
            365 * 24 * 60);
        SavedGroupRefreshCooldownMinutes = Math.Clamp(
            SavedGroupRefreshCooldownMinutes,
            0,
            30 * 24 * 60);
        if (SavedGroupRefreshDelaySeconds <= 0)
        {
            SavedGroupRefreshDelaySeconds =
                DefaultSavedGroupRefreshDelaySeconds;
        }
        SavedGroupRefreshDelaySeconds = Math.Clamp(
            SavedGroupRefreshDelaySeconds,
            6,
            3600);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>
/// Session cache of product details. The key is marketplace + ASIN, shared by
/// Web, CLI, and agents. The cache survives history edits and is cleared only
/// when the Web host starts.
/// </summary>
public sealed class ProductCacheStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;
    private readonly string _lockPath;
    private readonly string _configPath;
    private const int LockAttempts = 100;
    private const int LockDelayMilliseconds = 25;

    public ProductCacheStore(
        string? directory = null,
        string? configPath = null)
    {
        _directory = Path.GetFullPath(
            directory ?? WebPaths.ProductCacheDirectory());
        _lockPath = _directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + ".lock";
        _configPath = Path.GetFullPath(
            configPath ?? ProductCacheConfig.DefaultPath());
        ProductCacheConfig.EnsureDefault(_configPath);
    }

    public string DirectoryPath => _directory;

    public ProductCacheConfig LoadConfig() => ProductCacheConfig.Load(_configPath);

    public static string Key(string domain, string asin)
    {
        var normalizedDomain = string.IsNullOrWhiteSpace(domain)
            ? "unknown"
            : domain.Trim().ToLowerInvariant();
        var normalizedAsin = string.IsNullOrWhiteSpace(asin)
            ? "unknown"
            : asin.Trim().ToUpperInvariant();
        return $"{normalizedDomain}-{normalizedAsin}";
    }

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
            throw new ProductCacheException(
                $"Unable to clear product cache '{_directory}': {ex.Message}");
        }
    }

    public bool Has(string key) => File.Exists(PathFor(key));

    public JsonObject? Read(string key)
    {
        using var gate = AcquireLock();
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            Touch(path);
            return document;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return null;
        }
    }

    public ProductCacheHit? ReadFresh(
        string key,
        ProductCacheContext requested,
        ProductCacheConfig? config = null)
    {
        config ??= LoadConfig();
        var document = Read(key);
        if (document?["result"] is not JsonObject
            || document["cached_at"]?.GetValue<string>() is not { } cachedAtText
            || !DateTimeOffset.TryParse(cachedAtText, out var cachedAt))
        {
            return null;
        }
        if (DateTimeOffset.UtcNow - cachedAt
            > TimeSpan.FromMinutes(config.ProductCacheTtlMinutes))
        {
            return null;
        }
        ProductCacheContext? cachedContext;
        try
        {
            cachedContext = document["context"]?
                .Deserialize<ProductCacheContext>();
        }
        catch (JsonException)
        {
            return null;
        }
        if (!ContextMatches(cachedContext, requested))
        {
            return null;
        }
        return new ProductCacheHit(document, cachedAt);
    }

    public static SavedProduct? ReadSavedProduct(JsonObject document)
    {
        if (document["saved_product"] is not JsonObject node)
        {
            return null;
        }
        try
        {
            return node.Deserialize<SavedProduct>(SavedLibraryJson.Options);
        }
        catch (Exception ex) when (
            ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool ContextMatches(
        ProductCacheContext? cached,
        ProductCacheContext requested)
    {
        cached ??= new ProductCacheContext();
        static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().ToUpperInvariant();

        if (!string.Equals(
                Normalize(cached.Country),
                Normalize(requested.Country),
                StringComparison.Ordinal)
            || !string.Equals(
                Normalize(cached.PostalCode),
                Normalize(requested.PostalCode),
                StringComparison.Ordinal))
        {
            return false;
        }
        return (!requested.Rating || cached.Rating)
            && (!requested.Reviews || cached.Reviews)
            && (!requested.Options || cached.Options);
    }

    public void Save(
        string key,
        JsonNode payload,
        ProductCacheContext? context = null,
        SavedProduct? savedProduct = null)
    {
        using var gate = AcquireLock();
        Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        var document = new JsonObject
        {
            ["cached_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["context"] = JsonSerializer.SerializeToNode(
                context ?? new ProductCacheContext(),
                Options),
            ["result"] = payload.DeepClone(),
        };
        if (savedProduct is not null)
        {
            document["saved_product"] = JsonSerializer.SerializeToNode(
                savedProduct,
                SavedLibraryJson.Options);
        }
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
            TryDelete(temporary);
            throw new ProductCacheException(
                $"Unable to write product cache '{path}': {ex.Message}");
        }

        Prune();
    }

    public void Delete(string key)
    {
        using var gate = AcquireLock();
        TryDelete(PathFor(key));
    }

    private void Prune()
    {
        var config = ProductCacheConfig.Load(_configPath);
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.json")
            .Where(file => !file.Name.StartsWith(".", StringComparison.Ordinal))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var removeCount = files.Count - config.MaxEntries;
        for (var index = 0; index < removeCount; index++)
        {
            TryDelete(files[index].FullName);
        }
    }

    private string PathFor(string key)
    {
        var safe = new string(key
            .Where(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_')
            .ToArray());
        if (safe.Length == 0)
        {
            safe = "product";
        }
        return Path.Combine(_directory, $"{safe}.json");
    }

    private void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception)
        {
            // LRU ordering is best effort.
        }
    }

    private FileStream AcquireLock()
    {
        var directory = Path.GetDirectoryName(_lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (attempt < LockAttempts - 1)
            {
                Thread.Sleep(LockDelayMilliseconds);
            }
        }
        throw new ProductCacheException(
            $"Unable to acquire product cache lock '{_lockPath}'.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
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
