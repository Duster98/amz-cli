using System.Text.Json.Nodes;
using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.Tests;

public class WebUiStateTests
{
    [Fact]
    public void Load_creates_the_state_file_with_defaults()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "web", "ui-state.json");
        var store = new WebUiStateStore(path);

        var state = store.Load();

        Assert.True(File.Exists(path));
        Assert.Equal(4, state.Schema);
        Assert.Equal("system", state.Preferences.Theme);
        Assert.Equal("en", state.Preferences.Language);
        Assert.Equal(string.Empty, state.Preferences.DeliveryCountry);
        Assert.Equal(string.Empty, state.Preferences.DeliveryPostalCode);
        Assert.Equal("it", state.Preferences.Domain);
        Assert.Equal(new[] { "it" }, state.Preferences.Domains);
        Assert.Equal("it", state.Preferences.DealsDomain);
        Assert.Equal("list", state.Preferences.ResultsLayout);
        Assert.Equal("grid", state.Preferences.MonitoringLayout);
        Assert.True(state.Preferences.Filters.NoAds);
        Assert.True(state.Preferences.Filters.Delivery);
        Assert.False(state.Preferences.Filters.Enrich);
        Assert.Equal(0, state.Preferences.Filters.MinRating);
        Assert.Null(state.Preferences.Filters.PriceMin);
        Assert.Null(state.Preferences.Filters.PriceMax);
        Assert.False(state.Preferences.Filters.OnlyDeals);
        Assert.False(state.Preferences.Filters.FreeShipping);
        Assert.False(state.Preferences.Filters.AmazonChoice);
        Assert.Equal("relevance", state.Preferences.Filters.Sort);
        Assert.Empty(state.RecentSearches);
        Assert.Empty(state.SavedSearches);
        Assert.Empty(state.Comparisons);
    }

    [Fact]
    public void Preferences_are_normalized_and_persisted()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "ui-state.json");
        var store = new WebUiStateStore(path);

        var updated = store.UpdatePreferences(new WebUiPreferences
        {
            Theme = "DARK",
            Language = "DE",
            DeliveryCountry = "it",
            DeliveryPostalCode = "20121",
            Domain = "de",
            Domains = ["de", "fr", "not-a-marketplace", "de"],
            DealsDomain = "ES",
            RailPinned = true,
            ResultsLayout = "GRID",
            MonitoringLayout = "LIST",
            Filters = new WebSearchFilters
            {
                NoAds = false,
                Delivery = false,
                Enrich = true,
                MinRating = 4.5,
                PriceMax = 100,
                PriceMin = 250,
                OnlyDeals = true,
                FreeShipping = true,
                AmazonChoice = true,
                Sort = "PRICE-ASC",
            },
        });

        var reloaded = new WebUiStateStore(path).Load();

        Assert.Equal("dark", updated.Preferences.Theme);
        Assert.Equal("de", updated.Preferences.Language);
        Assert.Equal("dark", reloaded.Preferences.Theme);
        Assert.Equal("de", reloaded.Preferences.Language);
        Assert.Equal("IT", reloaded.Preferences.DeliveryCountry);
        Assert.Equal("20121", reloaded.Preferences.DeliveryPostalCode);
        Assert.Equal("de", reloaded.Preferences.Domain);
        Assert.Equal(new[] { "de", "fr" }, reloaded.Preferences.Domains);
        Assert.Equal("es", reloaded.Preferences.DealsDomain);
        Assert.True(reloaded.Preferences.RailPinned);
        Assert.Equal("grid", reloaded.Preferences.ResultsLayout);
        Assert.Equal("list", reloaded.Preferences.MonitoringLayout);
        Assert.False(reloaded.Preferences.Filters.NoAds);
        Assert.False(reloaded.Preferences.Filters.Delivery);
        Assert.True(reloaded.Preferences.Filters.Enrich);
        Assert.Equal(4.5, reloaded.Preferences.Filters.MinRating);
        Assert.Equal(100, reloaded.Preferences.Filters.PriceMin);
        Assert.Equal(250, reloaded.Preferences.Filters.PriceMax);
        Assert.True(reloaded.Preferences.Filters.OnlyDeals);
        Assert.True(reloaded.Preferences.Filters.FreeShipping);
        Assert.True(reloaded.Preferences.Filters.AmazonChoice);
        Assert.Equal("price-asc", reloaded.Preferences.Filters.Sort);
    }

    [Fact]
    public void Country_only_preferences_are_preserved_for_us_and_jp()
    {
        using var directory = new TempDirectory();
        var store = new WebUiStateStore(Path.Combine(directory.Root, "ui-state.json"));

        var us = store.UpdatePreferences(new WebUiPreferences
        {
            Domain = "com",
            Domains = ["com"],
            DeliveryCountry = "IT",
            DeliveryPostalCode = string.Empty,
        });
        Assert.Equal("IT", us.Preferences.DeliveryCountry);
        Assert.Equal(string.Empty, us.Preferences.DeliveryPostalCode);

        var jp = store.UpdatePreferences(new WebUiPreferences
        {
            Domain = "jp",
            Domains = ["jp"],
            DeliveryCountry = "DE",
            DeliveryPostalCode = string.Empty,
        });
        Assert.Equal("DE", jp.Preferences.DeliveryCountry);
        Assert.Equal(string.Empty, jp.Preferences.DeliveryPostalCode);

        var it = store.UpdatePreferences(new WebUiPreferences
        {
            Domain = "it",
            Domains = ["it"],
            DeliveryCountry = "IT",
            DeliveryPostalCode = string.Empty,
        });
        Assert.Equal(string.Empty, it.Preferences.DeliveryCountry);
        Assert.Equal(string.Empty, it.Preferences.DeliveryPostalCode);
    }

    [Fact]
    public void Invalid_preferences_fall_back_to_safe_values()
    {
        using var directory = new TempDirectory();
        var store = new WebUiStateStore(Path.Combine(directory.Root, "ui-state.json"));

        var state = store.UpdatePreferences(new WebUiPreferences
        {
            Theme = "neon",
            Language = "xx",
            DeliveryCountry = "IT",
            DeliveryPostalCode = "bad",
            Domain = "not-a-marketplace",
            Domains = ["not-a-marketplace"],
            DealsDomain = "not-a-marketplace",
            ResultsLayout = "cards",
            MonitoringLayout = "cards",
            Filters = new WebSearchFilters
            {
                MinRating = 2.5,
                PriceMin = -10,
                PriceMax = 2_000_000,
                Sort = "bogus",
            },
        });

        Assert.Equal("system", state.Preferences.Theme);
        Assert.Equal("en", state.Preferences.Language);
        Assert.Equal(string.Empty, state.Preferences.DeliveryCountry);
        Assert.Equal(string.Empty, state.Preferences.DeliveryPostalCode);
        Assert.Equal("it", state.Preferences.Domain);
        Assert.Equal(new[] { "it" }, state.Preferences.Domains);
        Assert.Equal("it", state.Preferences.DealsDomain);
        Assert.Equal("list", state.Preferences.ResultsLayout);
        Assert.Equal("grid", state.Preferences.MonitoringLayout);
        Assert.Equal(0, state.Preferences.Filters.MinRating);
        Assert.Null(state.Preferences.Filters.PriceMin);
        Assert.Null(state.Preferences.Filters.PriceMax);
        Assert.Equal("relevance", state.Preferences.Filters.Sort);
    }

    [Fact]
    public void Old_schema_files_get_the_default_language()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "ui-state.json");
        File.WriteAllText(
            path,
            """
            {
              "schema": 1,
              "preferences": {
                "theme": "dark",
                "domain": "de"
              }
            }
            """);

        var state = new WebUiStateStore(path).Load();

        Assert.Equal(4, state.Schema);
        Assert.Equal("en", state.Preferences.Language);
        Assert.Equal("dark", state.Preferences.Theme);
        Assert.Equal("de", state.Preferences.Domain);
    }

    [Fact]
    public void Schema_3_library_is_removed_from_the_ui_state()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "ui-state.json");
        File.WriteAllText(
            path,
            """
            {
              "schema": 3,
              "preferences": { "theme": "dark", "language": "it" },
              "library": {
                "products": [
                  { "asin": "B0H25NG1JK", "domain": "it" }
                ],
                "groups": []
              }
            }
            """);

        var state = new WebUiStateStore(path).Load();
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.Equal(4, state.Schema);
        Assert.False(root.ContainsKey("library"));
        Assert.Equal("it", state.Preferences.Language);
    }

    [Fact]
    public void Recent_searches_are_deduplicated_ordered_and_limited()
    {
        using var directory = new TempDirectory();
        var store = new WebUiStateStore(Path.Combine(directory.Root, "ui-state.json"));
        var now = DateTimeOffset.UtcNow;

        var state = store.UpdateRecentSearches(
        [
            new WebRecentSearch
            {
                Query = "zaino",
                Domain = "it",
                Marketplace = "Amazon.it",
                At = now.AddMinutes(-1),
            },
            new WebRecentSearch
            {
                Query = "ZAINO",
                Domain = "it",
                Marketplace = "Amazon.it",
                At = now,
            },
            new WebRecentSearch
            {
                Query = "cuffie",
                Domain = "de",
                Marketplace = "Amazon.de",
                At = now.AddMinutes(-2),
            },
            new WebRecentSearch
            {
                Query = "ssd",
                Domain = "com",
                Marketplace = "Amazon.com",
                At = now.AddMinutes(-3),
            },
            new WebRecentSearch
            {
                Query = "kindle",
                Domain = "fr",
                Marketplace = "Amazon.fr",
                At = now.AddMinutes(-4),
            },
            new WebRecentSearch
            {
                Query = "mouse",
                Domain = "es",
                Marketplace = "Amazon.es",
                At = now.AddMinutes(-5),
            },
            new WebRecentSearch
            {
                Query = "tastiera",
                Domain = "uk",
                Marketplace = "Amazon.co.uk",
                At = now.AddMinutes(-6),
            },
        ]);

        Assert.Equal(5, state.RecentSearches.Count);
        Assert.Equal("ZAINO", state.RecentSearches[0].Query);
        Assert.Equal("cuffie", state.RecentSearches[1].Query);
        Assert.Single(
            state.RecentSearches,
            item => item.Query.Equals("zaino", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recent_searches_keep_valid_marketplace_scopes()
    {
        using var directory = new TempDirectory();
        var store = new WebUiStateStore(
            Path.Combine(directory.Root, "ui-state.json"));

        var state = store.UpdateRecentSearches(
        [
            new WebRecentSearch
            {
                Query = "cuffie",
                Domain = "de",
                Domains = ["de", "fr", "de", "bogus"],
                Marketplace = "Amazon.de +1",
                At = DateTimeOffset.UtcNow,
            },
        ]);

        var item = Assert.Single(state.RecentSearches);
        Assert.Equal("de", item.Domain);
        Assert.Equal(new[] { "de", "fr" }, item.Domains);
    }

    [Fact]
    public void Reserved_future_sections_survive_preference_updates()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "ui-state.json");
        var store = new WebUiStateStore(path);
        store.Load();

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["saved_searches"] = new JsonArray(
            new JsonObject { ["label"] = "Preferiti" });
        root["comparisons"] = new JsonArray(
            new JsonObject { ["label"] = "Confronto" });
        root["settings"] = new JsonObject { ["future"] = true };
        File.WriteAllText(path, root.ToJsonString());

        store.UpdatePreferences(new WebUiPreferences { Theme = "light" });
        var reloaded = store.Load();

        Assert.Single(reloaded.SavedSearches);
        Assert.Single(reloaded.Comparisons);
        Assert.True(reloaded.Settings["future"]!.GetValue<bool>());
    }
}
