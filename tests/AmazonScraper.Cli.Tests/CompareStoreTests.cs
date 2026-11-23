using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.Tests;

public class CompareStoreTests
{
    [Fact]
    public void Missing_file_returns_an_empty_comparison()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));

        var document = store.Read();

        Assert.Empty(document.Items);
        Assert.Equal(0, document.Revision);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Add_normalizes_deduplicates_and_bumps_the_revision()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));

        var document = store.Add(
        [
            new CompareTarget("IT", "b0h25ng1jk"),
            new CompareTarget("it", "B0H25NG1JK"),
            new CompareTarget("de", "B0FL85P2QS"),
        ],
        "cli");

        Assert.Equal(2, document.Items.Count);
        Assert.Equal(1, document.Revision);
        Assert.Equal("it:B0H25NG1JK", document.Items[0].Id);
        Assert.Equal("de:B0FL85P2QS", document.Items[1].Id);
        Assert.Equal("cli", document.Items[0].Source);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Add_rejects_invalid_marketplace_and_asin()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));

        Assert.Throws<CompareException>(() =>
            store.Add([new CompareTarget("xx", "B0H25NG1JK")]));
        Assert.Throws<CompareException>(() =>
            store.Add([new CompareTarget("it", "bad")]));
        Assert.Empty(store.Read().Items);
    }

    [Fact]
    public void Add_enforces_the_six_item_limit()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        var targets = Enumerable.Range(0, CompareStore.MaxItems)
            .Select(index => new CompareTarget(
                "it",
                $"B{index:D9}"))
            .ToArray();

        var document = store.Add(targets);
        Assert.Equal(CompareStore.MaxItems, document.Items.Count);

        var error = Assert.Throws<CompareFullException>(() =>
            store.Add([new CompareTarget("it", "B999999999")]));
        Assert.Contains("at most 6", error.Message);
        Assert.Equal(CompareStore.MaxItems, store.Read().Items.Count);
    }

    [Fact]
    public void Remove_and_clear_update_the_document()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        store.Add(
        [
            new CompareTarget("it", "B0H25NG1JK"),
            new CompareTarget("de", "B0FL85P2QS"),
        ]);

        var afterRemove = store.Remove("it", "b0h25ng1jk");
        Assert.Single(afterRemove.Items);
        Assert.Equal("de:B0FL85P2QS", afterRemove.Items[0].Id);

        var afterClear = store.Clear();
        Assert.Empty(afterClear.Items);
        Assert.True(afterClear.Revision > afterRemove.Revision);
        Assert.Empty(store.Read().Items);
    }

    [Fact]
    public void Corrupt_file_is_treated_as_empty()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "compare.json");
        File.WriteAllText(path, "{ not json");
        var store = new CompareStore(path);

        var document = store.Read();

        Assert.Empty(document.Items);
    }

    [Fact]
    public void File_roundtrips_the_comparison()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "compare.json");
        var store = new CompareStore(path);
        store.Add([new CompareTarget("it", "B0H25NG1JK")], "agent");

        var reloaded = new CompareStore(path).Read();

        Assert.Single(reloaded.Items);
        Assert.Equal("agent", reloaded.Items[0].Source);
        Assert.Equal(1, reloaded.Revision);
    }
}
