using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Library;

public static class SavedLibraryLookup
{
    public static SavedGroup ResolveGroup(
        SavedLibraryDocument document,
        string reference)
    {
        var value = reference.Trim();
        var byId = document.Groups.FirstOrDefault(group =>
            string.Equals(group.Id, value, StringComparison.Ordinal));
        if (byId is not null)
        {
            return byId;
        }

        var matches = document.Groups
            .Where(group => string.Equals(
                group.Name,
                value,
                StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            > 1 => throw new MonitorException(
                $"Group name is ambiguous: '{value}'. Use the group id."),
            _ => throw new MonitorException(
                $"Saved group not found: {value}."),
        };
    }

    public static SavedProduct RequireProduct(
        SavedLibraryDocument document,
        MonitorTarget target)
    {
        var id = $"{target.Domain}:{target.Asin}";
        var product = document.Products.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        return product ?? throw new MonitorException(
            $"Product not saved: {id}. Add it with 'amz-cli library add'.");
    }

    public static SavedProduct RequireProductById(
        SavedLibraryDocument document,
        string productId)
    {
        var product = document.Products.FirstOrDefault(item =>
            string.Equals(item.Id, productId, StringComparison.Ordinal));
        return product ?? throw new MonitorException(
            $"Saved product not found: {productId}.");
    }

    public static List<SavedGroup> GroupsForProduct(
        SavedLibraryDocument document,
        string productId) =>
        document.Groups
            .Where(group => group.Items.Any(item =>
                string.Equals(
                    item.ProductId,
                    productId,
                    StringComparison.Ordinal)))
            .ToList();

    public static string Currency(SavedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Currency))
        {
            return product.Currency.Trim().ToUpperInvariant();
        }
        if (!string.IsNullOrWhiteSpace(product.Snapshot.Currency))
        {
            return product.Snapshot.Currency.Trim().ToUpperInvariant();
        }
        return MarketplaceRegistry.Resolve(product.Domain).CurrencyCode;
    }
}
