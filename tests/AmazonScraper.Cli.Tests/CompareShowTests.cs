using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Web;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Tests;

public class CompareShowTests
{
    [Fact]
    public void Diff_keeps_fields_and_specs_even_when_values_do_not_match()
    {
        var first = Item(
            "B000000001",
            new ProductDetail
            {
                Asin = "B000000001",
                Title = "Phone A",
                Brand = "Apple",
                Price = "900,00 €",
                Rating = "3,9 / 5",
                Reviews = "586",
                Seller = "Phone-Buddy",
                Availability = "Disponibilità immediata",
                Specs = new Dictionary<string, string>
                {
                    ["Colore"] = "Titanio Nero",
                    ["Memoria RAM installata"] = "8 GB",
                },
                AmazonDetails =
                [
                    new ProductDetailExtra { Name = "Anno modello", Value = "2024" },
                ],
            });
        var second = Item(
            "B000000002",
            new ProductDetail
            {
                Asin = "B000000002",
                Title = "Phone B",
                Brand = "Apple",
                Price = "1.109,99 €",
                Rating = "4,4 / 5",
                Reviews = "66",
                Seller = "Reswipe",
                Availability = "Disponibilità immediata",
                Specs = new Dictionary<string, string>
                {
                    ["Colore"] = "Arancione Cosmico",
                    ["Memoria RAM installata"] = "256 GB",
                    ["Capacità batteria"] = "4000 mAh",
                },
                AmazonDetails =
                [
                    new ProductDetailExtra { Name = "Anno modello", Value = "2025" },
                ],
            });

        var diff = CompareDiffBuilder.Build([first, second]);

        Assert.Equal(2, diff.Products.Count);
        Assert.Contains(diff.Attributes, item => item.Name == "Price"
            && item.Different
            && item.Values.SequenceEqual(["900,00 €", "1.109,99 €"]));
        Assert.Contains(diff.Attributes, item => item.Name == "Spec · Colore"
            && item.Different);
        Assert.Contains(diff.Attributes, item => item.Name == "Spec · Memoria RAM installata"
            && item.Different);
        var battery = Assert.Single(
            diff.Attributes,
            item => item.Name == "Spec · Capacità batteria");
        Assert.Equal(["—", "4000 mAh"], battery.Values);
        Assert.True(battery.Different);
        Assert.Contains(diff.Attributes, item => item.Name == "Amazon · Anno modello"
            && item.Different);
        Assert.True(diff.DifferenceCount > 0);
    }

    [Fact]
    public void Diff_marks_an_unloaded_product_as_an_error_instead_of_hiding_it()
    {
        var failed = new CompareShowItem(
            new CompareItem
            {
                Domain = "it",
                Asin = "B000000003",
            },
            null,
            string.Empty,
            "HTTP 503");

        var diff = CompareDiffBuilder.Build([failed]);

        Assert.Single(diff.Products);
        Assert.Equal("HTTP 503", diff.Products[0].Error);
        Assert.Contains(diff.Attributes, item => item.Name == "Price"
            && item.Values[0] == "ERROR: HTTP 503");
    }

    [Fact]
    public void Tsv_table_includes_market_specs_and_error_and_is_tab_safe()
    {
        var item = Item(
            "B000000001",
            new ProductDetail
            {
                Asin = "B000000001",
                Title = "Phone\tA",
                Price = "900,00 €",
                Specs = new Dictionary<string, string>
                {
                    ["Colore"] = "Nero",
                },
            });
        var failed = new CompareShowItem(
            new CompareItem { Domain = "de", Asin = "B000000002" },
            null,
            string.Empty,
            "blocked");

        var table = CompareTabsTsv.Table([item, failed]);
        var lines = table.Split('\n');

        Assert.StartsWith("Market\tSource\tASIN\tTitle", lines[0]);
        Assert.Contains("it\tweb\tB000000001", lines[1]);
        Assert.Contains("Specs", lines[0]);
        Assert.Contains("Colore=Nero", lines[1]);
        Assert.Contains("de\tweb\tB000000002", lines[2]);
        Assert.Contains("blocked", lines[2]);
        Assert.DoesNotContain(lines[1].Split('\t'), cell => cell.Contains('\t'));
    }

    [Fact]
    public void Tsv_diff_includes_the_difference_flag_and_all_values()
    {
        var diff = CompareDiffBuilder.Build(
        [
            Item("B000000001", new ProductDetail { Price = "10,00 €" }),
            Item("B000000002", new ProductDetail { Price = "20,00 €" }),
        ]);

        var tsv = CompareTabsTsv.Diff(diff);
        var lines = tsv.Split('\n');

        Assert.StartsWith("Attribute\tDifferent", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("Price\ttrue\t10,00 €\t20,00 €"));
    }

