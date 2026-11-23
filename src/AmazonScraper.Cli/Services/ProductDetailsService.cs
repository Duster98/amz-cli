using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Services;

public sealed record ProductDetailsRequest(
    string Asin,
    string? Domain = null,
    bool IncludeRating = true,
    bool IncludeReviews = true,
    int ReviewLimit = 8,
    double TimeoutSeconds = 30,
    bool IncludeBuyingOptions = true,
    int OptionsDelayMs = 250,
    int OptionsPages = 3,
    string? DeliveryCountry = null,
    string? DeliveryPostalCode = null,
    bool IncludeVariantPrices = false,
    HttpSettings? Http = null);

public sealed record ProductDetailsResult(
    string MarketplaceId,
    string MarketplaceName,
    string Currency,
    string CountryCode,
    ProductDetail Product,
    IReadOnlyList<string> Warnings,
    bool Cached,
    DateTimeOffset FetchedAt,
    TimeSpan CacheAge,
    bool Stale);

public sealed class ProductDetailsService
{
    private const int OptionsPageSize = 10;
    private const int CachedReviewLimit = 8;
    private readonly ProductDetailsCache _cache;

    public ProductDetailsService(ProductDetailsCache? cache = null)
    {
        _cache = cache ?? ProductDetailsCache.Shared;
    }

