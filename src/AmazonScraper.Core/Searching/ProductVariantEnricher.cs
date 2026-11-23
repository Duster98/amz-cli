using System.Collections.Concurrent;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Searching;

public static class ProductVariantEnricher
{
    /// <summary>
    /// Copies the current product price to the selected variant and fetches
    /// price/availability for the other variant ASINs.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnrichAsync(
        ProductDetail detail,
        IMarketplaceProfile marketplace,
        AmazonClient client,
        int delayMs,
        CancellationToken cancellationToken = default,
        int maxConcurrency = 1)
    {
        if (detail.Variants is not { Items.Count: > 0 } variants)
        {
            return [];
        }

        var warnings = new ConcurrentQueue<string>();
        var parser = new DetailParser(marketplace);
        var pending = new List<ProductVariant>();

        foreach (var item in variants.Items)
        {
            if (item.Selected)
            {
                item.Price = NullIfEmpty(detail.Price);
                item.OriginalPrice = NullIfEmpty(detail.OriginalPrice);
                item.Discount = NullIfEmpty(detail.Discount);
                item.Availability = NullIfEmpty(detail.Availability);
            }
            else
            {
                pending.Add(item);
            }
        }

        if (pending.Count == 0)
        {
            return warnings.ToArray();
        }

        var blocked = 0;

        async Task FetchAsync(ProductVariant item)
        {
            if (Volatile.Read(ref blocked) != 0)
            {
                return;
            }

            try
            {
                var html = await client.DetailAsync(item.Asin, cancellationToken)
                    .ConfigureAwait(false);
                var child = parser.Parse(html, item.Asin);
                item.Price = NullIfEmpty(child.Price);
                item.OriginalPrice = NullIfEmpty(child.OriginalPrice);
                item.Discount = NullIfEmpty(child.Discount);
                item.Availability = NullIfEmpty(child.Availability);
            }
            catch (AmazonBlockedException ex)
            {
                Interlocked.Exchange(ref blocked, 1);
                warnings.Enqueue($"Variant {item.Asin}: {ex.Message}");
            }
            catch (AmazonNotFoundException ex)
            {
                warnings.Enqueue($"Variant {item.Asin}: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Enqueue($"Variant {item.Asin}: {ex.Message}");
            }
        }

        if (maxConcurrency <= 1)
        {
            var fetched = 0;
            foreach (var item in pending)
            {
                if (fetched > 0 && delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }

                await FetchAsync(item).ConfigureAwait(false);
                fetched++;
            }
        }
        else
        {
            using var gate = new SemaphoreSlim(
                Math.Min(maxConcurrency, pending.Count));
            var tasks = pending.Select(async item =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref blocked) != 0)
                    {
                        return;
                    }

                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await FetchAsync(item).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        return warnings.ToArray();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
