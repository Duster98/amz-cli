using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorBindingTests
{
    [Fact]
    public void Binding_store_persists_and_updates_revision()
    {
        using var directory = new TempDirectory();
        var store = new MonitorBindingStore(
            Path.Combine(directory.Root, "monitor-bindings.json"));

        var first = store.Upsert(new MonitorBinding
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B000000001",
            MaxPrice = 100,
        });
        Assert.Equal(1, first.Revision);
        var saved = Assert.Single(first.Bindings);
        Assert.Equal("product:it:B000000001", saved.Id);

        var second = store.Upsert(new MonitorBinding
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B000000001",
            MaxPrice = 90,
            DropPercent = 10,
            RisePercent = 15,
        });
        Assert.Equal(2, second.Revision);
        var updated = Assert.Single(second.Bindings);
        Assert.Equal(90, updated.MaxPrice);
        Assert.Equal(15, updated.RisePercent);
    }

    [Fact]
    public void Product_binding_rejects_a_product_that_belongs_to_a_group()
    {
        var library = Library(
            [
                Product("it", "B000000001", "EUR"),
            ],
            [
                Group("g_test", "Gruppo", "it:B000000001"),
            ]);
        var binding = new MonitorBinding
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B000000001",
        };

        var error = Assert.Throws<MonitorException>(() =>
            LibraryMonitorPlanBuilder.Build(
                binding,
                library,
                new MonitorBindingDocument()));

        Assert.Contains("belongs to group", error.Message);
    }

    [Fact]
    public void Group_binding_follows_live_membership_without_changing_hash()
    {
        var library = Library(
            [
                Product("it", "B000000001", "EUR"),
            ],
            [
                Group("g_test", "Gruppo", "it:B000000001"),
            ]);
        var binding = new MonitorBinding
        {
            Kind = MonitorBindingKind.Group,
            GroupId = "g_test",
            MaxPrice = 100,
        };

        var first = LibraryMonitorPlanBuilder.Build(
            binding,
            library,
            new MonitorBindingDocument());
        library.Products.Add(Product("it", "B000000002", "EUR"));
        library.Groups[0].Items.Add(new SavedGroupItem
        {
            ProductId = "it:B000000002",
        });
        var second = LibraryMonitorPlanBuilder.Build(
            binding,
            library,
            new MonitorBindingDocument());

        Assert.Single(first.Groups[0].Items);
        Assert.Equal(2, second.Groups[0].Items.Count);
        Assert.Equal(first.ConfigHash, second.ConfigHash);
    }

    [Fact]
    public void Multi_currency_group_uses_the_target_currency_and_fx_rate()
    {
        var library = Library(
            [
                Product("it", "B000000001", "EUR"),
                Product("uk", "B000000002", "GBP"),
            ],
            [
                Group("g_test", "Gruppo", "it:B000000001", "uk:B000000002"),
            ]);
        var binding = new MonitorBinding
        {
            Kind = MonitorBindingKind.Group,
            GroupId = "g_test",
            Currency = "EUR",
            TotalMax = 300,
            Fx = new Dictionary<string, decimal> { ["GBP"] = 1.2m },
        };

        var plan = LibraryMonitorPlanBuilder.Build(
            binding,
            library,
            new MonitorBindingDocument());

        var group = Assert.Single(plan.Groups);
        Assert.Equal("EUR", group.Currency);
        Assert.Equal(1.2m, group.FxRates["GBP"]);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void Multi_currency_group_requires_a_target_and_fx_rate()
    {
        var library = Library(
            [
                Product("it", "B000000001", "EUR"),
                Product("uk", "B000000002", "GBP"),
            ],
            [
                Group("g_test", "Gruppo", "it:B000000001", "uk:B000000002"),
            ]);
        var binding = new MonitorBinding
        {
            Kind = MonitorBindingKind.Group,
            GroupId = "g_test",
        };

        var error = Assert.Throws<MonitorException>(() =>
            LibraryMonitorPlanBuilder.Build(
                binding,
                library,
                new MonitorBindingDocument()));
        Assert.Contains("multiple currencies", error.Message);
    }

    [Fact]
    public void Group_binding_rejects_an_individual_product_binding()
    {
        var library = Library(
            [
                Product("it", "B000000001", "EUR"),
            ],
            [
                Group("g_test", "Gruppo", "it:B000000001"),
            ]);
        var bindings = new MonitorBindingDocument
        {
            Bindings =
            [
                new MonitorBinding
                {
                    Id = MonitorBinding.ProductBindingId("it:B000000001"),
                    Kind = MonitorBindingKind.Product,
                    ProductId = "it:B000000001",
                },
            ],
        };

        var error = Assert.Throws<MonitorException>(() =>
            LibraryMonitorPlanBuilder.Build(
                new MonitorBinding
                {
                    Kind = MonitorBindingKind.Group,
                    GroupId = "g_test",
                },
                library,
                bindings));

        Assert.Contains("already monitored individually", error.Message);
    }

    [Fact]
    public void Import_service_creates_library_targets_and_bindings()
    {
        using var directory = new TempDirectory();
        var libraryStore = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var bindingStore = new MonitorBindingStore(
            Path.Combine(directory.Root, "monitor-bindings.json"));
        var config = new MonitorConfig
        {
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "group-one",
                    Name = "Gruppo",
                    TotalMax = 250,
                    Items =
                    [
                        new MonitorGroupItemConfig
                        {
                            Asin = "B000000001",
                            Domain = "it",
                            Quantity = 2,
                            MaxPrice = 100,
                        },
                    ],
                },
            ],
            Items =
            [
                new MonitorItemConfig
                {
                    Asin = "B000000002",
                    Domain = "it",
                    DropPercent = 10,
                    RisePercent = 20,
                },
            ],
        };

        var result = MonitorImportService.Import(
            config,
            "it",
            libraryStore,
            bindingStore,
            dryRun: false);

        Assert.Equal(2, result.ProductsAdded);
        Assert.Equal(1, result.GroupsImported);
        Assert.Equal(1, result.ProductBindings);
        Assert.Equal(1, result.GroupBindings);
        var library = libraryStore.Read();
        Assert.Equal(2, library.Products.Count);
        var group = Assert.Single(library.Groups);
        Assert.Equal("g_group-one", group.Id);
        Assert.Equal(2, Assert.Single(group.Items).Quantity);
        var bindings = bindingStore.Read();
        Assert.Equal(2, bindings.Bindings.Count);
        var groupBinding = bindings.Bindings.Single(item =>
            item.Kind == MonitorBindingKind.Group);
        Assert.Equal(250, groupBinding.TotalMax);
        Assert.Equal(100, groupBinding.ItemRules["it:B000000001"].MaxPrice);
        var productBinding = bindings.Bindings.Single(item =>
            item.Kind == MonitorBindingKind.Product);
        Assert.Equal(20, productBinding.RisePercent);
    }

    private static SavedLibraryDocument Library(
        IEnumerable<SavedProduct> products,
        IEnumerable<SavedGroup> groups) => new()
    {
        Products = products.ToList(),
        Groups = groups.ToList(),
    };

    private static SavedProduct Product(
        string domain,
        string asin,
        string currency) => new()
    {
        Id = $"{domain}:{asin}",
        Asin = asin,
        Domain = domain,
        Currency = currency,
        Snapshot = new SavedSnapshot
        {
            Title = asin,
            Currency = currency,
        },
    };

    private static SavedGroup Group(
        string id,
        string name,
        params string[] productIds) => new()
    {
        Id = id,
        Name = name,
        Items = productIds
            .Select(productId => new SavedGroupItem
            {
                ProductId = productId,
            })
            .ToList(),
    };
}
