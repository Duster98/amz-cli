using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class NotificationServiceTests
{
    [Fact]
    public async Task Alert_is_sent_only_when_alerts_are_enabled()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Alerts,
            cooldownSeconds: 3600,
            logger);
        var state = new MonitorState();
        var alert = Alert();

        await service.NotifyAlertAsync(alert, payload: null, CancellationToken.None);
        await service.NotifyErrorDigestAsync(
            [new MonitorErrorEvent("it", "B000000001", "error", "timeout")],
            state,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var message = Assert.Single(backend.Messages);
        Assert.Contains("B000000001", message.Title);
        Assert.Contains("28,99", message.Body);
    }

    [Fact]
    public async Task Error_digest_is_suppressed_for_the_same_set_until_cooldown()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Errors,
            cooldownSeconds: 3600,
            logger);
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");
        var errors = new[]
        {
            new MonitorErrorEvent("it", "B000000001", "error", "timeout"),
        };

        await service.NotifyErrorDigestAsync(errors, state, now, CancellationToken.None);
        Assert.Single(backend.Messages);
        Assert.Equal("errors", backend.Messages[0].Payload?.Kind);

        await service.NotifyErrorDigestAsync(
            errors,
            state,
            now.AddMinutes(10),
            CancellationToken.None);
        Assert.Single(backend.Messages);

        await service.NotifyErrorDigestAsync(
            errors,
            state,
            now.AddHours(2),
            CancellationToken.None);
        Assert.Equal(2, backend.Messages.Count);

        await service.NotifyErrorDigestAsync(
            [new MonitorErrorEvent("it", "B000000002", "not_found", "404")],
            state,
            now.AddHours(2).AddMinutes(1),
            CancellationToken.None);
        Assert.Equal(3, backend.Messages.Count);
    }

    [Fact]
    public async Task Block_and_disabled_notifications_respect_cooldown_and_once_semantics()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Blocks,
            cooldownSeconds: 3600,
            logger);
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        await service.NotifyBlockAsync("it", "HTTP 503", state, now, CancellationToken.None);
        Assert.Equal("block", backend.Messages[0].Payload?.Kind);
        await service.NotifyBlockAsync(
            "it",
            "HTTP 503",
            state,
            now.AddMinutes(5),
            CancellationToken.None);
        Assert.Single(backend.Messages);

        await service.NotifyBlockAsync(
            "it",
            "HTTP 429",
            state,
            now.AddMinutes(6),
            CancellationToken.None);
        Assert.Equal(2, backend.Messages.Count);

        await service.NotifyDomainDisabledAsync(
            "it",
            "3 errori consecutivi",
            state,
            now.AddMinutes(7),
            CancellationToken.None);
        Assert.Equal("domain_disabled", backend.Messages[2].Payload?.Kind);
        await service.NotifyDomainDisabledAsync(
            "it",
            "3 errori consecutivi",
            state,
            now.AddMinutes(8),
            CancellationToken.None);
        Assert.Equal(3, backend.Messages.Count);
    }

    [Fact]
    public async Task Fatal_notification_requires_fatal_event()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Alerts,
            cooldownSeconds: 3600,
            logger);

        await service.NotifyFatalAsync("stop", CancellationToken.None);
        Assert.Empty(backend.Messages);

        var fatal = new NotificationService(
            backend,
            MonitorNotificationEvents.Fatal,
            cooldownSeconds: 3600,
            logger);
        await fatal.NotifyFatalAsync("stop", CancellationToken.None);
        Assert.Single(backend.Messages);
    }

    [Fact]
    public async Task Alert_payload_is_forwarded_to_the_backend()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Alerts,
            cooldownSeconds: 3600,
            logger);
        var payload = new MonitorNotificationPayload
        {
            Kind = "alert",
            Title = "Calo di prezzo rilevato",
        };

        await service.NotifyAlertAsync(Alert(), payload, CancellationToken.None);

        Assert.Same(payload, Assert.Single(backend.Messages).Payload);
    }

    [Fact]
    public async Task Test_notification_has_a_test_payload()
    {
        var backend = new FakeBackend();
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend,
            MonitorNotificationEvents.Alerts,
            cooldownSeconds: 3600,
            logger);

        await service.TestAsync(CancellationToken.None);

        var message = Assert.Single(backend.Messages);
        Assert.Equal("test", message.Payload?.Kind);
    }

    [Fact]
    public async Task Test_without_backend_returns_an_install_hint()
    {
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = new NotificationService(
            backend: null,
            MonitorNotificationEvents.Alerts,
            cooldownSeconds: 3600,
            logger);

        var result = await service.TestAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static MonitorAlert Alert() =>
        new()
        {
            AlertedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            RuleId = "item:it:B000000001:max_price",
            Type = "item_max_price",
            Domain = "it",
            Asin = "B000000001",
            Currency = "EUR",
            Threshold = 30m,
            Observed = 28.99m,
            Message = "it:B000000001: 28,99 € <= 30 EUR (max_price)",
            Url = "https://www.amazon.it/dp/B000000001",
        };

    private sealed class FakeBackend : INotificationBackend
    {
        public List<MonitorNotificationMessage> Messages { get; } = [];

        public string Name => "fake";

        public Task<NotificationSendResult> SendAsync(
            MonitorNotificationMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(new NotificationSendResult(true, "fake"));
        }
    }
}
