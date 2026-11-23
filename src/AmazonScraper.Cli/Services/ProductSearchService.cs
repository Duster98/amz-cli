using System.Diagnostics;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Services;

public sealed record ProductSearchRequest(
    string Query,
    string? Domain = null,
    int Pages = 1,
    bool NoAds = false,
    bool Delivery = false,
    bool Enrich = false,
    int PageDelayMs = 2000,
    int EnrichDelayMs = 1500,
    double TimeoutSeconds = 30,
    bool FillMissingPrices = true,
    int? Page = null,
    IReadOnlyList<string>? Domains = null,
    string? Sort = null,
    int MarketplaceDelayMs = ProductSearchService.DefaultMarketplaceDelayMs,
    int TransportFallbackDelayMs = ProductSearchService.DefaultTransportFallbackDelayMs,
    bool AllowTransportFallback = true,
    string? DealType = null,
    string? DealCategory = null,
    string? DeliveryCountry = null,
    string? DeliveryPostalCode = null,
    HttpSettings? Http = null);

public sealed record ProductSearchProgress(
    string Stage,
    int CompletedPages,
    int TotalPages,
    string Message);

public sealed record SearchMarketplaceSummary(
    string Id,
    string Name,
    string Currency,
    int ResultCount,
    int TotalAvailablePages,
    IReadOnlyList<int> CompletedPages,
    IReadOnlyList<string> Warnings);

public sealed record ProductSearchResult(
    string Query,
    string MarketplaceId,
    string MarketplaceName,
    string Currency,
    IReadOnlyList<int> RequestedPages,
    IReadOnlyList<int> CompletedPages,
    int TotalAvailablePages,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<string> Warnings,
    int DuplicateCount,
    long ElapsedMilliseconds,
    IReadOnlyList<SearchMarketplaceSummary>? Marketplaces = null);

public sealed class ProductSearchService
{
    public const int DefaultMarketplaceDelayMs = 3_000;
    public const int DefaultTransportFallbackDelayMs = 4_000;

