using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Searching;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

/// <summary>
/// Queryless deals listing. Amazon exposes the active offers through the
/// marketplace-specific <c>p_n_deal_type</c> refinement, so the normal search
/// parser and renderers can be reused.
/// </summary>
public sealed class DealsCommand : AsyncCommand<DealsSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        DealsSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var marketplace = MarketplaceRegistry.Resolve(settings.Domain);
            var options = AmazonOptionsFactory.Create(
                settings,
                marketplace,
                desktopLayout: true,
                saved: HttpSettingsStore.Default.Load());

            if (!DealFilterCatalog.TryParseKind(settings.DealType, out var dealKind))
            {
                CliConsole.Error("--deal-type must be all, today, or coupons.");
                return 1;
            }

            if (!DealFilterCatalog.TryGet(marketplace.Id, dealKind, out var filterId))
            {
                var available = DealFilterCatalog.Available(marketplace.Id)
                    .Select(DealFilterCatalog.KindName);
                CliConsole.Error(
                    $"The deal filter '{DealFilterCatalog.KindName(dealKind)}' " +
                    $"is not available for {marketplace.DisplayName}. " +
                    $"Available: {string.Join(", ", available)}.");
                return 1;
            }

            if (!TryResolvePages(settings, out var pages))
            {
                return 1;
            }

            if (settings.HtmlFile is not null)
            {
                if (pages.Pages.Count > 1)
                {
                    CliConsole.Error("With --html you can only read one page.");
                    return 1;
                }

                var html = await File.ReadAllTextAsync(settings.HtmlFile, cancellationToken)
                    .ConfigureAwait(false);
                var results = new SearchParser(marketplace).Parse(html, settings.Delivery);
                if (settings.NoAds)
                {
                    results = SearchResultFilters.WithoutSponsored(results);
                }
                if (settings.SaveHtml is not null)
                {
                    await File.WriteAllTextAsync(settings.SaveHtml, html, cancellationToken)
                        .ConfigureAwait(false);
                }

                return WriteOutput(ApplyLimit(results, settings), settings);
            }

            using var client = new AmazonClient(marketplace, options);
            var parser = new SearchParser(marketplace);
            var merged = new List<SearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var completedPages = new List<int>();
            var warnings = new List<string>();
            var duplicateCount = 0;
            var multiPage = pages.Pages.Count > 1;

            DealCategory? category = null;
            if (settings.Category is { } categoryQuery)
            {
                var discoveryHtml = await client
                    .DealsPageAsync(filterId, 1, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var categories = DealCategoryCatalog.Parse(discoveryHtml);
                if (!DealCategoryCatalog.TryResolve(categories, categoryQuery, out var resolved))
                {
                    var available = categories.Count == 0
                        ? "no categories detected"
                        : string.Join(", ", categories.Select(item => item.Label));
                    CliConsole.Error(
                        $"Category '{categoryQuery}' not found for " +
                        $"{marketplace.DisplayName}. Available: {available}.");
                    return 1;
                }
                category = resolved;
            }

            for (var index = 0; index < pages.Pages.Count; index++)
            {
                var page = pages.Pages[index];

                try
                {
                    var html = await client
                        .DealsPageAsync(filterId, page, category?.NodeId, cancellationToken)
                        .ConfigureAwait(false);

                    if (settings.SaveHtml is not null)
                    {
                        var path = multiPage
                            ? BuildPageHtmlPath(settings.SaveHtml, page)
                            : settings.SaveHtml;
                        await File.WriteAllTextAsync(path, html, cancellationToken)
                            .ConfigureAwait(false);
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
                        warnings.Add($"Page {page}: no deals.");
                        break;
                    }

                    if (newResults == 0 && settings.Pages is not null)
                    {
                        warnings.Add($"Page {page}: only deals already seen.");
                        break;
                    }
                }
                catch (AmazonBlockedException ex)
                {
                    if (merged.Count == 0)
                    {
                        CliConsole.Error($"Deals error: {ex.Message}");
                        return 1;
                    }

                    warnings.Add($"Page {page} blocked: {ex.Message}");
                    break;
                }
                catch (AmazonNotFoundException ex)
                {
                    if (merged.Count == 0)
                    {
                        CliConsole.Error($"Deals error: {ex.Message}");
                        return 1;
                    }

                    warnings.Add($"Page {page}: {ex.Message}");
                    break;
                }

                if (index < pages.Pages.Count - 1 && settings.PageDelay > 0)
                {
                    await Task.Delay(settings.PageDelay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var output = ApplyLimit(merged, settings);
            if (settings.Enrich && output.Count > 0)
            {
                var enrichment = await SearchResultEnricher.EnrichAsync(
                        output,
                        marketplace,
                        client,
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
                    $"[grey]Pages read: {readPages}; unique deals: {merged.Count}{duplicates}.[/]");
            }

            return WriteOutput(output, settings);
        }
        catch (MarketplaceException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CliConsole.Error($"Deals error: {ex.Message}");
            return 1;
        }
    }

    private static bool TryResolvePages(DealsSettings settings, out PageSelection pages)
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

    private static List<SearchResult> ApplyLimit(
        IReadOnlyList<SearchResult> results,
        DealsSettings settings)
    {
        if (settings.Limit is not { } limit || results.Count <= limit)
        {
            return results.ToList();
        }

        CliConsole.Stderr.MarkupLine(
            $"[grey]Showing {limit} of {results.Count} deals.[/]");
        return results.Take(limit).ToList();
    }

    private static int WriteOutput(
        IReadOnlyList<SearchResult> results,
        DealsSettings settings)
    {
        if (results.Count == 0)
        {
            Console.Out.WriteLine("No deals found.");
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
                SearchTableRenderer.Render(
                    CliConsole.Stdout(settings.Width),
                    results,
                    settings.Delivery);
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
}
