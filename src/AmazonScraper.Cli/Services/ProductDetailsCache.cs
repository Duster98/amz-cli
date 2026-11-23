using System.Collections.Concurrent;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Services;

/// <summary>
/// In-memory, single-flight cache for product details. The same entry is shared
/// by the web UI and by search-result price enrichment.
/// </summary>
public sealed class ProductDetailsCache
{
    public static ProductDetailsCache Shared { get; } = new(
        TimeSpan.FromMinutes(15),
        maxEntries: 100);

    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    public ProductDetailsCache(TimeSpan ttl, int maxEntries = 100)
    {
        _ttl = ttl;
        _maxEntries = Math.Max(1, maxEntries);
    }

    public sealed record BaseData(
        ProductDetail Detail,
        bool ShouldLoadOptions);

    public sealed record BaseResult(
        BaseData Data,
        bool FromCache,
        DateTimeOffset FetchedAt,
        bool Stale,
        IReadOnlyList<string> Warnings);

    public sealed record OptionsResult(
        bool LoadedNow,
        IReadOnlyList<string> Warnings);

    public async Task<BaseResult> GetBaseAsync(
        string key,
        Func<CancellationToken, Task<BaseData>> loader,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(key);
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            entry.LastAccess = DateTimeOffset.UtcNow;
            var expired = entry.Data is null
                || DateTimeOffset.UtcNow - entry.FetchedAt >= _ttl;
            var loadedNow = false;

            if (forceRefresh || expired)
            {
                try
                {
                    var data = await loader(cancellationToken).ConfigureAwait(false);
                    entry.Data = data;
                    entry.FetchedAt = DateTimeOffset.UtcNow;
                    entry.OptionsExpanded = false;
                    entry.Stale = false;
                    entry.Warnings.Clear();
                    loadedNow = true;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!forceRefresh && entry.Data is not null)
                {
                    entry.Stale = true;
                    entry.Warnings.Add(
                        $"Refresh failed, using previous data: {ex.Message}");
                }
            }

            if (entry.Data is null)
            {
                throw new InvalidOperationException(
                    "Product detail unavailable.");
            }

            return new BaseResult(
                entry.Data,
                FromCache: !forceRefresh && !loadedNow,
                entry.FetchedAt,
                entry.Stale,
                entry.Warnings.ToArray());
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<OptionsResult> EnsureOptionsAsync(
        string key,
        Func<ProductDetail, List<string>, CancellationToken, Task<bool>> loader,
        CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(key);
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            entry.LastAccess = DateTimeOffset.UtcNow;
            if (entry.Data is null)
            {
                throw new InvalidOperationException(
                    "Product detail unavailable.");
            }
            if (entry.OptionsExpanded || !entry.Data.ShouldLoadOptions)
            {
                return new OptionsResult(false, []);
            }

            var warnings = new List<string>();
            var success = false;
            try
            {
                success = await loader(
                        entry.Data.Detail,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Buying options not loaded: {ex.Message}");
            }

            entry.Warnings.AddRange(warnings);
            if (success)
            {
                entry.OptionsExpanded = true;
            }

            return new OptionsResult(success, warnings);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private Entry GetEntry(string key)
    {
        Trim();
        return _entries.GetOrAdd(key, _ => new Entry());
    }

    private void Trim()
    {
        if (_entries.Count <= _maxEntries)
        {
            return;
        }

        var excess = _entries.Count - _maxEntries;
        foreach (var pair in _entries
                     .OrderBy(item => item.Value.LastAccess)
                     .Take(excess))
        {
            _entries.TryRemove(pair.Key, out _);
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public BaseData? Data { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        public bool OptionsExpanded { get; set; }

        public bool Stale { get; set; }

        public List<string> Warnings { get; } = [];

        public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;
    }
}
