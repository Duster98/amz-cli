namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Metadata for a marketplace, including the ones that are planned but not
/// implemented yet. <see cref="Available"/> is false for planned domains.
/// </summary>
public sealed record MarketplaceInfo(
    string Id,
    string DisplayName,
    string Host,
    bool Available,
    string Language);
