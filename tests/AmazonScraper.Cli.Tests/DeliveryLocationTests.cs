using AmazonScraper.Cli.Services;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Cli.Tests;

public class DeliveryLocationTests
{
    [Fact]
    public void Empty_cap_means_automatic_location()
    {
        var location = DeliveryLocation.Normalize(
            string.Empty,
            string.Empty,
            ItalyMarketplace.Instance);

        Assert.Null(location);
    }

    [Fact]
    public void Country_and_cap_are_normalized()
    {
        var location = DeliveryLocation.Normalize(
            "it",
            " 20121 ",
            GermanyMarketplace.Instance);

        Assert.NotNull(location);
        Assert.Equal("IT", location!.CountryCode);
        Assert.Equal("20121", location.PostalCode);
        Assert.Equal("IT:20121", location.CacheKey);
    }

    [Fact]
    public void Missing_cap_with_country_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLocation.Normalize(
                "IT",
                string.Empty,
                ItalyMarketplace.Instance));
    }

    [Fact]
    public void Country_only_is_allowed_for_us_international_delivery()
    {
        var location = DeliveryLocation.Normalize(
            "it",
            string.Empty,
            UnitedStatesMarketplace.Instance);

        Assert.NotNull(location);
        Assert.Equal("IT", location!.CountryCode);
        Assert.Equal(string.Empty, location.PostalCode);
        Assert.True(location.CountryOnly);
        Assert.Equal("IT:", location.CacheKey);
    }

    [Fact]
    public void Country_only_is_allowed_for_japan_international_delivery()
    {
        var location = DeliveryLocation.Normalize(
            "IT",
            string.Empty,
            JapanMarketplace.Instance);

        Assert.NotNull(location);
        Assert.Equal("IT", location!.CountryCode);
        Assert.True(location.CountryOnly);
    }

    [Fact]
    public void Japan_domestic_delivery_still_requires_a_postal_code()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLocation.Normalize(
                "JP",
                string.Empty,
                JapanMarketplace.Instance));
    }

    [Fact]
    public void Us_domestic_delivery_still_requires_a_zip_code()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLocation.Normalize(
                "US",
                string.Empty,
                UnitedStatesMarketplace.Instance));
    }

    [Fact]
    public void Us_international_delivery_ignores_a_supplied_postal_code()
    {
        var location = DeliveryLocation.Normalize(
            "IT",
            "20121",
            UnitedStatesMarketplace.Instance);

        Assert.NotNull(location);
        Assert.Equal("IT", location!.CountryCode);
        Assert.Equal(string.Empty, location.PostalCode);
        Assert.True(location.CountryOnly);
    }

    [Fact]
    public void Invalid_cap_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLocation.Normalize(
                "IT",
                "abc",
                ItalyMarketplace.Instance));
    }
}
