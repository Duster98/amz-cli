using System.Text.Json.Nodes;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Library;

namespace AmazonScraper.Cli.Tests;

public class ProductCacheStoreTests
{
    [Fact]
    public void Product_key_is_normalized_and_shared_by_domain_and_asin()
    {
        Assert.Equal(
            "it-B0H25NG1JK",
            ProductCacheStore.Key(" IT ", " b0h25ng1jk "));
        Assert.NotEqual(
            ProductCacheStore.Key("it", "B0H25NG1JK"),
            ProductCacheStore.Key("de", "B0H25NG1JK"));
    }

    [Fact]
    public void Product_cache_roundtrip_and_clear()
    {
        using var directory = new TempDirectory();
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            Path.Combine(directory.Root, "cache-settings.json"));
        cache.Clear();
        var key = ProductCacheStore.Key("it", "B0H25NG1JK");
        cache.Save(
            key,
            JsonNode.Parse(
                """{"product":{"asin":"B0H25NG1JK","title":"ASUS"}}""")!,
            new ProductCacheContext { Country = "IT" });

        Assert.True(cache.Has(key));
        var cached = cache.Read(key);
        Assert.NotNull(cached);
        Assert.Equal(
            "ASUS",
            cached!["result"]!["product"]!["title"]!.GetValue<string>());
        Assert.Equal("IT", cached["context"]!["country"]!.GetValue<string>());

        cache.Clear();
        Assert.False(cache.Has(key));
    }

    [Fact]
    public void Least_recent_product_is_evicted_at_the_configured_limit()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Root, "cache-settings.json");
        File.WriteAllText(
            configPath,
            """{"schema":1,"product_cache_max_entries":2}""");
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            configPath);
        var first = ProductCacheStore.Key("it", "FIRST");
        var second = ProductCacheStore.Key("it", "SECOND");
        var third = ProductCacheStore.Key("it", "THIRD");

        cache.Save(first, JsonNode.Parse("""{"product":{}}""")!);
        cache.Save(second, JsonNode.Parse("""{"product":{}}""")!);
        File.SetLastWriteTimeUtc(
            Path.Combine(cache.DirectoryPath, $"{first}.json"),
            DateTime.UtcNow.AddMinutes(-5));
        cache.Save(third, JsonNode.Parse("""{"product":{}}""")!);

        Assert.False(cache.Has(first));
        Assert.True(cache.Has(second));
        Assert.True(cache.Has(third));
    }

    [Fact]
    public void Reading_a_product_refreshes_its_lru_recency()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Root, "cache-settings.json");
        File.WriteAllText(
            configPath,
            """{"schema":1,"product_cache_max_entries":2}""");
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            configPath);
        var first = ProductCacheStore.Key("it", "FIRST");
        var second = ProductCacheStore.Key("it", "SECOND");
        var third = ProductCacheStore.Key("it", "THIRD");

        cache.Save(first, JsonNode.Parse("""{"product":{}}""")!);
        cache.Save(second, JsonNode.Parse("""{"product":{}}""")!);
        File.SetLastWriteTimeUtc(
            Path.Combine(cache.DirectoryPath, $"{first}.json"),
            DateTime.UtcNow.AddMinutes(-5));
        Assert.NotNull(cache.Read(first));
        cache.Save(third, JsonNode.Parse("""{"product":{}}""")!);

        Assert.True(cache.Has(first));
        Assert.False(cache.Has(second));
        Assert.True(cache.Has(third));
    }
}


