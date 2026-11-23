using System.Collections.Concurrent;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Services;

public interface ISuggestionService
{
    Task<IReadOnlyList<string>> GetAsync(
        IMarketplaceProfile marketplace,
        string prefix,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Short-lived, in-memory cache for search-box suggestions. Suggestions are
/// cheap but typing can produce many prefixes, so entries are keyed by
/// marketplace and normalized prefix and bounded in number.
/// </summary>
public sealed class SuggestionService : ISuggestionService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(20);

    private readonly AmazonSuggestionClient _client;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);

    public SuggestionService(
        AmazonSuggestionClient? client = null,
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        int maxEntries = 300)
    {
        _client = client ?? new AmazonSuggestionClient();
        _time = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? DefaultTtl;
        _maxEntries = Math.Max(10, maxEntries);
    }

    public async Task<IReadOnlyList<string>> GetAsync(
        IMarketplaceProfile marketplace,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var query = AmazonSuggestionClient.NormalizePrefix(prefix);
        var minimumLength = MinimumLengthFor(marketplace.Id);
        if (query.Length < minimumLength)
        {
            return [];
        }

        var key = $"{marketplace.Id}:{query.ToLowerInvariant()}";
        var entry = GetEntry(key);
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            entry.LastAccess = now;
            if (entry.Values is not null
                && now - entry.FetchedAt < _ttl)
            {
                return entry.Values;
            }

            if (entry.FailedAt is { } failedAt
                && now - failedAt < FailureCooldown)
            {
                return entry.Values ?? [];
            }

            try
            {
                var values = await _client
                    .GetAsync(marketplace, query, cancellationToken)
                    .ConfigureAwait(false);
                entry.Values = values;
                entry.FetchedAt = now;
                entry.FailedAt = null;
                return values;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Assisted typing must never turn a network hiccup into an
                // error in the search UI. Keep a short failure cooldown so a
                // dead endpoint is not hammered while the user types.
                entry.FailedAt = now;
                return entry.Values ?? [];
            }
        }
        finally
        {
            entry.Gate.Release();
            Trim();
        }
    }

    private Entry GetEntry(string key)
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        entry.LastAccess = _time.GetUtcNow();
        return entry;
    }

    public static int MinimumLengthFor(string marketplaceId) =>
        string.Equals(marketplaceId, "jp", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 2;

    private void Trim()
    {
        var excess = _entries.Count - _maxEntries;
        if (excess <= 0)
        {
            return;
        }

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

        public IReadOnlyList<string>? Values { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        public DateTimeOffset? FailedAt { get; set; }

        public DateTimeOffset LastAccess { get; set; }
    }
}