    [Fact]
    public void Show_settings_validate_width_timeout_and_country()
    {
        Assert.False(new CompareShowSettings { Width = 20 }.Validate().Successful);
        Assert.False(new CompareShowSettings { Timeout = 0 }.Validate().Successful);
        Assert.False(new CompareShowSettings { Country = "Italy" }.Validate().Successful);
        Assert.True(new CompareShowSettings
        {
            Width = 220,
            Timeout = 45,
            Country = "IT",
            Cap = "70125",
            Diff = true,
        }.Validate().Successful);
    }

    [Fact]
    public void Diff_condition_shows_the_grade_of_the_seller_offer()
    {
        var first = Item(
            "B000000001",
            new ProductDetail
            {
                Asin = "B000000001",
                Seller = "Phone-Buddy",
                BuyingOptions = new BuyingOptionsSummary
                {
                    Condition = "Rinnovato",
                    Options =
                    [
                        new BuyingOption
                        {
                            Seller = "Phone-Buddy",
                            Condition = "Ricondizionato: premium",
                            ConditionType = "refurbished",
                            ConditionGrade = "premium",
                            IsFeatured = true,
                        },
                    ],
                },
            });
        var second = Item(
            "B000000002",
            new ProductDetail
            {
                Asin = "B000000002",
                Seller = "Reswipe",
                BuyingOptions = new BuyingOptionsSummary
                {
                    Condition = "Rinnovato",
                    Options =
                    [
                        new BuyingOption
                        {
                            Seller = "Reswipe",
                            Condition = "Ricondizionato: eccellente",
                            ConditionType = "refurbished",
                            ConditionGrade = "excellent",
                            IsFeatured = true,
                        },
                    ],
                },
            });

        var diff = CompareDiffBuilder.Build([first, second]);

        var condition = Assert.Single(
            diff.Attributes,
            item => item.Name == "Condition");
        Assert.Equal(
            ["Ricondizionato: premium", "Ricondizionato: eccellente"],
            condition.Values);
        Assert.True(condition.Different);
    }

    [Fact]
    public void Diff_and_tsv_include_prime_pricing()
    {
        var first = Item(
            "B000000001",
            new ProductDetail
            {
                Asin = "B000000001",
                Title = "Keyboard A",
                Price = "149,99 €",
                Prime = new PrimePricing
                {
                    Price = "129,99 €",
                    OriginalPrice = "149,99 €",
                    Discount = "-13%",
                },
            });
        var second = Item(
            "B000000002",
            new ProductDetail
            {
                Asin = "B000000002",
                Title = "Keyboard B",
                Price = "132,99 €",
                Prime = new PrimePricing
                {
                    Price = "109,99 €",
                    OriginalPrice = "132,99 €",
                    Discount = "-17%",
                    Only = true,
                },
            });

        var diff = CompareDiffBuilder.Build([first, second]);

        Assert.Contains(diff.Attributes, item => item.Name == "Prime price"
            && item.Values.SequenceEqual(["129,99 €", "109,99 €"])
            && item.Different);
        Assert.Contains(diff.Attributes, item => item.Name == "Prime only"
            && item.Values.SequenceEqual(["No", "Yes"])
            && item.Different);

        var lines = CompareTabsTsv.Table([first, second]).Split('\n');
        Assert.Contains("Prime\tPrimeDiscount\tPrimeOnly", lines[0]);
        Assert.Contains("129,99 €\t-13%", lines[1]);
        Assert.Contains("\t109,99 €\t-17%\ttrue", lines[2]);
    }

    [Fact]
    public void Diff_and_tsv_include_usually_kept()
    {
        var first = Item(
            "B000000001",
            new ProductDetail
            {
                Asin = "B000000001",
                Title = "Cube",
                UsuallyKept = "I clienti di solito tengono questo articolo.",
            });
        var second = Item(
            "B000000002",
            new ProductDetail
            {
                Asin = "B000000002",
                Title = "Other",
            });

        var diff = CompareDiffBuilder.Build([first, second]);

        Assert.Contains(diff.Attributes, item => item.Name == "Usually kept"
            && item.Values.SequenceEqual([
                "I clienti di solito tengono questo articolo.",
                "—",
            ])
            && item.Different);

        var lines = CompareTabsTsv.Table([first, second]).Split('\n');
        Assert.Contains("UsuallyKept", lines[0]);
        Assert.Contains("I clienti di solito tengono questo articolo.", lines[1]);
    }

    private static CompareShowItem Item(
        string asin,
        ProductDetail detail) =>
        new(
            new CompareItem
            {
                Domain = asin == "B000000002" ? "de" : "it",
                Asin = asin,
                Source = asin == "B000000002" ? "agent" : "web",
            },
            detail,
            "EUR",
            null);
}
