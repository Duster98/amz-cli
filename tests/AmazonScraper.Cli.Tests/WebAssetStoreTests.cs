using AmazonScraper.Cli.Web;

namespace AmazonScraper.Cli.Tests;

public class WebAssetStoreTests
{
    [Fact]
    public void Loads_a_vite_build_from_disk_with_cache_metadata()
    {
        using var directory = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Root, "assets"));
        File.WriteAllText(
            Path.Combine(directory.Root, "index.html"),
            "<!doctype html><title>test</title>");
        File.WriteAllText(
            Path.Combine(directory.Root, "assets", "app.js"),
            "console.log('ok')");

        var store = WebAssetStore.Load(directory.Root);

        Assert.True(store.IsAvailable);
        Assert.False(store.IsFallback);
        Assert.True(store.TryGetIndex(out var index));
        Assert.Contains("test", System.Text.Encoding.UTF8.GetString(index.Content));
        Assert.False(index.Immutable);
        Assert.True(store.TryGet("assets/app.js", out var script));
        Assert.Equal("text/javascript; charset=utf-8", script.ContentType);
        Assert.True(script.Immutable);
        Assert.StartsWith("\"", script.ETag);
    }

    [Fact]
    public void Serves_a_helpful_fallback_when_the_frontend_is_missing()
    {
        using var directory = new TempDirectory();
        var store = WebAssetStore.Load(directory.Root);

        Assert.True(store.IsAvailable);
        Assert.True(store.IsFallback);
        Assert.True(store.TryGetIndex(out var index));
        Assert.Contains(
            "Web interface not built",
            System.Text.Encoding.UTF8.GetString(index.Content));
    }
}
