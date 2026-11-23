namespace AmazonScraper.Core.Searching;

/// <summary>
/// Amazon's autocomplete endpoint is separate from the storefront and has its
/// own marketplace parameters. The values mirror the request made by the
/// search box in each locale.
/// </summary>
public sealed record SuggestionMarketplace(
    string Host,
    string Locale,
    string Mid,
    string PlainMid);

public static class SuggestionCatalog
{
    private static readonly IReadOnlyDictionary<string, SuggestionMarketplace>
        ById = new Dictionary<string, SuggestionMarketplace>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["it"] = new(
                "completion.amazon.it",
                "it_IT",
                "A11IL2PNWYJU7H",
                "35691"),
            ["com"] = new(
                "completion.amazon.com",
                "en_US",
                "ATVPDKIKX0DER",
                "1"),
            ["fr"] = new(
                "completion.amazon.fr",
                "fr_FR",
                "A13V1IB3VIYZZH",
                "5"),
            ["uk"] = new(
                "completion.amazon.co.uk",
                "en_GB",
                "A1F83G8C2ARO7P",
                "4"),
            ["de"] = new(
                "completion.amazon.de",
                "de_DE",
                "A1PA6795UKMFR9",
                "3"),
            ["jp"] = new(
                "completion.amazon.co.jp",
                "ja_JP",
                "A1VC38T7YXB528",
                "6"),
            ["es"] = new(
                "completion.amazon.es",
                "es_ES",
                "A1RKKUPIHCS9HS",
                "44551"),
        };

    public static bool TryGet(
        string? marketplaceId,
        out SuggestionMarketplace settings)
    {
        if (ById.TryGetValue(
                (marketplaceId ?? string.Empty).Trim(),
                out var value))
        {
            settings = value;
            return true;
        }

        settings = null!;
        return false;
    }

    public static SuggestionMarketplace Get(string marketplaceId) =>
        TryGet(marketplaceId, out var settings)
            ? settings
            : throw new ArgumentException(
                $"Suggestions unavailable for marketplace '{marketplaceId}'.",
                nameof(marketplaceId));

    public static Uri SuggestionsUrl(
        this SuggestionMarketplace settings,
        string prefix,
        int limit = 10)
    {
        var query =
            $"limit={Math.Clamp(limit, 1, 20)}" +
            $"&prefix={Uri.EscapeDataString(prefix)}" +
            "&suggestion-type=KEYWORD" +
            "&suggestion-type=WIDGET" +
            "&page-type=Gateway" +
            "&alias=aps" +
            "&site-variant=desktop" +
            "&version=3" +
            "&event=onKeyPress" +
            $"&lop={Uri.EscapeDataString(settings.Locale)}" +
            $"&mid={Uri.EscapeDataString(settings.Mid)}" +
            $"&plain-mid={Uri.EscapeDataString(settings.PlainMid)}" +
            "&client-info=amazon-search-ui" +
            "&fb=1";
        return new Uri($"https://{settings.Host}/api/2017/suggestions?{query}");
    }
}
