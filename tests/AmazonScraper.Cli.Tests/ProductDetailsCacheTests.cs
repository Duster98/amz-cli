using AmazonScraper.Cli.Services;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Tests;

public class ProductDetailsCacheTests
{
    [Fact]
    public async Task Reuses_the_cached_detail_until_force_refresh()
    {
        var cache = new ProductDetailsCache(TimeSpan.FromMinutes(1));
        var calls = 0;
        Task<ProductDetailsCache.BaseData> Load(CancellationToken _)
        {
            calls++;
            return Task.FromResult(Data("1.000,00 €"));
        }

        var first = await cache.GetBaseAsync("it:B000000001", Load, false);
        var second = await cache.GetBaseAsync("it:B000000001", Load, false);
        var forced = await cache.GetBaseAsync("it:B000000001", Load, true);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.False(forced.FromCache);
        Assert.Equal(2, calls);
        Assert.Equal("1.000,00 €", second.Data.Detail.Price);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_loader_call()
    {
        var cache = new ProductDetailsCache(TimeSpan.FromMinutes(1));
        var calls = 0;
        async Task<ProductDetailsCache.BaseData> Load(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, token);
            return Data("10,00 €");
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                cache.GetBaseAsync("it:B000000002", Load, false)));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal("10,00 €", result.Data.Detail.Price));
    }

    [Fact]
    public async Task Expired_entry_is_refreshed()
    {
        var cache = new ProductDetailsCache(TimeSpan.FromMilliseconds(40));
        var calls = 0;
        Task<ProductDetailsCache.BaseData> Load(CancellationToken _)
        {
            calls++;
            return Task.FromResult(Data($"{calls},00 €"));
        }

        await cache.GetBaseAsync("it:B000000003", Load, false);
        await Task.Delay(90);
        var refreshed = await cache.GetBaseAsync("it:B000000003", Load, false);

        Assert.Equal(2, calls);
        Assert.Equal("2,00 €", refreshed.Data.Detail.Price);
    }

    [Fact]
    public async Task Failed_refresh_returns_the_previous_value_as_stale()
    {
        var cache = new ProductDetailsCache(TimeSpan.FromMilliseconds(40));
        var calls = 0;
        Task<ProductDetailsCache.BaseData> Load(CancellationToken _)
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult(Data("20,00 €"));
            }

            throw new InvalidOperationException("Amazon non raggiungibile");
        }

        await cache.GetBaseAsync("it:B000000004", Load, false);
        await Task.Delay(90);
        var stale = await cache.GetBaseAsync("it:B000000004", Load, false);

        Assert.True(stale.FromCache);
        Assert.True(stale.Stale);
        Assert.Equal("20,00 €", stale.Data.Detail.Price);
        Assert.NotEmpty(stale.Warnings);
    }

    [Fact]
    public async Task Options_are_loaded_once_and_retried_after_a_failure()
    {
        var cache = new ProductDetailsCache(TimeSpan.FromMinutes(1));
        var calls = 0;
        Task<ProductDetailsCache.BaseData> Load(CancellationToken _) =>
            Task.FromResult(Data("30,00 €"));

        await cache.GetBaseAsync("it:B000000005", Load, false);
        var failed = await cache.EnsureOptionsAsync(
            "it:B000000005",
            (_, warnings, _) =>
            {
                calls++;
                warnings.Add("offerta temporaneamente non disponibile");
                return Task.FromResult(false);
            });
        var retried = await cache.EnsureOptionsAsync(
            "it:B000000005",
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(true);
            });
        var skipped = await cache.EnsureOptionsAsync(
            "it:B000000005",
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(true);
            });

        Assert.False(failed.LoadedNow);
        Assert.True(retried.LoadedNow);
        Assert.False(skipped.LoadedNow);
        Assert.Equal(2, calls);
    }

    private static ProductDetailsCache.BaseData Data(string price) =>
        new(
            new ProductDetail
            {
                Asin = "B000000001",
                Title = "Test",
                Price = price,
            },
            ShouldLoadOptions: true);
}
