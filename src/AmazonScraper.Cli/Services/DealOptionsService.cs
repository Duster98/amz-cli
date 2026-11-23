using System.Collections.Concurrent;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Services;

public sealed record DealOptionsResult(
    string Domain,
    string MarketplaceName,
    string Currency,
    string CountryCode,
    string DealType,
    IReadOnlyList<string> DealTypes,
    IReadOnlyList<DealCategory> Categories,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Loads the deal-type availability and the live category refinements shown by
/// the Amazon deals page. Amazon does not expose a stable category catalog, so
/// the first page is parsed per marketplace and deal type, then cached briefly.
/// </summary>
public sealed class DealOptionsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);

    public async Task<DealOptionsResult> GetAsync(
        string? domain,
        string? dealType,
        HttpSettings? httpSettings = null,
        CancellationToken cancellationToken = default)
    {
        var marketplace = MarketplaceRegistry.Resolve(domain);
        var available = DealFilterCatalog.Available(marketplace.Id);
        if (available.Count == 0)
        {
            throw new ArgumentException(
                $"No deal type available for {marketplace.DisplayName}.",
                nameof(domain));
        }

        DealFilterKind kind;
        if (string.IsNullOrWhiteSpace(dealType))
        {
            kind = available.Contains(DealFilterKind.Today)
                ? DealFilterKind.Today
                : available[0];
        }
        else if (!DealFilterCatalog.TryParseKind(dealType, out kind)
            || !available.Contains(kind))
        {
            kind = available.Contains(DealFilterKind.Today)
                ? DealFilterKind.Today
                : available[0];
        }

        var dealTypes = available
            .Select(DealFilterCatalog.KindName)
            .ToArray();
        var normalizedType = DealFilterCatalog.KindName(kind);
        var cacheKey = $"{marketplace.Id}:{normalizedType}";
        if (_cache.TryGetValue(cacheKey, out var cached)
            && DateTimeOffset.UtcNow - cached.At < CacheTtl)
        {
            return cached.Result;
        }

        if (!DealFilterCatalog.TryGet(marketplace.Id, kind, out var filterId))
        {
            throw new ArgumentException(
                $"Deal type unavailable for {marketplace.DisplayName}.",
                nameof(dealType));
        }

        var html = await FetchDealsPageAsync(
                marketplace,
                filterId,
                httpSettings,
                cancellationToken)
            .ConfigureAwait(false);
        var categories = DealCategoryCatalog.Parse(html);
        var result = new DealOptionsResult(
            marketplace.Id,
            marketplace.DisplayName,
            marketplace.CurrencyCode,
            marketplace.CountryCode,
            normalizedType,
            dealTypes,
            categories,
            []);
        _cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow, result);
        return result;
    }

    private static async Task<string> FetchDealsPageAsync(
        IMarketplaceProfile marketplace,
        string filterId,
        HttpSettings? httpSettings,
        CancellationToken cancellationToken)
    {
        var settings = new CommonSettings
        {
            Domain = marketplace.Id,
            Timeout = null,
        };
        var options = AmazonOptionsFactory.Create(
            settings,
            marketplace,
            desktopLayout: true,
            saved: httpSettings);

        try
        {
            using var client = new AmazonClient(marketplace, options);
            return await client
                .DealsPageAsync(filterId, 1, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AmazonBlockedException)
            when ((httpSettings?.FallbackToCurl ?? true)
                && options.Transport == AmazonTransport.Http)
        {
            var delay = ProductSearchService.DefaultTransportFallbackDelayMs;
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var curlOptions = AmazonOptionsFactory.Create(
                settings,
                marketplace,
                AmazonTransport.Curl,
                desktopLayout: true,
                saved: httpSettings);
            using var client = new AmazonClient(marketplace, curlOptions);
            return await client
                .DealsPageAsync(filterId, 1, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed record CacheEntry(
        DateTimeOffset At,
        DealOptionsResult Result);
}
