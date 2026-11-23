using AmazonScraper.Cli.Library;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Tests;

public class SavedLibraryStoreTests
{
    [Fact]
    public void Products_and_groups_are_normalized_and_persisted()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "library.json");
        var store = new SavedLibraryStore(path);

        store.AddOrUpdateProducts(
        [
            NewProduct("b0h25ng1jk", "IT"),
        ]);
        var document = store.CreateGroup(new SavedGroup
        {
            Id = "g_laptops",
            Name = "  Laptop ufficio  ",
            Accent = "BLUE",
            Description = "  Portatili per il lavoro  ",
            Items =
            [
                new SavedGroupItem { ProductId = "it:B0H25NG1JK" },
            ],
        });

        var reloaded = new SavedLibraryStore(path).Read();
        var product = Assert.Single(reloaded.Products);
        Assert.Equal("it:B0H25NG1JK", product.Id);
        Assert.Equal("B0H25NG1JK", product.Asin);
        Assert.Equal(679, product.PriceLast);
        Assert.False(product.Snapshot.Charger!.Included);
        var group = Assert.Single(reloaded.Groups);
        Assert.Equal("Laptop ufficio", group.Name);
        Assert.Equal("blue", group.Accent);
        Assert.Equal("Portatili per il lavoro", group.Description);
        var item = Assert.Single(group.Items);
        Assert.Equal("it:B0H25NG1JK", item.ProductId);
        Assert.Equal(document.Revision, reloaded.Revision);
    }

    [Fact]
    public void Normalizer_drops_duplicates_and_dangling_items()
    {
        var library = SavedLibraryNormalizer.Normalize(new SavedLibrary
        {
            Products =
            [
                NewProduct("B0H25NG1JK", "it", "First"),
                NewProduct("B0H25NG1JK", "IT", "Second"),
            ],
            Groups =
            [
                new SavedGroup
                {
                    Id = "g_test",
                    Name = "Test",
                    Items =
                    [
                        new SavedGroupItem { ProductId = "it:B0H25NG1JK" },
                        new SavedGroupItem { ProductId = "it:B0H25NG1JK" },
                        new SavedGroupItem { ProductId = "it:B0MISSING00" },
                    ],
                },
            ],
        });

        var product = Assert.Single(library.Products);
        Assert.Equal("it:B0H25NG1JK", product.Id);
        var group = Assert.Single(library.Groups);
        Assert.Single(group.Items);
    }

    [Fact]
    public void Mutations_increment_revision_and_keep_products_when_a_group_is_deleted()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));

        var first = store.AddOrUpdateProducts([NewProduct("B0H25NG1JK", "it")]);
        Assert.Equal(1, first.Revision);

        var created = store.CreateGroup(new SavedGroup
        {
            Id = "g_test",
            Name = "Test",
        });
        Assert.Equal(2, created.Revision);
        Assert.Empty(Assert.Single(created.Groups).Items);

        var withItem = store.AddGroupItems(
            "g_test",
            [new SavedGroupItem { ProductId = "it:B0H25NG1JK" }]);
        Assert.Equal(3, withItem.Revision);
        Assert.Single(Assert.Single(withItem.Groups).Items);

        var removedItem = store.RemoveGroupItem("g_test", "it:B0H25NG1JK");
        Assert.Equal(4, removedItem.Revision);
        Assert.Empty(Assert.Single(removedItem.Groups).Items);

        var deleted = store.DeleteGroup("g_test");
        Assert.Equal(5, deleted.Revision);
        Assert.Empty(deleted.Groups);
        Assert.Single(deleted.Products);
    }

    [Fact]
    public void Update_product_preserves_the_original_save_time()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var saved = store.AddOrUpdateProducts(
            [NewProduct("B0H25NG1JK", "it")]).Products[0];
        var originalSavedAt = saved.SavedAt;
        var originalPriceAtSave = saved.PriceAtSave;

        Thread.Sleep(5);
        var update = NewProduct("B0H25NG1JK", "it");
        update.PriceLast = 599;
        update.Snapshot.Price = "599,00 €";
        var document = store.UpdateProduct(saved.Id, update);

        var reloaded = Assert.Single(document.Products);
        Assert.Equal(originalSavedAt, reloaded.SavedAt);
        Assert.Equal(599, reloaded.PriceLast);
        Assert.Equal(originalPriceAtSave, reloaded.PriceAtSave);
    }

    [Fact]
    public void Syncing_details_updates_freshness_and_preserves_the_save_baseline()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var saved = store.AddOrUpdateProducts(
            [NewProduct("B0H25NG1JK", "it")]).Products[0];
        var originalSavedAt = saved.SavedAt;
        var originalPriceAtSave = saved.PriceAtSave;
        var checkedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var details = NewProduct("B0H25NG1JK", "it", "ASUS Vivobook 16");
        details.PriceLast = 599;
        details.LastCheckedAt = checkedAt;
        details.Snapshot.Price = "599,00 €";
        details.Snapshot.PriceAmount = 599;

        Assert.True(store.TrySyncProductDetails(details, checkedAt));

        var reloaded = Assert.Single(
            new SavedLibraryStore(Path.Combine(directory.Root, "library.json"))
                .Read()
                .Products);
        Assert.Equal(originalSavedAt, reloaded.SavedAt);
        Assert.Equal(originalPriceAtSave, reloaded.PriceAtSave);
        Assert.Equal(599, reloaded.PriceLast);
        Assert.Equal(checkedAt, reloaded.LastCheckedAt);
        Assert.Equal("ASUS Vivobook 16", reloaded.Snapshot.Title);
        Assert.Equal(2, new SavedLibraryStore(Path.Combine(directory.Root, "library.json"))
            .Read()
            .Revision);

        Assert.False(store.TrySyncProductDetails(details, checkedAt));
        Assert.Equal(2, new SavedLibraryStore(Path.Combine(directory.Root, "library.json"))
            .Read()
            .Revision);
    }

    [Fact]
    public void Syncing_details_ignores_data_older_than_the_save_time()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var saved = store.AddOrUpdateProducts(
            [NewProduct("B0H25NG1JK", "it")]).Products[0];
        var details = NewProduct("B0H25NG1JK", "it", "Older title");
        details.PriceLast = 500;

        Assert.False(store.TrySyncProductDetails(
            details,
            saved.SavedAt.AddHours(-1)));
        var reloaded = Assert.Single(store.Read().Products);
        Assert.Equal("ASUS Vivobook", reloaded.Snapshot.Title);
        Assert.Equal(679, reloaded.PriceLast);
        Assert.Null(reloaded.LastCheckedAt);
    }

    [Fact]
    public void Readding_a_product_does_not_regress_last_checked_at()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var checkedAt = DateTimeOffset.UtcNow;
        var details = NewProduct("B0H25NG1JK", "it");
        details.LastCheckedAt = checkedAt;
        store.AddOrUpdateProducts([details]);
        var stale = NewProduct("B0H25NG1JK", "it", "Stale title");
        stale.LastCheckedAt = checkedAt.AddHours(-2);

        var document = store.AddOrUpdateProducts([stale]);

        Assert.Equal(
            checkedAt,
            Assert.Single(document.Products).LastCheckedAt);
    }

    [Fact]
    public void Syncing_details_ignores_products_that_are_not_saved()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var details = NewProduct("B0H25NG2JK", "it");

        Assert.False(store.TrySyncProductDetails(details, DateTimeOffset.UtcNow));
        Assert.Empty(store.Read().Products);
    }

    [Fact]
    public void Group_quantity_is_set_not_accumulated()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        store.AddOrUpdateProducts([NewProduct("B0H25NG1JK", "it")]);
        store.CreateGroup(new SavedGroup { Id = "g_test", Name = "Test" });

        store.AddGroupItems(
            "g_test",
            [new SavedGroupItem { ProductId = "it:B0H25NG1JK", Quantity = 1 }]);
        var document = store.AddGroupItems(
            "g_test",
            [new SavedGroupItem { ProductId = "it:B0H25NG1JK", Quantity = 2 }]);
        document = store.AddGroupItems(
            "g_test",
            [new SavedGroupItem { ProductId = "it:B0H25NG1JK", Quantity = 1 }]);

        Assert.Equal(
            2,
            Assert.Single(Assert.Single(document.Groups).Items).Quantity);
    }

    [Fact]
    public void Removing_a_product_removes_it_from_groups()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        store.AddOrUpdateProducts([NewProduct("B0H25NG1JK", "it")]);
        store.CreateGroup(new SavedGroup
        {
            Id = "g_test",
            Name = "Test",
            Items =
            [
                new SavedGroupItem { ProductId = "it:B0H25NG1JK" },
            ],
        });

        var document = store.RemoveProduct("it:B0H25NG1JK");

        Assert.Empty(document.Products);
        Assert.Empty(Assert.Single(document.Groups).Items);
    }

    [Fact]
    public void Add_group_items_requires_saved_products()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        store.CreateGroup(new SavedGroup { Id = "g_test", Name = "Test" });

        var error = Assert.Throws<SavedLibraryNotFoundException>(() =>
            store.AddGroupItems(
                "g_test",
                [new SavedGroupItem { ProductId = "it:B0H25NG1JK" }]));
        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public void Concurrent_stores_do_not_lose_updates()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "library.json");
        var first = new SavedLibraryStore(path);
        var second = new SavedLibraryStore(path);

        first.AddOrUpdateProducts([NewProduct("B0H25NG1JK", "it")]);
        second.AddOrUpdateProducts([NewProduct("B0H25NG2JK", "it")]);

        var reloaded = new SavedLibraryStore(path).Read();
        Assert.Equal(2, reloaded.Products.Count);
        Assert.Equal(2, reloaded.Revision);
    }

    [Fact]
    public void Legacy_ui_state_library_is_migrated_and_backed_up()
    {
        using var directory = new TempDirectory();
        var libraryPath = Path.Combine(directory.Root, "library.json");
        var uiPath = Path.Combine(directory.Root, "web", "ui-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(uiPath)!);
        File.WriteAllText(
            uiPath,
            """
            {
              "schema": 3,
              "library": {
                "products": [
                  {
                    "asin": "B0H25NG1JK",
                    "domain": "it",
                    "price_at_save": 699,
                    "price_last": 679,
                    "snapshot": {
                      "title": "ASUS Vivobook",
                      "price": "679,00 €",
                      "price_amount": 679,
                      "currency": "EUR"
                    }
                  }
                ],
                "groups": [
                  {
                    "id": "g_laptops",
                    "name": "Laptop ufficio",
                    "accent": "blue",
                    "items": [
                      {
                        "product_id": "it:B0H25NG1JK",
                        "quantity": 1
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var document = new SavedLibraryStore(
            libraryPath,
            legacyUiStatePath: uiPath).Read();

        Assert.Single(document.Products);
        Assert.Single(document.Groups);
        Assert.True(File.Exists(libraryPath));
        Assert.True(File.Exists($"{uiPath}.pre-library.bak"));
    }

    [Fact]
    public void Corrupt_library_is_not_overwritten()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "library.json");
        File.WriteAllText(path, "{ not json");

        var store = new SavedLibraryStore(path);
        Assert.Throws<SavedLibraryException>(() => store.Read());
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    private static SavedProduct NewProduct(
        string asin,
        string domain,
        string title = "ASUS Vivobook")
    {
        return new SavedProduct
        {
            Asin = asin,
            Domain = domain,
            PriceAtSave = 699,
            PriceLast = 679,
            Snapshot = new SavedSnapshot
            {
                Title = title,
                Price = "679,00 €",
                PriceAmount = 679,
                Currency = "EUR",
                Charger = new ChargerInfo
                {
                    Included = false,
                    Label = "Senza caricatore",
                },
            },
        };
    }
}


public class SavedGroupDescriptionTests
{
    [Fact]
    public void Normalizer_accepts_custom_hex_colors_and_truncates_descriptions()
    {
        var group = SavedLibraryNormalizer.NormalizeGroup(
            new SavedGroup
            {
                Id = "g_test",
                Name = "Test",
                Accent = "#A1b2C3",
                Description = new string('x', 600),
            },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.NotNull(group);
        Assert.Equal("#a1b2c3", group!.Accent);
        Assert.Equal(
            SavedLibraryNormalizer.MaxGroupDescriptionLength,
            group.Description!.Length);
    }

    [Fact]
    public void Invalid_accent_and_empty_description_are_normalized()
    {
        var group = SavedLibraryNormalizer.NormalizeGroup(
            new SavedGroup
            {
                Id = "g_test",
                Name = "Test",
                Accent = "not-a-color",
                Description = "   ",
            },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.NotNull(group);
        Assert.Equal("accent", group!.Accent);
        Assert.Null(group.Description);
    }
}
