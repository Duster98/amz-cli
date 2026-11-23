using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmazonScraper.Cli.Tests;

public class LibraryRealtimeTests
{
    [Fact]
    public async Task Watcher_broadcasts_a_new_library_revision()
    {
        using var directory = new TempDirectory();
        var store = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var notifier = new FakeNotifier();
        using var watcher = new LibraryChangeWatcher(
            store,
            notifier,
            NullLogger<LibraryChangeWatcher>.Instance);

        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        try
        {
            store.AddOrUpdateProducts(
            [
                new SavedProduct
                {
                    Asin = "B000000001",
                    Domain = "it",
                    Snapshot = new SavedSnapshot
                    {
                        Title = "Realtime test",
                        Currency = "EUR",
                    },
                },
            ]);

            var completed = await Task.WhenAny(
                notifier.Changed.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(notifier.Changed.Task, completed);
            Assert.Equal(1, await notifier.Changed.Task);
        }
        finally
        {
            await watcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Watcher_does_not_broadcast_when_the_revision_is_unchanged()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "library.json");
        var store = new SavedLibraryStore(path);
        store.AddOrUpdateProducts(
        [
            new SavedProduct
            {
                Asin = "B000000001",
                Domain = "it",
                Snapshot = new SavedSnapshot
                {
                    Title = "Realtime test",
                    Currency = "EUR",
                },
            },
        ]);
        var notifier = new FakeNotifier();
        using var watcher = new LibraryChangeWatcher(
            store,
            notifier,
            NullLogger<LibraryChangeWatcher>.Instance);

        await watcher.StartAsync(CancellationToken.None);
        try
        {
            // Let ExecuteAsync initialize its last-revision baseline before
            // triggering the file watcher.
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            await File.WriteAllTextAsync(
                path,
                await File.ReadAllTextAsync(path));
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            Assert.False(notifier.Changed.Task.IsCompleted);
        }
        finally
        {
            await watcher.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FakeNotifier : ILibraryRealtimeNotifier
    {
        public TaskCompletionSource<long> Changed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NotifyLibraryChangedAsync(
            long revision,
            CancellationToken cancellationToken = default)
        {
            Changed.TrySetResult(revision);
            return Task.CompletedTask;
        }
    }
}
