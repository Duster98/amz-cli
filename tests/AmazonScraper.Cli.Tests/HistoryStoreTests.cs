using System.Text.Json;
using System.Text.Json.Nodes;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.History;

namespace AmazonScraper.Cli.Tests;

public class HistoryStoreTests
{
    [Fact]
    public void Search_lifecycle_and_product_entries_are_persisted()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));

        var search = store.StartSearch(new HistorySearchInput(
            "ssd portatile",
            "it",
            ["it"],
            new HistorySearchFilters
            {
                MinRating = 4,
                OnlyDeals = true,
                Sort = "price-asc",
            },
            HistorySource.Agent));
        Assert.Equal(HistoryStatus.Running, search.Status);
        Assert.Equal(HistorySource.Agent, search.Source);
        Assert.True(store.Contains(search.Id));

        var completed = store.CompleteSearch(
            search.Id,
            HistoryStatus.Succeeded,
            42,
            null);
        var savedSearch = Assert.Single(completed.Entries);
        Assert.Equal(HistoryStatus.Succeeded, savedSearch.Status);
        Assert.Equal(42, savedSearch.ResultCount);
        Assert.Equal(4, savedSearch.Filters.MinRating);
        Assert.True(savedSearch.Filters.OnlyDeals);

        Assert.False(store.Contains("h_missing"));

        var product = store.AddProduct(new HistoryProductInput(
            "b0h25ng1jk",
            "it",
            new HistoryProductSnapshot
            {
                Title = "ASUS Vivobook",
                Price = "679,00 €",
                PriceAmount = 679,
                Currency = "EUR",
            },
            HistorySource.Web));

        var reloaded = new HistoryStore(
            Path.Combine(directory.Root, "history.json")).Read();
        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal(product.Id, reloaded.Entries[0].Id);
        Assert.Equal("B0H25NG1JK", reloaded.Entries[0].Asin);
        Assert.Equal("ASUS Vivobook", reloaded.Entries[0].Snapshot!.Title);
    }

    [Fact]
    public void Restarting_a_search_reuses_its_entry()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        var search = store.StartSearch(new HistorySearchInput(
            "ssd",
            "it",
            ["it"],
            new HistorySearchFilters(),
            HistorySource.Agent));
        store.CompleteSearch(
            search.Id,
            HistoryStatus.Succeeded,
            20,
            null);
        Thread.Sleep(5);
        var before = store.Read();

        var restarted = store.RestartSearch(
            search.Id,
            new HistorySearchFilters { MinRating = 4 });

        Assert.NotNull(restarted);
        Assert.Equal(search.Id, restarted!.Id);
        Assert.Equal(HistoryStatus.Running, restarted.Status);
        Assert.Null(restarted.ResultCount);
        Assert.Null(restarted.FinishedAt);
        Assert.Equal(HistorySource.Agent, restarted.Source);
        Assert.Equal(4, restarted.Filters.MinRating);
        Assert.True(restarted.At > before.Entries[0].At
            || restarted.At == before.Entries[0].At);
        var after = store.Read();
        Assert.Single(after.Entries);
        Assert.Equal(HistoryStatus.Running, after.Entries[0].Status);
        Assert.True(after.Revision > before.Revision);
        Assert.Null(store.RestartSearch("h_missing", new HistorySearchFilters()));
        Assert.Single(store.Read().Entries);
    }

    [Fact]
    public void Repeated_operations_are_not_deduplicated()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        var input = new HistorySearchInput(
            "ssd",
            "it",
            ["it"],
            new HistorySearchFilters(),
            HistorySource.Cli);

        store.StartSearch(input);
        store.StartSearch(input);
        store.AddProduct(new HistoryProductInput(
            "B0H25NG1JK",
            "it",
            new HistoryProductSnapshot { Title = "ASUS" },
            HistorySource.Cli));
        store.AddProduct(new HistoryProductInput(
            "B0H25NG1JK",
            "it",
            new HistoryProductSnapshot { Title = "ASUS" },
            HistorySource.Cli));

        var document = store.Read();
        Assert.Equal(2, document.Entries.Count(entry => entry.Kind == HistoryKind.Search));
        Assert.Equal(2, document.Entries.Count(entry => entry.Kind == HistoryKind.Product));
    }

    [Fact]
    public void Stale_running_searches_become_interrupted()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        var entry = store.StartSearch(new HistorySearchInput(
            "ssd",
            "it",
            ["it"],
            new HistorySearchFilters(),
            HistorySource.Cli));
        var path = Path.Combine(directory.Root, "history.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var target = root["entries"]!.AsArray()
            .First(item => string.Equals(
                item!["id"]!.GetValue<string>(),
                entry.Id,
                StringComparison.Ordinal))!;
        target["started_at"] = DateTimeOffset.UtcNow
            .AddHours(-8)
            .ToString("O");
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var document = new HistoryStore(path).MarkStaleRunning(TimeSpan.FromHours(6));

        var stale = Assert.Single(document.Entries);
        Assert.Equal(HistoryStatus.Interrupted, stale.Status);
        Assert.NotNull(stale.Error);
    }

    [Fact]
    public void Cache_is_cleared_and_round_trips_json()
    {
        using var directory = new TempDirectory();
        var cache = new HistoryCacheStore(
            Path.Combine(directory.Root, "history-cache"));
        cache.Clear();
        cache.Save(
            "h_test",
            System.Text.Json.Nodes.JsonNode.Parse(
                """{"query":"ssd","results":[]}""")!);

        Assert.True(cache.Has("h_test"));
        var cached = cache.Read("h_test");
        Assert.NotNull(cached);
        Assert.Equal("ssd", cached!["result"]!["query"]!.GetValue<string>());

        cache.Delete("h_test");
        Assert.False(cache.Has("h_test"));

        cache.Save(
            "h_other",
            System.Text.Json.Nodes.JsonNode.Parse("""{"results":[]}""")!);
        cache.Clear();
        Assert.False(cache.Has("h_other"));
    }
}

