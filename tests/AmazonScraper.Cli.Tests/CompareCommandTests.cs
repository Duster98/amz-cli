using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.Tests;

public class CompareCommandTests
{
    [Fact]
    public void Add_requires_products_and_a_valid_source()
    {
        Assert.False(new CompareAddSettings().Validate().Successful);
        Assert.False(new CompareAddSettings
        {
            Products = ["B0H25NG1JK"],
            Source = "browser",
        }.Validate().Successful);
        Assert.True(new CompareAddSettings
        {
            Products = ["B0H25NG1JK"],
            Source = "agent",
        }.Validate().Successful);
    }

    [Fact]
    public void Add_command_accepts_asins_and_domain_prefixed_products()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        var exit = new CompareAddCommand(store).Execute(
            null!,
            new CompareAddSettings
            {
                Products = ["B0H25NG1JK", "de:B0FL85P2QS"],
                Source = "agent",
            },
            CancellationToken.None);

        Assert.Equal(0, exit);
        var document = store.Read();
        Assert.Equal(2, document.Items.Count);
        Assert.Equal("it:B0H25NG1JK", document.Items[0].Id);
        Assert.Equal("de:B0FL85P2QS", document.Items[1].Id);
        Assert.Equal("agent", document.Items[0].Source);
    }

    [Fact]
    public void Add_command_accepts_amazon_urls_and_default_domain()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        var exit = new CompareAddCommand(store).Execute(
            null!,
            new CompareAddSettings
            {
                Products = [
                    "https://www.amazon.it/dp/B0H25NG1JK?th=1",
                    "B0FL85P2QS",
                ],
                Domain = "de",
            },
            CancellationToken.None);

        Assert.Equal(0, exit);
        var document = store.Read();
        Assert.Equal(2, document.Items.Count);
        Assert.Equal("it:B0H25NG1JK", document.Items[0].Id);
        Assert.Equal("de:B0FL85P2QS", document.Items[1].Id);
    }

    [Fact]
    public void Remove_and_clear_commands_update_the_store()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        store.Add(
        [
            new CompareTarget("it", "B0H25NG1JK"),
            new CompareTarget("de", "B0FL85P2QS"),
        ]);

        var removeExit = new CompareRemoveCommand(store).Execute(
            null!,
            new CompareRemoveSettings { Products = ["it:B0H25NG1JK"] },
            CancellationToken.None);
        Assert.Equal(0, removeExit);
        Assert.Single(store.Read().Items);

        var clearExit = new CompareClearCommand(store).Execute(
            null!,
            new CompareClearSettings(),
            CancellationToken.None);
        Assert.Equal(0, clearExit);
        Assert.Empty(store.Read().Items);
    }
}
