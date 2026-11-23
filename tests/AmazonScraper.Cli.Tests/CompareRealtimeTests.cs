using AmazonScraper.Cli.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmazonScraper.Cli.Tests;

public class CompareRealtimeTests
{
    [Fact]
    public async Task Watcher_broadcasts_a_new_comparison_revision()
    {
        using var directory = new TempDirectory();
        var store = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        var notifier = new FakeCompareNotifier();
        using var watcher = new CompareChangeWatcher(
            store,
            notifier,
            NullLogger<CompareChangeWatcher>.Instance);

        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        try
        {
            store.Add([new CompareTarget("it", "B0H25NG1JK")], "cli");

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

    private sealed class FakeCompareNotifier : ICompareRealtimeNotifier
    {
        public TaskCompletionSource<long> Changed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NotifyCompareChangedAsync(
            long revision,
            CancellationToken cancellationToken = default)
        {
            Changed.TrySetResult(revision);
            return Task.CompletedTask;
        }
    }
}
