namespace AmazonScraper.Core.Searching;

public enum DealFilterKind
{
    All,
    Today,
    Coupons,
}

/// <summary>
/// Amazon search exposes deals through the internal <c>p_n_deal_type</c>
/// refinement. The numeric values are marketplace-specific and not documented,
/// so this catalog keeps the verified mappings and reports unsupported
/// combinations instead of silently showing unfiltered results.
/// </summary>
public static class DealFilterCatalog
{
    private static readonly Dictionary<string, Dictionary<DealFilterKind, string>>
        Filters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["it"] = new()
            {
                [DealFilterKind.All] = "26901109031",
                [DealFilterKind.Today] = "26901107031",
                [DealFilterKind.Coupons] = "210770022031",
            },
            ["de"] = new()
            {
                [DealFilterKind.All] = "26902994031",
                [DealFilterKind.Today] = "26902993031",
                [DealFilterKind.Coupons] = "210770231031",
            },
            ["fr"] = new()
            {
                [DealFilterKind.All] = "26902977031",
                [DealFilterKind.Today] = "26902975031",
                [DealFilterKind.Coupons] = "210770227031",
            },
            ["es"] = new()
            {
                [DealFilterKind.All] = "26902953031",
            },
            ["uk"] = new()
            {
                [DealFilterKind.All] = "26901100031",
                [DealFilterKind.Today] = "26901098031",
            },
            ["com"] = new()
            {
                [DealFilterKind.All] = "23566065011",
                [DealFilterKind.Today] = "23566064011",
            },
            ["jp"] = new()
            {
                [DealFilterKind.All] = "10343616051",
                [DealFilterKind.Today] = "10343614051",
            },
        };

    public static bool TryParseKind(string? value, out DealFilterKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "all" or "all-discounts" or "all-savings":
                kind = DealFilterKind.All;
                return true;
            case "today" or "today-deals" or "daily":
                kind = DealFilterKind.Today;
                return true;
            case "coupons" or "coupon":
                kind = DealFilterKind.Coupons;
                return true;
            default:
                kind = DealFilterKind.All;
                return false;
        }
    }

    public static bool TryGet(
        string domain,
        DealFilterKind kind,
        out string filterId)
    {
        if (Filters.TryGetValue(domain, out var domainFilters)
            && domainFilters.TryGetValue(kind, out filterId!))
        {
            return true;
        }

        filterId = string.Empty;
        return false;
    }

    public static IReadOnlyList<DealFilterKind> Available(string domain) =>
        Filters.TryGetValue(domain, out var domainFilters)
            ? domainFilters.Keys.OrderBy(value => value).ToArray()
            : [];

    public static string KindName(DealFilterKind kind) =>
        kind switch
        {
            DealFilterKind.Today => "today",
            DealFilterKind.Coupons => "coupons",
            _ => "all",
        };
}