public class ProductCachePolicyTests
{
    [Fact]
    public void Config_defaults_and_new_fields_are_normalized()
    {
        var defaults = ProductCacheConfig.Load(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json"));
        Assert.Equal(500, defaults.MaxEntries);
        Assert.Equal(30, defaults.ProductCacheTtlMinutes);
        Assert.Equal(7 * 24 * 60, defaults.SavedAutoRefreshAfterMinutes);
        Assert.Equal(24 * 60, defaults.SavedGroupRefreshCooldownMinutes);
        Assert.Equal(6, defaults.SavedGroupRefreshDelaySeconds);
    }

    [Fact]
    public void Existing_cache_config_receives_new_defaults()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "cache-settings.json");
        File.WriteAllText(
            path,
            """{"schema":1,"product_cache_max_entries":12}""");
        _ = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            path);
        var config = ProductCacheConfig.Load(path);
        Assert.Equal(12, config.MaxEntries);
        Assert.Equal(30, config.ProductCacheTtlMinutes);
        Assert.Equal(7 * 24 * 60, config.SavedAutoRefreshAfterMinutes);
        Assert.Equal(24 * 60, config.SavedGroupRefreshCooldownMinutes);
        Assert.Equal(6, config.SavedGroupRefreshDelaySeconds);
    }

    [Fact]
    public void Cache_context_matches_delivery_and_requested_options()
    {
        var cached = new ProductCacheContext
        {
            Rating = true,
            Reviews = true,
            Options = true,
            Country = "IT",
            PostalCode = "00100",
        };
        Assert.True(ProductCacheStore.ContextMatches(
            cached,
            new ProductCacheContext
            {
                Country = "it",
                PostalCode = "00100",
                Options = true,
            }));
        Assert.False(ProductCacheStore.ContextMatches(
            cached,
            new ProductCacheContext
            {
                Country = "DE",
                PostalCode = "00100",
            }));
        Assert.False(ProductCacheStore.ContextMatches(
            new ProductCacheContext { Options = false },
            new ProductCacheContext { Options = true }));
        Assert.True(ProductCacheStore.ContextMatches(
            cached,
            new ProductCacheContext
            {
                Country = "IT",
                PostalCode = "00100",
                Options = false,
            }));
    }

    [Fact]
    public void Fresh_cache_read_honors_ttl_and_delivery_context()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Root, "cache-settings.json");
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            configPath);
        var key = ProductCacheStore.Key("it", "B0H25NG1JK");
        cache.Save(
            key,
            JsonNode.Parse("""{"product":{"asin":"B0H25NG1JK"}}""")!,
            new ProductCacheContext { Country = "IT", PostalCode = "00100" });
        var config = cache.LoadConfig();
        var requested = new ProductCacheContext
        {
            Country = "IT",
            PostalCode = "00100",
        };

        Assert.NotNull(cache.ReadFresh(key, requested, config));
        Assert.Null(cache.ReadFresh(
            key,
            new ProductCacheContext { Country = "DE" },
            config));

        File.WriteAllText(
            configPath,
            """{"schema":1,"product_cache_max_entries":500,"product_cache_ttl_minutes":1}""");
        var path = Path.Combine(cache.DirectoryPath, $"{key}.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["cached_at"] = DateTimeOffset.UtcNow
            .AddMinutes(-2)
            .ToString("O");
        File.WriteAllText(path, document.ToJsonString());
        Assert.Null(cache.ReadFresh(key, requested, cache.LoadConfig()));
    }

    [Fact]
    public void Long_cache_ttl_accepts_an_eight_day_entry()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Root, "cache-settings.json");
        File.WriteAllText(
            configPath,
            """{"schema":1,"product_cache_max_entries":500,"product_cache_ttl_minutes":20160}""");
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            configPath);
        var key = ProductCacheStore.Key("it", "B0H25NG1JK");
        cache.Save(
            key,
            JsonNode.Parse("""{"product":{"asin":"B0H25NG1JK"}}""")!);
        var path = Path.Combine(cache.DirectoryPath, $"{key}.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["cached_at"] = DateTimeOffset.UtcNow
            .AddDays(-8)
            .ToString("O");
        File.WriteAllText(path, document.ToJsonString());

        Assert.NotNull(cache.ReadFresh(
            key,
            new ProductCacheContext(),
            cache.LoadConfig()));
    }

    [Fact]
    public void Cache_roundtrips_the_saved_product_snapshot()
    {
        using var directory = new TempDirectory();
        var cache = new ProductCacheStore(
            Path.Combine(directory.Root, "products"),
            Path.Combine(directory.Root, "cache-settings.json"));
        var checkedAt = DateTimeOffset.UtcNow;
        var saved = new SavedProduct
        {
            Id = "it:B0H25NG1JK",
            Asin = "B0H25NG1JK",
            Domain = "it",
            SavedAt = checkedAt.AddDays(-10),
            UpdatedAt = checkedAt,
            LastCheckedAt = checkedAt,
            PriceAtSave = 699,
            PriceLast = 679,
            Currency = "EUR",
            Snapshot = new SavedSnapshot
            {
                Title = "ASUS Vivobook",
                Price = "679,00 €",
                PriceAmount = 679,
                Currency = "EUR",
            },
        };
        cache.Save(
            ProductCacheStore.Key("it", "B0H25NG1JK"),
            JsonNode.Parse("""{"product":{"asin":"B0H25NG1JK"}}""")!,
            new ProductCacheContext { Country = "IT" },
            saved);

        var document = cache.Read(
            ProductCacheStore.Key("it", "B0H25NG1JK"));
        var restored = ProductCacheStore.ReadSavedProduct(document!);
        Assert.NotNull(restored);
        Assert.Equal("it:B0H25NG1JK", restored!.Id);
        Assert.Equal(679, restored.PriceLast);
        Assert.Equal(checkedAt, restored.LastCheckedAt);
        Assert.Equal("ASUS Vivobook", restored.Snapshot.Title);
    }
}
