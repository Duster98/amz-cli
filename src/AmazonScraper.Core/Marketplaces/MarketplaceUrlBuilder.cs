namespace AmazonScraper.Core.Marketplaces;

internal static class MarketplaceUrlBuilder
{
    public static string Search(IMarketplaceProfile marketplace, string query, int page = 1)
    {
        var pageSuffix = page > 1 ? $"&page={page}" : string.Empty;
        return new Uri(
            marketplace.BaseUri,
            $"{marketplace.SearchPath}?k={Uri.EscapeDataString(query)}{pageSuffix}").AbsoluteUri;
    }

    /// <summary>
    /// Queryless deals search: Amazon applies the marketplace-specific
    /// <c>p_n_deal_type</c> refinement without a keyword.
    /// </summary>
    public static string Deals(
        IMarketplaceProfile marketplace,
        string filterId,
        int page = 1,
        string? categoryNodeId = null)
    {
        var pageSuffix = page > 1 ? $"&page={page}" : string.Empty;
        var refinement = string.IsNullOrWhiteSpace(categoryNodeId)
            ? $"p_n_deal_type%3A{Uri.EscapeDataString(filterId)}"
            : $"n%3A{Uri.EscapeDataString(categoryNodeId)}%2Cp_n_deal_type%3A{Uri.EscapeDataString(filterId)}";
        return new Uri(
            marketplace.BaseUri,
            $"{marketplace.SearchPath}?rh={refinement}{pageSuffix}")
            .AbsoluteUri;
    }
}
