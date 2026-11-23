using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class DetailsSettingsTests
{
    [Fact]
    public void Smid_accepts_one_asin_and_an_alphanumeric_seller()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            SellerId = "A131AJORA55M3D",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Smid_requires_exactly_one_asin()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2", "B0FMRWBK4R"],
            SellerId = "A131AJORA55M3D",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Smid_rejects_html_files()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            SellerId = "A131AJORA55M3D",
            HtmlFile = "/tmp/product.html",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Smid_rejects_compare_mode()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            SellerId = "A131AJORA55M3D",
            Compare = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void All_options_is_valid_for_a_network_run()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            AllOptions = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Options_pages_accepts_one_to_ten_pages()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            OptionsPages = 3,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void All_options_and_options_pages_are_mutually_exclusive()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            AllOptions = true,
            OptionsPages = 3,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Options_pages_out_of_range_is_rejected(int pages)
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            OptionsPages = pages,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Pagination_flags_require_network()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            AllOptions = true,
            HtmlFile = "/tmp/product.html",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Pagination_flags_are_incompatible_with_no_buying_options()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            AllOptions = true,
            NoBuyingOptions = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Save_aod_html_requires_exactly_one_asin()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2", "B0FMRWBK4R"],
            SaveAodHtml = "/tmp/aod.html",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Variant_prices_requires_network()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            HtmlFile = "/tmp/product.html",
            VariantPrices = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Variants_can_be_read_from_local_html()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            HtmlFile = "/tmp/product.html",
            Variants = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Variant_delay_is_bounded()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            VariantDelay = 60_001,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Smid_rejects_non_alphanumeric_values()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B07R3YS8R2"],
            SellerId = "A131-AJORA",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Rating_and_reviews_can_be_read_from_local_html()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            HtmlFile = "/tmp/product.html",
            Rating = true,
            Reviews = true,
            ReviewLimit = 5,
            ReviewImages = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rating_and_reviews_reject_compare_mode(bool rating, bool reviews)
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            Compare = true,
            Rating = rating,
            Reviews = reviews,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Review_images_require_reviews()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            ReviewImages = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Review_limit_requires_reviews()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            ReviewLimit = 5,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Review_limit_is_bounded(int limit)
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            Reviews = true,
            ReviewLimit = limit,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Review_limit_accepts_the_maximum()
    {
        var settings = new DetailsSettings
        {
            Asins = ["B0C9C4GHGN"],
            Reviews = true,
            ReviewLimit = 50,
        };

        Assert.True(settings.Validate().Successful);
    }
}
