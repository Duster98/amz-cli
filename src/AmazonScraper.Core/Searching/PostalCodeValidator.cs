using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Searching;

/// <summary>
/// Validates and normalizes delivery postal codes for the countries where the
/// tool has a known format. Unknown countries are accepted with a permissive
/// check and left to Amazon's address-validation endpoint.
/// </summary>
public static partial class PostalCodeValidator
{
    public static bool IsCountryCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && CountryCodeRegex().IsMatch(code.Trim());

    public static bool TryNormalize(
        string? countryCode,
        string? postalCode,
        out string normalizedCountry,
        out string normalizedPostalCode,
        out string error)
    {
        normalizedCountry = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        normalizedPostalCode = (postalCode ?? string.Empty).Trim();
        error = string.Empty;

        if (!CountryCodeRegex().IsMatch(normalizedCountry))
        {
            error = "The country must be a two-letter ISO code (e.g. IT, FR, GB, ES).";
            return false;
        }

        if (normalizedPostalCode.Length == 0)
        {
            error = "The postal code cannot be empty.";
            return false;
        }

        var pattern = normalizedCountry switch
        {
            "IT" or "FR" or "ES" or "DE" => @"^\d{5}$",
            "US" => @"^\d{5}(?:-\d{4})?$",
            "GB" => @"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$",
            "JP" => @"^\d{3}-?\d{4}$",
            _ => @"^[A-Za-z0-9][A-Za-z0-9\s\-]{1,15}$",
        };

        if (!Regex.IsMatch(normalizedPostalCode, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = normalizedCountry switch
            {
                "IT" or "FR" or "ES" or "DE" => $"The postal code for {normalizedCountry} must have 5 digits.",
                "US" => "The ZIP code for US must have 5 digits (or ZIP+4).",
                "GB" => "The postcode for GB is not valid (e.g. E1 6AN, SW1A 1AA).",
                "JP" => "The postal code for JP must have 7 digits (e.g. 100-0001).",
                _ => $"The postal code for {normalizedCountry} is not valid.",
            };
            return false;
        }

        if (normalizedCountry == "GB")
        {
            normalizedPostalCode = normalizedPostalCode.ToUpperInvariant();
        }

        return true;
    }

    [GeneratedRegex(@"^[A-Za-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryCodeRegex();
}
