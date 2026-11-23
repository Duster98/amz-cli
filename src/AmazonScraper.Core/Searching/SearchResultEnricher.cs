using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Searching;

public sealed record SearchEnrichmentResult(
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Optional detail-page enrichment for search/deals rows that do not expose a
/// reference price on the search card.
/// </summary>
public static class SearchResultEnricher
{
    public static async Task<SearchEnrichmentResult> EnrichAsync(
        IReadOnlyList<SearchResult> results,
        IMarketplaceProfile marketplace,
        AmazonClient client,
        int delayMs,
        CancellationToken cancellationToken = default,
        bool fillMissingPrices = true,
        bool fillReferences = true,
        int maxMissingPriceFetches = 5,
        Func<string, CancellationToken, Task<ProductDetail>>? detailLoader = null)
    {
        var warnings = new List<string>();
        var parser = new DetailParser(marketplace);
        var fetched = 0;
        var missingPriceFetches = 0;

        foreach (var result in results)
        {
            var needsPrice = fillMissingPrices
                && string.IsNullOrWhiteSpace(result.Price)
                && missingPriceFetches < Math.Max(0, maxMissingPriceFetches);
            var needsReference = fillReferences
                && (string.IsNullOrWhiteSpace(result.OriginalPrice)
                    || string.IsNullOrWhiteSpace(result.Discount));
            if (!needsPrice && !needsReference)
            {
                continue;
            }
            if (needsPrice)
            {
                missingPriceFetches++;
            }

            if (fetched > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                ProductDetail detail;
                if (detailLoader is not null)
                {
                    detail = await detailLoader(result.Asin, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var html = await client.DetailAsync(result.Asin, cancellationToken)
                        .ConfigureAwait(false);
                    detail = parser.Parse(html, result.Asin);
                }
                if (string.IsNullOrWhiteSpace(result.Price))
                {
                    result.Price = !string.IsNullOrWhiteSpace(detail.Price)
                        ? detail.Price
                        : detail.BuyingOptions?.LowestPrice ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(result.Condition))
                {
                    result.Condition = detail.BuyingOptions?.Options
                        .Select(option => option.Condition)
                        .FirstOrDefault(condition => !string.IsNullOrWhiteSpace(condition))
                        ?? detail.BuyingOptions?.Condition;
                }
                if (string.IsNullOrWhiteSpace(result.OriginalPrice))
                {
                    result.OriginalPrice = detail.OriginalPrice;
                }
                if (string.IsNullOrWhiteSpace(result.Discount))
                {
                    result.Discount = detail.Discount;
                }
                fetched++;
            }
            catch (AmazonBlockedException ex)
            {
                warnings.Add(
                    $"Enrichment aborted for {result.Asin}: {ex.Message}");
                break;
            }
            catch (AmazonNotFoundException ex)
            {
                warnings.Add($"Enrichment {result.Asin}: {ex.Message}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                warnings.Add($"Enrichment {result.Asin}: {ex.Message}");
            }
            catch (Exception ex)
            {
                warnings.Add($"Enrichment {result.Asin}: {ex.Message}");
            }
        }

        return new SearchEnrichmentResult(results, warnings);
    }
}
