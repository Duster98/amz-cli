using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Services;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Tests;

public class LibraryCommandTests
{
    [Fact]
    public void Add_settings_require_products_and_a_valid_quantity()
    {
        var missing = new LibraryAddSettings { Products = [] };
        Assert.False(missing.Validate().Successful);

        var invalid = new LibraryAddSettings
        {
            Products = ["B0H25NG1JK"],
            Quantity = 100,
        };
        Assert.False(invalid.Validate().Successful);

        var valid = new LibraryAddSettings
        {
            Products = ["B0H25NG1JK"],
            Quantity = 2,
        };
        Assert.True(valid.Validate().Successful);
    }

    [Fact]
    public void Saved_product_uses_lowest_offer_when_the_buybox_has_no_price()
    {
        var detail = new ProductDetail
        {
            Asin = "B0CYHF18TY",
            Title = "Kingston FURY Beast DDR5",
            Price = string.Empty,
            BuyingOptions = new BuyingOptionsSummary
            {
                Count = 14,
                LowestPrice = "428,15 €",
                Condition = "Nuovo",
            },
        };
        var result = new ProductDetailsResult(
            "it",
            "Amazon.it",
            "EUR",
            "IT",
            detail,
            [],
            Cached: false,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            Stale: false);

        var product = SavedProductResolver.FromDetails(result);

        Assert.Equal(428.15m, product.PriceLast);
        Assert.Equal(428.15m, product.PriceAtSave);
        Assert.Equal("428,15 €", product.Snapshot.Price);
        Assert.Equal(428.15m, product.Snapshot.PriceAmount);
    }

    [Fact]
    public void Group_settings_validate_names_and_products()
    {
        Assert.False(new LibraryGroupCreateSettings().Validate().Successful);
        Assert.False(new LibraryGroupAddSettings
        {
            Group = "g_test",
            Products = [],
        }.Validate().Successful);
        Assert.True(new LibraryGroupRenameSettings
        {
            Group = "g_test",
            Name = "Nuovo nome",
        }.Validate().Successful);
    }

    [Fact]
    public void Group_description_and_color_settings_are_validated()
    {
        Assert.True(new LibraryGroupCreateSettings
        {
            Name = "Test",
            Accent = "#a1b2c3",
            Description = "Descrizione",
        }.Validate().Successful);
        Assert.False(new LibraryGroupCreateSettings
        {
            Name = "Test",
            Accent = "not-a-color",
        }.Validate().Successful);
        Assert.False(new LibraryGroupCreateSettings
        {
            Name = "Test",
            Description = new string('x', 501),
        }.Validate().Successful);
        Assert.False(new LibraryGroupEditSettings
        {
            Group = "g_test",
        }.Validate().Successful);
        Assert.True(new LibraryGroupEditSettings
        {
            Group = "g_test",
            Accent = "violet",
            ClearDescription = true,
        }.Validate().Successful);
        Assert.False(new LibraryGroupEditSettings
        {
            Group = "g_test",
            Description = "x",
            ClearDescription = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Detail_result_maps_to_a_saved_snapshot()
    {
        var detail = new ProductDetail
        {
            Asin = "B0H25NG1JK",
            Title = "ASUS Vivobook",
            Image = "https://example.test/image.jpg",
            Price = "679,00 €",
            OriginalPrice = "799,00 €",
            Discount = "-15%",
            Rating = "4,5 / 5",
            Reviews = "128",
            Availability = "Disponibilità immediata",
            Url = "https://www.amazon.it/dp/B0H25NG1JK",
            Delivery = new DeliveryInfo { Time = "domani" },
            Charger = new ChargerInfo
            {
                Included = false,
                Label = "Senza caricatore",
            },
        };
        var result = new ProductDetailsResult(
            "it",
            "Amazon.it",
            "EUR",
            "IT",
            detail,
            [],
            Cached: false,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            Stale: false);

        var product = SavedProductResolver.FromDetails(result);

        Assert.Equal("it:B0H25NG1JK", product.Id);
        Assert.Equal(679, product.PriceAtSave);
        Assert.Equal(679, product.PriceLast);
        Assert.Equal(result.FetchedAt, product.LastCheckedAt);
        Assert.Equal("EUR", product.Currency);
        Assert.Equal("679,00 €", product.Snapshot.Price);
        Assert.Equal("domani", product.Snapshot.DeliveryText);
        Assert.False(product.Snapshot.Charger!.Included);
    }
}