    public async Task<ProductSearchResult> SearchAsync(
        ProductSearchRequest request,
        IProgress<ProductSearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var domains = ResolveDomains(request);
        if (domains.Count == 1)
        {
            var single = await SearchDomainWithFallbackAsync(
                    request,
                    domains[0],
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            return AnnotateSingle(single);
        }

        return await SearchManyAsync(
                request,
                domains,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProductSearchResult> SearchSingleAsync(
        ProductSearchRequest request,
        IProgress<ProductSearchProgress>? progress = null,
        CancellationToken cancellationToken = default,
        AmazonOptions? optionsOverride = null)
    {
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            throw new ArgumentException("The query cannot be empty.", nameof(request));
        }
        if (query.Length > 200)
        {
            throw new ArgumentException(
                "The query cannot exceed 200 characters.",
                nameof(request));
        }
        if (request.PageDelayMs is < 0 or > 60_000)
        {
            throw new ArgumentException(
                "The delay between pages must be between 0 and 60000 ms.",
                nameof(request));
        }
        if (request.EnrichDelayMs is < 0 or > 60_000)
        {
            throw new ArgumentException(
                "The enrichment delay must be between 0 and 60000 ms.",
                nameof(request));
        }
        if (request.MarketplaceDelayMs is < 0 or > 60_000)
        {
            throw new ArgumentException(
                "The delay between marketplaces must be between 0 and 60000 ms.",
                nameof(request));
        }
        if (request.TransportFallbackDelayMs is < 0 or > 60_000)
        {
            throw new ArgumentException(
                "The transport fallback delay must be between 0 and 60000 ms.",
                nameof(request));
        }

        var marketplace = MarketplaceRegistry.Resolve(request.Domain);
        var selection = request.Page is { } requestedPage
            ? PageSelection.Single(
                Math.Clamp(requestedPage, PageSelection.MinPage, PageSelection.MaxPage))
            : PageSelection.First(Math.Clamp(request.Pages, 1, 5));
        var isDeals = !string.IsNullOrWhiteSpace(request.DealType);
        string? dealFilterId = null;
        if (isDeals)
        {
            if (!DealFilterCatalog.TryParseKind(request.DealType, out var dealKind)
                || !DealFilterCatalog.TryGet(
                    marketplace.Id,
                    dealKind,
                    out dealFilterId))
            {
                throw new ArgumentException(
                    $"Deal type unavailable for {marketplace.DisplayName}: " +
                    $"'{request.DealType}'.",
                    nameof(request));
            }
            if (!string.IsNullOrWhiteSpace(request.DealCategory)
                && request.DealCategory.Any(character => !char.IsDigit(character)))
            {
                throw new ArgumentException(
                    "The category node must be numeric.",
                    nameof(request));
            }
        }
        var deliveryLocation = DeliveryLocation.Normalize(
            request.DeliveryCountry,
            request.DeliveryPostalCode,
            marketplace);
        var options = optionsOverride
            ?? CreateOptions(
                request,
                marketplace,
                deliveryLocation is not null && marketplace.Id == "com"
                    ? AmazonTransport.Curl
                    : null);

        var stopwatch = Stopwatch.StartNew();
        var merged = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var completedPages = new List<int>();
        var warnings = new List<string>();
        var duplicates = 0;
        var totalAvailablePages = 0;

        progress?.Report(new ProductSearchProgress(
            "starting",
            0,
            selection.Pages.Count,
            "Preparing the search"));

        using var client = new AmazonClient(marketplace, options);
        if (deliveryLocation is not null)
        {
            progress?.Report(new ProductSearchProgress(
                "starting",
                0,
                selection.Pages.Count,
                "Setting the delivery location"));
            // The marketplace homepage is often protected by an AWS WAF
            // challenge, while the search/deals page provides the GLOW token
            // and modal. Using the results page as the seed keeps the same
            // number of requests and avoids that failure.
            var glowSeedUrl = isDeals
                ? marketplace.SearchUrl(string.Empty, 1, dealFilterId!)
                : marketplace.SearchUrl(request.Query ?? string.Empty, 1);
            await deliveryLocation
                .ApplyAsync(
                    client,
                    glowSeedUrl,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        var parser = new SearchParser(marketplace);
        for (var index = 0; index < selection.Pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = selection.Pages[index];
            progress?.Report(new ProductSearchProgress(
                "searching",
                index,
                selection.Pages.Count,
                isDeals
                    ? request.Page is null
                        ? $"Fetching page {page} of {selection.Pages.Count} deals"
                        : $"Fetching page {page} of deals"
                    : request.Page is null
                        ? $"Fetching page {page} of {selection.Pages.Count}"
                        : $"Fetching page {page}"));

            try
            {
                var html = isDeals
                    ? await client.DealsPageAsync(
                            dealFilterId!,
                            page,
                            request.DealCategory,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await client.SearchPageSortedAsync(
                            query,
                            page,
                            request.Sort,
                            cancellationToken)
                        .ConfigureAwait(false);
                var parsedTotalPages = SearchPaginationParser.ParseTotalPages(html);
                if (parsedTotalPages is { } available && available > totalAvailablePages)
                {
                    totalAvailablePages = available;
                }
                var pageResults = parser.Parse(html, request.Delivery);
                if (request.NoAds)
                {
                    pageResults = SearchResultFilters.WithoutSponsored(pageResults);
                }

                var newResults = 0;
                foreach (var result in pageResults)
                {
                    if (seen.Add(result.Asin))
                    {
                        merged.Add(result);
                        newResults++;
                    }
                    else
                    {
                        duplicates++;
                    }
                }

                completedPages.Add(page);
                if (pageResults.Count == 0)
                {
                    warnings.Add($"Page {page}: no results.");
                    break;
                }
                if (newResults == 0)
                {
                    warnings.Add($"Page {page}: only results already seen.");
                    break;
                }
            }
            catch (AmazonBlockedException ex)
            {
                if (merged.Count == 0)
                {
                    throw;
                }
                warnings.Add($"Page {page} blocked: {ex.Message}");
                break;
            }
            catch (AmazonNotFoundException ex)
            {
                if (merged.Count == 0)
                {
                    throw;
                }
                warnings.Add($"Page {page}: {ex.Message}");
                break;
            }

            if (index < selection.Pages.Count - 1 && request.PageDelayMs > 0)
            {
                await Task.Delay(request.PageDelayMs, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var missingPrices = request.FillMissingPrices
            && merged.Any(result => string.IsNullOrWhiteSpace(result.Price));
        if ((request.Enrich || missingPrices) && merged.Count > 0)
        {
            progress?.Report(new ProductSearchProgress(
                "enriching",
                completedPages.Count,
                selection.Pages.Count,
                missingPrices && !request.Enrich
                    ? "Filling missing prices"
                    : "Looking up reference prices"));
            var detailsService = new ProductDetailsService();
            var enrichment = await SearchResultEnricher.EnrichAsync(
                    merged,
                    marketplace,
                    client,
                    request.EnrichDelayMs,
                    cancellationToken,
                    fillMissingPrices: request.FillMissingPrices,
                    fillReferences: request.Enrich,
                    detailLoader: (asin, token) =>
                        detailsService.GetBaseAsync(
                            asin,
                            marketplace,
                            client,
                            token,
                            deliveryLocation?.CountryCode,
                            deliveryLocation?.PostalCode))
                .ConfigureAwait(false);
            warnings.AddRange(enrichment.Warnings);
        }

        progress?.Report(new ProductSearchProgress(
            "done",
            completedPages.Count,
            selection.Pages.Count,
            "Search completed"));

        return new ProductSearchResult(
            query,
            marketplace.Id,
            marketplace.DisplayName,
            marketplace.CurrencyCode,
            selection.Pages,
            completedPages,
            totalAvailablePages > 0
                ? totalAvailablePages
                : Math.Max(1, completedPages.Count),
            merged,
            warnings,
            duplicates,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<ProductSearchResult> SearchDomainWithFallbackAsync(
        ProductSearchRequest request,
        string domain,
        IProgress<ProductSearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var singleRequest = request with
        {
            Domain = domain,
            Domains = null,
        };
        var marketplace = MarketplaceRegistry.Resolve(domain);
        var options = CreateOptions(singleRequest, marketplace);

        try
        {
            return await SearchSingleAsync(
                    singleRequest,
                    progress,
                    cancellationToken,
                    options)
                .ConfigureAwait(false);
        }
        catch (AmazonBlockedException)
            when (request.AllowTransportFallback
                && (request.Http?.FallbackToCurl ?? true)
                && options.Transport == AmazonTransport.Http)
        {
            var delay = Math.Clamp(
                request.TransportFallbackDelayMs,
                0,
                60_000);
            progress?.Report(new ProductSearchProgress(
                "searching",
                0,
                1,
                $"HTTP transport blocked: retrying with curl in {FormatDelay(delay)}"));
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ProductSearchProgress(
                "searching",
                0,
                1,
                "Retrying with curl"));
            var curlOptions = CreateOptions(
                singleRequest,
                marketplace,
                AmazonTransport.Curl);
            return await SearchSingleAsync(
                    singleRequest,
                    progress,
                    cancellationToken,
                    curlOptions)
                .ConfigureAwait(false);
        }
    }

    private static AmazonOptions CreateOptions(
        ProductSearchRequest request,
        IMarketplaceProfile marketplace,
        AmazonTransport? transportOverride = null)
    {
        var settings = new CommonSettings
        {
            Domain = request.Domain,
            Timeout = request.TimeoutSeconds,
        };
        return AmazonOptionsFactory.Create(
            settings,
            marketplace,
            transportOverride,
            desktopLayout: !string.IsNullOrWhiteSpace(request.DealType),
            saved: request.Http);
    }

    private static string FormatDelay(int milliseconds) =>
        $"{milliseconds / 1000d:0.#} s";

    private async Task<ProductSearchResult> SearchManyAsync(
        ProductSearchRequest request,
        IReadOnlyList<string> domains,
        IProgress<ProductSearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plannedPages = request.Page is null
            ? Math.Clamp(request.Pages, 1, 5)
            : 1;
        var totalUnits = Math.Max(1, domains.Count * plannedPages);
        var stopwatch = Stopwatch.StartNew();
        var merged = new List<SearchResult>();
        var summaries = new List<SearchMarketplaceSummary>();
        var warnings = new List<string>();
        var requestedPages = new SortedSet<int>();
        var completedPages = new SortedSet<int>();
        var duplicateCount = 0;
        var totalAvailablePages = 0;
        Exception? firstFailure = null;

        for (var index = 0; index < domains.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = domains[index];
            var marketplace = MarketplaceRegistry.Resolve(domain);
            if (index > 0 && request.MarketplaceDelayMs > 0)
            {
                var delay = Math.Clamp(request.MarketplaceDelayMs, 0, 60_000);
                progress?.Report(new ProductSearchProgress(
                    "searching",
                    Math.Min(totalUnits, index * plannedPages),
                    totalUnits,
                    $"Waiting {FormatDelay(delay)} before {marketplace.DisplayName}"));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            progress?.Report(new ProductSearchProgress(
                "starting",
                Math.Min(totalUnits, index * plannedPages),
                totalUnits,
                $"Preparing the search on {marketplace.DisplayName}"));

            try
            {
                var singleRequest = request with
                {
                    Domain = domain,
                    Domains = null,
                };
                var adapter = new CallbackProgress(item =>
                {
                    if (item.Stage == "done")
                    {
                        return;
                    }

                    progress?.Report(new ProductSearchProgress(
                        item.Stage == "starting" ? "searching" : item.Stage,
                        Math.Min(
                            totalUnits,
                            index * plannedPages
                            + Math.Min(item.CompletedPages, plannedPages)),
                        totalUnits,
                        $"{marketplace.DisplayName}: {item.Message}"));
                });
                var single = await SearchDomainWithFallbackAsync(
                        singleRequest,
                        domain,
                        adapter,
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var item in single.Results)
                {
                    item.MarketplaceId = marketplace.Id;
                    item.MarketplaceName = marketplace.DisplayName;
                    item.Currency = marketplace.CurrencyCode;
                }

                merged.AddRange(single.Results);
                summaries.Add(new SearchMarketplaceSummary(
                    marketplace.Id,
                    marketplace.DisplayName,
                    marketplace.CurrencyCode,
                    single.Results.Count,
                    single.TotalAvailablePages,
                    single.CompletedPages,
                    single.Warnings));
                warnings.AddRange(single.Warnings.Select(warning =>
                    $"{marketplace.DisplayName}: {warning}"));
                requestedPages.UnionWith(single.RequestedPages);
                completedPages.UnionWith(single.CompletedPages);
                duplicateCount += single.DuplicateCount;
                totalAvailablePages = Math.Max(
                    totalAvailablePages,
                    single.TotalAvailablePages);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                warnings.Add($"{marketplace.DisplayName}: {ex.Message}");
                summaries.Add(new SearchMarketplaceSummary(
                    marketplace.Id,
                    marketplace.DisplayName,
                    marketplace.CurrencyCode,
                    0,
                    1,
                    [],
                    [ex.Message]));
            }
        }

        if (merged.Count == 0 && firstFailure is not null)
        {
            throw firstFailure;
        }

        var names = domains
            .Select(domain => MarketplaceRegistry.Resolve(domain).DisplayName)
            .ToArray();
        var currencies = domains
            .Select(domain => MarketplaceRegistry.Resolve(domain).CurrencyCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        progress?.Report(new ProductSearchProgress(
            "done",
            totalUnits,
            totalUnits,
            domains.Count == 1
                ? "Search completed"
                : $"Search completed on {domains.Count} marketplaces"));

        return new ProductSearchResult(
            (request.Query ?? string.Empty).Trim(),
            domains[0],
            string.Join(" · ", names),
            currencies.Length == 1 ? currencies[0] : string.Empty,
            requestedPages.ToList(),
            completedPages.ToList(),
            Math.Max(1, totalAvailablePages),
            merged,
            warnings,
            duplicateCount,
            stopwatch.ElapsedMilliseconds,
            summaries);
    }

    private static ProductSearchResult AnnotateSingle(ProductSearchResult result)
    {
        var marketplace = MarketplaceRegistry.Resolve(result.MarketplaceId);
        foreach (var item in result.Results)
        {
            item.MarketplaceId ??= marketplace.Id;
            item.MarketplaceName ??= marketplace.DisplayName;
            item.Currency ??= result.Currency;
        }

        return result with
        {
            Marketplaces =
            [
                new SearchMarketplaceSummary(
                    marketplace.Id,
                    marketplace.DisplayName,
                    marketplace.CurrencyCode,
                    result.Results.Count,
                    result.TotalAvailablePages,
                    result.CompletedPages,
                    result.Warnings),
            ],
        };
    }

    private static IReadOnlyList<string> ResolveDomains(ProductSearchRequest request)
    {
        var raw = request.Domains is { Count: > 0 }
            ? request.Domains
            : [request.Domain ?? string.Empty];
        var domains = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (!MarketplaceRegistry.TryResolve(entry, out var marketplace))
            {
                throw new ArgumentException(
                    $"Unsupported marketplace: '{entry}'.",
                    nameof(request));
            }

            if (seen.Add(marketplace.Id))
            {
                domains.Add(marketplace.Id);
            }
        }

        if (domains.Count == 0)
        {
            domains.Add(MarketplaceRegistry.Default.Id);
        }

        return domains;
    }

    private sealed class CallbackProgress(
        Action<ProductSearchProgress> callback)
        : IProgress<ProductSearchProgress>
    {
        public void Report(ProductSearchProgress value) => callback(value);
    }
}