    public async Task<ProductDetailsResult> GetAsync(
        ProductDetailsRequest request,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var asin = NormalizeAsin(request.Asin);
        var marketplace = MarketplaceRegistry.Resolve(request.Domain);
        var settings = new CommonSettings
        {
            Domain = request.Domain,
            Timeout = request.TimeoutSeconds,
        };
        var deliveryLocation = DeliveryLocation.Normalize(
            request.DeliveryCountry,
            request.DeliveryPostalCode,
            marketplace);
        var savedTransportExplicit = request.Http?.Transport is "curl" or "http";
        var forceCurlForDelivery = deliveryLocation is not null
            && marketplace.Id == "com"
            && !savedTransportExplicit;
        var options = AmazonOptionsFactory.Create(
            settings,
            marketplace,
            forceCurlForDelivery ? AmazonTransport.Curl : null,
            saved: request.Http);
        using var client = new AmazonClient(marketplace, options);
        if (deliveryLocation is not null)
        {
            await deliveryLocation
                .ApplyAsync(
                    client,
                    marketplace.DetailUrl(asin),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        var key = CacheKey(
            marketplace.Id,
            asin,
            deliveryLocation?.CountryCode,
            deliveryLocation?.PostalCode);

        var baseResult = await _cache
            .GetBaseAsync(
                key,
                token => FetchBaseAsync(
                    asin,
                    marketplace,
                    client,
                    deliveryLocation?.CountryCode,
                    deliveryLocation?.PostalCode,
                    token),
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);
        var detail = baseResult.Data.Detail;
        var warnings = new List<string>(baseResult.Warnings);
        var optionsLoadedNow = false;

        if (request.IncludeBuyingOptions && baseResult.Data.ShouldLoadOptions)
        {
            var optionsResult = await _cache
                .EnsureOptionsAsync(
                    key,
                    (current, optionWarnings, token) =>
                        LoadBuyingOptionsAsync(
                            client,
                            marketplace,
                            current,
                            asin,
                            request.OptionsDelayMs,
                            request.OptionsPages,
                            optionWarnings,
                            token),
                    cancellationToken)
                .ConfigureAwait(false);
            optionsLoadedNow = optionsResult.LoadedNow;
            warnings.AddRange(optionsResult.Warnings);
        }

        if (request.IncludeVariantPrices
            && detail.Variants is { Items.Count: > 1 } variants
            && variants.Items.Any(item =>
                !item.Selected && string.IsNullOrWhiteSpace(item.Price)))
        {
            var variantWarnings = await ProductVariantEnricher
                .EnrichAsync(
                    detail,
                    marketplace,
                    client,
                    delayMs: 50,
                    cancellationToken: cancellationToken,
                    maxConcurrency: 6)
                .ConfigureAwait(false);
            warnings.AddRange(variantWarnings);
        }

        return new ProductDetailsResult(
            marketplace.Id,
            marketplace.DisplayName,
            marketplace.CurrencyCode,
            marketplace.CountryCode,
            detail,
            warnings,
            Cached: baseResult.FromCache
                && !optionsLoadedNow
                && !baseResult.Stale,
            baseResult.FetchedAt,
            DateTimeOffset.UtcNow - baseResult.FetchedAt,
            baseResult.Stale);
    }

    /// <summary>
    /// Base detail without AOD expansion, used by search enrichment. It shares
    /// the same cache entry as the web detail endpoint.
    /// </summary>
    public async Task<ProductDetail> GetBaseAsync(
        string asin,
        IMarketplaceProfile marketplace,
        AmazonClient client,
        CancellationToken cancellationToken = default,
        string? deliveryCountry = null,
        string? deliveryPostalCode = null)
    {
        var normalized = NormalizeAsin(asin);
        var location = DeliveryLocation.Normalize(
            deliveryCountry,
            deliveryPostalCode,
            marketplace);
        var result = await _cache
            .GetBaseAsync(
                CacheKey(
                    marketplace.Id,
                    normalized,
                    location?.CountryCode,
                    location?.PostalCode),
                token => FetchBaseAsync(
                    normalized,
                    marketplace,
                    client,
                    location?.CountryCode,
                    location?.PostalCode,
                    token),
                forceRefresh: false,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Data.Detail;
    }

    private static async Task<ProductDetailsCache.BaseData> FetchBaseAsync(
        string asin,
        IMarketplaceProfile marketplace,
        AmazonClient client,
        string? deliveryCountry,
        string? deliveryPostalCode,
        CancellationToken cancellationToken)
    {
        var html = await client.DetailAsync(asin, cancellationToken)
            .ConfigureAwait(false);
        var detail = new DetailParser(marketplace)
            .Parse(html, asin, deliveryCountry, deliveryPostalCode);

        if (string.IsNullOrWhiteSpace(detail.Title)
            && string.IsNullOrWhiteSpace(detail.Price)
            && detail.Images.Count == 0
            && detail.Description.Count == 0
            && detail.Specs.Count == 0)
        {
            throw new InvalidOperationException(
                "Detail page with no product content " +
                "(possible block, redirect, or unsupported layout).");
        }

        detail.RatingDetails = CustomerReviewsParser.ParseRating(
            html,
            marketplace);
        detail.TopReviews = CustomerReviewsParser.ParseReviews(
            html,
            marketplace,
            limit: CachedReviewLimit,
            includeImages: true);
        if (string.IsNullOrWhiteSpace(detail.Url))
        {
            detail.Url = marketplace.ProductUrl(asin);
        }

        var shouldLoadOptions = detail.BuyingOptions is not null
            || string.IsNullOrWhiteSpace(detail.Price)
            || AmazonPatterns.BuyingOptionsLink.IsMatch(html);
        return new ProductDetailsCache.BaseData(detail, shouldLoadOptions);
    }

    private static async Task<bool> LoadBuyingOptionsAsync(
        AmazonClient client,
        IMarketplaceProfile marketplace,
        ProductDetail detail,
        string asin,
        int delayMs,
        int maxPages,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            var aodHtml = await client
                .FetchBuyingOptionsPageAsync(asin, 1, cancellationToken)
                .ConfigureAwait(false);
            var fromAod = BuyingOptionsParser.Parse(aodHtml, marketplace, asin);
            if (fromAod is not null)
            {
                detail.BuyingOptions = BuyingOptionsSummary.Combine(
                    detail.BuyingOptions,
                    fromAod);
            }

            if (detail.BuyingOptions is null)
            {
                return true;
            }

            var totalCount = detail.BuyingOptions.Count;
            var pages = totalCount > OptionsPageSize
                ? Math.Clamp(
                    (int)Math.Ceiling(totalCount / (double)OptionsPageSize),
                    1,
                    Math.Clamp(maxPages, 1, 10))
                : 1;

            for (var page = 2; page <= pages; page++)
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken)
                        .ConfigureAwait(false);
                }

                var pageHtml = await client
                    .FetchBuyingOptionsPageAsync(
                        asin,
                        page,
                        cancellationToken)
                    .ConfigureAwait(false);
                var pageSummary = BuyingOptionsParser.Parse(
                    pageHtml,
                    marketplace,
                    asin);
                if (pageSummary is null || pageSummary.Options.Count == 0)
                {
                    break;
                }

                detail.BuyingOptions.AddOptions(pageSummary.Options);
                if (totalCount > 0
                    && detail.BuyingOptions.Options.Count >= totalCount)
                {
                    break;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            warnings.Add($"Buying options not loaded: {ex.Message}");
            return false;
        }
        catch (AmazonBlockedException ex)
        {
            warnings.Add($"Buying options unavailable: {ex.Message}");
            return false;
        }
        catch (AmazonNotFoundException ex)
        {
            warnings.Add($"Buying options: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            warnings.Add($"Buying options not loaded: {ex.Message}");
            return false;
        }
    }

    private static string NormalizeAsin(string? value)
    {
        var asin = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsAsin(asin))
        {
            throw new ArgumentException(
                "Invalid ASIN: 10 alphanumeric characters are required.",
                nameof(value));
        }

        return asin;
    }

    private static string CacheKey(
        string marketplaceId,
        string asin,
        string? deliveryCountry,
        string? deliveryPostalCode) =>
        $"{marketplaceId}:{asin}:{deliveryCountry}:{deliveryPostalCode}";

    private static bool IsAsin(string value) =>
        value.Length == 10 && value.All(character =>
            char.IsAsciiLetterOrDigit(character));
}