public class HistoryMaintenanceTests
{
    [Fact]
    public void Delete_removes_only_the_requested_entry_and_bumps_revision()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        var first = store.StartSearch(new HistorySearchInput(
            "ssd", "it", ["it"], new HistorySearchFilters(), HistorySource.Cli));
        var second = store.StartSearch(new HistorySearchInput(
            "ram", "it", ["it"], new HistorySearchFilters(), HistorySource.Agent));
        var before = store.Read().Revision;

        var after = store.Delete(first.Id);

        Assert.Equal(before + 1, after.Revision);
        var remaining = Assert.Single(after.Entries);
        Assert.Equal(second.Id, remaining.Id);
    }

    [Fact]
    public void Delete_of_a_missing_id_keeps_revision_and_entries()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        store.StartSearch(new HistorySearchInput(
            "ssd", "it", ["it"], new HistorySearchFilters(), HistorySource.Cli));
        var before = store.Read();

        var after = store.Delete("h_missing");

        Assert.Equal(before.Revision, after.Revision);
        Assert.Single(after.Entries);
    }

    [Fact]
    public void Clear_removes_all_entries_but_only_bumps_revision_once()
    {
        using var directory = new TempDirectory();
        var store = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        store.StartSearch(new HistorySearchInput(
            "ssd", "it", ["it"], new HistorySearchFilters(), HistorySource.Cli));
        store.AddProduct(new HistoryProductInput(
            "B0H25NG1JK",
            "it",
            new HistoryProductSnapshot { Title = "ASUS" },
            HistorySource.Web));
        var before = store.Read().Revision;

        var after = store.Clear();

        Assert.Empty(after.Entries);
        Assert.Equal(before + 1, after.Revision);
        Assert.Empty(store.Clear().Entries);
    }

    [Fact]
    public void History_settings_validate_filters_and_confirmation()
    {
        Assert.False(new HistoryListSettings { Kind = "invalid" }
            .Validate().Successful);
        Assert.False(new HistoryListSettings { Source = "invalid" }
            .Validate().Successful);
        Assert.False(new HistoryListSettings { Status = "invalid" }
            .Validate().Successful);
        Assert.True(new HistoryListSettings
        {
            Kind = "search",
            Source = "agent",
            Status = "succeeded",
        }.Validate().Successful);

        Assert.False(new HistoryRemoveSettings { Ids = [] }
            .Validate().Successful);
        Assert.True(new HistoryRemoveSettings { Ids = ["h_test"] }
            .Validate().Successful);

        Assert.False(new HistoryClearSettings().Validate().Successful);
        Assert.True(new HistoryClearSettings { Yes = true }
            .Validate().Successful);
    }
}
