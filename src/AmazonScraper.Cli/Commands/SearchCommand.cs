using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Searching;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class SearchCommand : AsyncCommand<SearchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchSettings settings, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        HistoryEntry? historyEntry = null;
        HistoryStore? historyStore = null;
        HistoryCacheStore? cacheStore = null;
        var historyCompleted = false;
        void CompleteFailure(string message)
        {
            if (historyEntry is null || historyCompleted)
            {
                return;
            }
            try
            {
                historyStore?.CompleteSearch(
                    historyEntry.Id,
                    HistoryStatus.Failed,
                    null,
                    message);
            }
            catch (HistoryException)
            {
                // Best effort.
            }
            historyCompleted = true;
        }
        try
        {
            var marketplace = MarketplaceRegistry.Resolve(settings.Domain);
            var savedHttp = HttpSettingsStore.Default.Load();
            var options = AmazonOptionsFactory.Create(
                settings,
                marketplace,
                saved: savedHttp);
            string? dealFilterId = null;
            if (settings.Deals)
            {
                if (!DealFilterCatalog.TryParseKind(
                        settings.DealType,
                        out var dealKind))
                {
                    CliConsole.Error(
                        "--deal-type must be all, today, or coupons.");
                    return 1;
                }

                if (!DealFilterCatalog.TryGet(
                        marketplace.Id,
                        dealKind,
                        out var resolvedFilter))
                {
                    var available = DealFilterCatalog.Available(marketplace.Id)
                        .Select(DealFilterCatalog.KindName);
                    CliConsole.Error(
                        $"The deal filter '{DealFilterCatalog.KindName(dealKind)}' " +
                        $"is not available for {marketplace.DisplayName}. " +
                        $"Available: {string.Join(", ", available)}.");
                    return 1;
                }

                dealFilterId = resolvedFilter;
            }

            if (!TryResolvePages(settings, out var pages))
            {
                return 1;
            }

            try
            {
                historyStore = new HistoryStore();
                cacheStore = new HistoryCacheStore();
                historyEntry = historyStore.StartSearch(
                    new HistorySearchInput(
                        settings.Query,
                        marketplace.Id,
                        [marketplace.Id],
                        new HistorySearchFilters
                        {
                            NoAds = settings.NoAds,
                            Delivery = true,
                            Enrich = settings.Enrich,
                            OnlyDeals = settings.Deals,
                            Sort = "relevance",
                        },
                        settings.Agent ? HistorySource.Agent : HistorySource.Cli));
            }
            catch (HistoryException)
            {
                historyEntry = null;
            }

            long Elapsed() =>
                (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            int CompleteSuccess(
                IReadOnlyList<SearchResult> results,
                IReadOnlyList<string> warnings,
                IReadOnlyList<int> completedPages,
                int totalAvailablePages,
                int duplicateCount)
            {
                if (historyEntry is not null && !historyCompleted)
                {
                    try
                    {
                        var payload = new ProductSearchResult(
                            settings.Query,
                            marketplace.Id,
                            marketplace.DisplayName,
                            marketplace.CurrencyCode,
                            pages.Pages,
                            completedPages,
                            totalAvailablePages,
                            results,
                            warnings,
                            duplicateCount,
                            Elapsed());
                        cacheStore?.Save(
                            historyEntry.Id,
                            AmazonScraper.Cli.Web.WebHostFactory
                                .SearchResultNode(payload));
                    }
                    catch (HistoryException)
                    {
                        // History is best effort: never fail a search because
                        // its metadata could not be written.
                    }

                    try
                    {
                        historyStore?.CompleteSearch(
                            historyEntry.Id,
                            HistoryStatus.Succeeded,
                            results.Count,
                            null);
                    }
                    catch (HistoryException)
                    {
                        // Best effort.
                    }

                    try
                    {
                        // If the entry was removed while the search was
                        // running, do not leave an orphan cache file behind.
                        if (historyStore is not null
                            && cacheStore is not null
                            && !historyStore.Contains(historyEntry.Id))
                        {
                            cacheStore.Delete(historyEntry.Id);
                        }
                    }
                    catch (HistoryException)
                    {
                        // Best effort.
                    }
                    historyCompleted = true;
                }
                return WriteOutput(results, settings);
            }

            if (settings.HtmlFile is not null)
            {
                if (pages.Pages.Count > 1)
                {
                    CliConsole.Error("With --html you can only read one page.");
                    return 1;
                }

                var html = await File.ReadAllTextAsync(settings.HtmlFile, cancellationToken).ConfigureAwait(false);
                var results = new SearchParser(marketplace).Parse(html, settings.Delivery);
                if (settings.NoAds)
                {
                    results = SearchResultFilters.WithoutSponsored(results);
                }
                if (settings.SaveHtml is not null)
                {
                    await File.WriteAllTextAsync(settings.SaveHtml, html, cancellationToken).ConfigureAwait(false);
                }

                return CompleteSuccess(
                    results,
                    [],
                    [1],
                    1,
                    0);
            }

            using var client = new SearchClient(marketplace, options);
            var parser = new SearchParser(marketplace);
            var merged = new List<SearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var completedPages = new List<int>();
            var warnings = new List<string>();
            var duplicateCount = 0;
            var multiPage = pages.Pages.Count > 1;
            var transportFallbackUsed = false;

            async Task<string> FetchSearchPageAsync(int page)
            {
                while (true)
                {
                    try
                    {
                        return await client.Client
                            .SearchPageAsync(
                                settings.Query,
                                page,
                                dealFilterId,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (AmazonBlockedException)
                        when (!transportFallbackUsed
                            && savedHttp.FallbackToCurl
                            && !settings.NoTransportFallback
                            && client.Options.Transport == AmazonTransport.Http)
                    {
                        transportFallbackUsed = true;
                        var delay =
                            ProductSearchService.DefaultTransportFallbackDelayMs;
                        CliConsole.Stderr.MarkupLine(
                            "[yellow]HTTP transport blocked: retrying with curl in " +
                            $"{delay / 1000d:0.#} s.[/]");
                        if (delay > 0)
                        {
                            await Task.Delay(delay, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        var fallbackOptions = AmazonOptionsFactory.Create(
                            settings,
                            marketplace,
                            AmazonTransport.Curl,
                            saved: savedHttp);
                        client.Replace(marketplace, fallbackOptions);
                    }
                }
            }

            for (var index = 0; index < pages.Pages.Count; index++)
            {
                var page = pages.Pages[index];

                try
                {
                    var html = await FetchSearchPageAsync(page)
                        .ConfigureAwait(false);

                    if (settings.SaveHtml is not null)
                    {
                        var path = multiPage
                            ? BuildPageHtmlPath(settings.SaveHtml, page)
                            : settings.SaveHtml;
                        await File.WriteAllTextAsync(path, html, cancellationToken).ConfigureAwait(false);
                    }

                    var pageResults = parser.Parse(html, settings.Delivery);
                    if (settings.NoAds)
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
                            duplicateCount++;
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
                        // With --pages we are walking sequentially, so no new
                        // ASINs means we reached the useful end of the list.
                        // With an explicit --page list the user asked for those
                        // pages, so keep going unless the page was empty.
                        if (settings.Pages is not null)
                        {
                            break;
                        }
                    }
                }
                catch (AmazonBlockedException ex)
                {
                    if (merged.Count == 0)
                    {
                        CompleteFailure(ex.Message);
                        CliConsole.Error($"Search error: {ex.Message}");
                        return 1;
                    }

                    warnings.Add($"Page {page} blocked: {ex.Message}");
                    break;
                }
                catch (AmazonNotFoundException ex)
                {
                    if (merged.Count == 0)
                    {
                        CompleteFailure(ex.Message);
                        CliConsole.Error($"Search error: {ex.Message}");
                        return 1;
                    }

                    warnings.Add($"Page {page}: {ex.Message}");
                    break;
                }

                if (index < pages.Pages.Count - 1 && settings.PageDelay > 0)
                {
                    await Task.Delay(settings.PageDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            if (settings.Enrich && merged.Count > 0)
            {
                var enrichment = await SearchResultEnricher.EnrichAsync(
                        merged,
                        marketplace,
                        client.Client,
                        settings.EnrichDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
                warnings.AddRange(enrichment.Warnings);
            }

            foreach (var warning in warnings)
            {
                CliConsole.Stderr.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
            }

            if (multiPage)
            {
                var readPages = completedPages.Count == 0
                    ? "none"
                    : string.Join(",", completedPages);
                var duplicates = duplicateCount > 0
                    ? $", {duplicateCount} duplicates removed"
                    : string.Empty;
                CliConsole.Stderr.MarkupLine(
                    $"[grey]Pages read: {readPages}; unique results: {merged.Count}{duplicates}.[/]");
            }

            return CompleteSuccess(
                merged,
                warnings,
                completedPages,
                completedPages.Count == 0 ? 0 : completedPages.Max(),
                duplicateCount);
        }
        catch (MarketplaceException ex)
        {
            CompleteFailure(ex.Message);
            CliConsole.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CompleteFailure(ex.Message);
            CliConsole.Error($"Search error: {ex.Message}");
            return 1;
        }
    }

    private static bool TryResolvePages(SearchSettings settings, out PageSelection pages)
    {
        pages = PageSelection.Single(1);

        if (settings.Pages is { } count)
        {
            if (count is < PageSelection.MinPage or > PageSelection.MaxPage)
            {
                CliConsole.Error(
                    $"--pages must be between {PageSelection.MinPage} and {PageSelection.MaxPage}.");
                return false;
            }

            pages = PageSelection.First(count);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(settings.Page))
        {
            if (!PageSelection.TryParse(settings.Page, out pages, out var error))
            {
                CliConsole.Error(error);
                return false;
            }
        }

        return true;
    }

    private static int WriteOutput(IReadOnlyList<SearchResult> results, SearchSettings settings)
    {
        if (results.Count == 0)
        {
            Console.Out.WriteLine("No results found.");
            return 0;
        }

        switch (OutputModeSelector.Pick(settings.Json, settings.TkOpt))
        {
            case OutputMode.Json:
                Console.Out.WriteLine(JsonFormatter.Search(results));
                break;
            case OutputMode.Tsv:
                Console.Out.WriteLine(TsvFormatter.Search(results, settings.Delivery));
                break;
            default:
                SearchTableRenderer.Render(CliConsole.Stdout(settings.Width), results, settings.Delivery);
                break;
        }

        return 0;
    }

    private static string BuildPageHtmlPath(string path, int page)
    {
        var directory = Path.GetDirectoryName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var fileName = $"{stem}-p{page}{extension}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Keeps the active HTTP client replaceable: a blocked HTTP search can be
    /// retried with curl-impersonate without leaking the previous handler.
    /// </summary>
    private sealed class SearchClient : IDisposable
    {
        public SearchClient(
            IMarketplaceProfile marketplace,
            AmazonOptions options)
        {
            Client = new AmazonClient(marketplace, options);
            Options = options;
        }

        public AmazonClient Client { get; private set; }

        public AmazonOptions Options { get; private set; }

        public void Replace(
            IMarketplaceProfile marketplace,
            AmazonOptions options)
        {
            Client.Dispose();
            Client = new AmazonClient(marketplace, options);
            Options = options;
        }

        public void Dispose() => Client.Dispose();
    }
}
