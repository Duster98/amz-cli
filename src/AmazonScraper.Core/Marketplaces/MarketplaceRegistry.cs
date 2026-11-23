namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Registry of Amazon marketplaces implemented by the tool.
/// </summary>
public static class MarketplaceRegistry
{
    public static IMarketplaceProfile Default => ItalyMarketplace.Instance;

    public static IReadOnlyList<MarketplaceInfo> All { get; } =
    [
        new("it", "Amazon.it", "www.amazon.it", true, "it"),
        new("com", "Amazon.com", "www.amazon.com", true, "en"),
        new("fr", "Amazon.fr", "www.amazon.fr", true, "fr"),
        new("uk", "Amazon.co.uk", "www.amazon.co.uk", true, "en"),
        new("de", "Amazon.de", "www.amazon.de", true, "de"),
        new("jp", "Amazon.co.jp", "www.amazon.co.jp", true, "ja"),
        new("es", "Amazon.es", "www.amazon.es", true, "es"),
    ];

    public static IReadOnlyList<string> AvailableIds { get; } =
        All.Where(m => m.Available).Select(m => m.Id).ToArray();

    public static bool TryResolve(string? idOrHost, out IMarketplaceProfile profile)
    {
        profile = Default;
        if (string.IsNullOrWhiteSpace(idOrHost))
        {
            return true;
        }

        var normalized = Normalize(idOrHost);
        if (normalized == "it")
        {
            profile = ItalyMarketplace.Instance;
            return true;
        }
        if (normalized == "fr")
        {
            profile = FranceMarketplace.Instance;
            return true;
        }
        if (normalized is "uk" or "co.uk")
        {
            profile = UnitedKingdomMarketplace.Instance;
            return true;
        }
        if (normalized is "de" or "germany")
        {
            profile = GermanyMarketplace.Instance;
            return true;
        }
        if (normalized is "com" or "us")
        {
            profile = UnitedStatesMarketplace.Instance;
            return true;
        }
        if (normalized is "jp" or "co.jp")
        {
            profile = JapanMarketplace.Instance;
            return true;
        }
        if (normalized is "es" or "spain")
        {
            profile = SpainMarketplace.Instance;
            return true;
        }

        return false;
    }

    public static IMarketplaceProfile Resolve(string? idOrHost)
    {
        if (TryResolve(idOrHost, out var profile))
        {
            return profile;
        }

        var planned = All.FirstOrDefault(
            m => !m.Available && Normalize(m.Id) == Normalize(idOrHost));
        if (planned is not null)
        {
            throw new MarketplaceNotAvailableException(
                $"{planned.DisplayName} is not yet available in this build. " +
                $"Available domains: {string.Join(", ", AvailableIds)}.");
        }

        throw new MarketplaceNotFoundException(
            $"Unsupported domain: '{idOrHost}'. " +
            $"Available domains: {string.Join(", ", AvailableIds)}.");
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }
        if (normalized.StartsWith("amazon.", StringComparison.Ordinal))
        {
            normalized = normalized["amazon.".Length..];
        }
        return normalized.TrimStart('.');
    }
}

public class MarketplaceException(string message) : Exception(message);

public sealed class MarketplaceNotAvailableException(string message) : MarketplaceException(message);

public sealed class MarketplaceNotFoundException(string message) : MarketplaceException(message);
