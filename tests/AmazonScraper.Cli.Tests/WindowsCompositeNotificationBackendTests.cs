using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class WindowsCompositeNotificationBackendTests
{
    [Fact]
    public async Task Native_success_does_not_use_the_fallback()
    {
        var native = new FakeBackend(true, "native");
        var fallback = new FakeBackend(true, "powershell");
        var backend = new WindowsCompositeNotificationBackend(native, fallback);

        var result = await backend.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, native.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Native_failure_uses_the_fallback()
    {
        var native = new FakeBackend(false, "native");
        var fallback = new FakeBackend(true, "powershell");
        var backend = new WindowsCompositeNotificationBackend(native, fallback);

        var result = await backend.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, native.Calls);
        Assert.Equal(1, fallback.Calls);
        Assert.Contains("fallback", result.Detail);
    }

    [Fact]
    public async Task Both_failures_include_both_errors()
    {
        var native = new FakeBackend(false, "native");
        var fallback = new FakeBackend(false, "powershell");
        var backend = new WindowsCompositeNotificationBackend(native, fallback);

        var result = await backend.SendAsync(Message(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("native", result.Error);
        Assert.Contains("fallback", result.Error);
    }

    private static MonitorNotificationMessage Message() =>
        new("AMZ CLI Monitor", "test", MonitorNotificationSeverity.Info);

    private sealed class FakeBackend(bool success, string detail) : INotificationBackend
    {
        public int Calls { get; private set; }

        public string Name => detail;

        public Task<NotificationSendResult> SendAsync(
            MonitorNotificationMessage message,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(success
                ? new NotificationSendResult(true, detail)
                : new NotificationSendResult(false, Error: $"{detail} failed"));
        }
    }
}
