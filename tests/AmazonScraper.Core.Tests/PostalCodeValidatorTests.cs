using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class PostalCodeValidatorTests
{
    [Theory]
    [InlineData("IT", "20121")]
    [InlineData("FR", "75001")]
    [InlineData("ES", "28010")]
    [InlineData("DE", "10115")]
    [InlineData("US", "10001")]
    [InlineData("US", "10001-1234")]
    [InlineData("GB", "E1 6AN")]
    [InlineData("GB", "sw1a 1aa")]
    [InlineData("JP", "100-0001")]
    [InlineData("JP", "1000001")]
    [InlineData("CH", "8001")]
    public void Valid_postal_codes_are_normalized(string country, string postalCode)
    {
        Assert.True(
            PostalCodeValidator.TryNormalize(
                country,
                postalCode,
                out var normalizedCountry,
                out var normalizedPostalCode,
                out var error),
            error);

        Assert.Equal(country.ToUpperInvariant(), normalizedCountry);
        Assert.Equal(postalCode.ToUpperInvariant(), normalizedPostalCode);
    }

    [Theory]
    [InlineData("IT", "2012")]
    [InlineData("FR", "ABC")]
    [InlineData("ES", "")]
    [InlineData("GB", "XX")]
    [InlineData("JP", "12345")]
    [InlineData("ITA", "20121")]
    [InlineData("IT", "   ")]
    public void Invalid_postal_codes_are_rejected(string country, string postalCode)
    {
        Assert.False(
            PostalCodeValidator.TryNormalize(
                country,
                postalCode,
                out _,
                out _,
                out var error));
        Assert.NotEmpty(error);
    }
}
