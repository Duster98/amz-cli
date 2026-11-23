using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Services;

/// <summary>
/// A normalized Amazon delivery destination. Amazon's GLOW flow accepts a
/// postal code and an optional country code; city and region are inferred by
/// Amazon.
/// </summary>
public sealed record DeliveryLocation(string CountryCode, string PostalCode)
{
    public string CacheKey => $"{CountryCode}:{PostalCode}";

    public bool CountryOnly => PostalCode.Length == 0;

    public static DeliveryLocation? Normalize(
        string? countryCode,
        string? postalCode,
        IMarketplaceProfile marketplace)
    {
        var postal = (postalCode ?? string.Empty).Trim();
        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (postal.Length == 0 && country.Length == 0)
        {
            return null;
        }

        if (country.Length == 0)
        {
            country = marketplace.CountryCode;
        }
        if (!PostalCodeValidator.IsCountryCode(country))
        {
            throw new ArgumentException(
                "The country must be a two-letter ISO code (e.g. IT, FR, GB, ES).");
        }

        if (marketplace.SupportsCountryOnlyDelivery
            && !string.Equals(
                country,
                marketplace.CountryCode,
                StringComparison.OrdinalIgnoreCase))
        {
            // Amazon.com uses the "ship outside the US" country picker for
            // international destinations. The ZIP field is US-only, so any
            // supplied postal code is intentionally ignored.
            return new DeliveryLocation(country, string.Empty);
        }

        if (postal.Length == 0)
        {
            throw new ArgumentException(
                $"With a delivery country, also specify a postal code for " +
                $"{marketplace.DisplayName}.");
        }

        if (!PostalCodeValidator.TryNormalize(
                country,
                postal,
                out var normalizedCountry,
                out var normalizedPostal,
                out var error))
        {
            throw new ArgumentException(error);
        }

        return new DeliveryLocation(normalizedCountry, normalizedPostal);
    }

    public async Task ApplyAsync(
        AmazonClient client,
        string seedUrl,
        CancellationToken cancellationToken = default)
    {
        var applied = await client
            .SetDeliveryLocationAsync(
                seedUrl,
                PostalCode,
                CountryCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (!applied)
        {
            var destination = CountryOnly
                ? CountryCode
                : $"{PostalCode} ({CountryCode})";
            throw new InvalidOperationException(
                $"Could not set the delivery location to {destination}.");
        }
    }
}
