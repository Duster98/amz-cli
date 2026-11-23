using System.Security.Cryptography;
using System.Text;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

public sealed class NotificationService
{
    private readonly INotificationBackend? _backend;
    private readonly MonitorNotificationEvents _events;
    private readonly int _cooldownSeconds;
    private readonly MonitorLogger _logger;

    public NotificationService(
        INotificationBackend? backend,
        MonitorNotificationEvents events,
        int cooldownSeconds,
        MonitorLogger logger)
    {
        _backend = backend;
        _events = events;
        _cooldownSeconds = cooldownSeconds;
        _logger = logger;
    }

    public string BackendName => _backend?.Name ?? "none";

    public bool Enabled =>
        _backend is not null && _events != MonitorNotificationEvents.None;

    public MonitorNotificationEvents Events => _events;

    public static NotificationService Create(
        NotificationOptions options,
        MonitorLogger logger)
    {
        var events = MonitorNotificationEventsParser.Parse(options.On);
        if (!options.Enabled)
        {
            return new NotificationService(null, events, options.CooldownSeconds, logger);
        }

        var backend = NotificationBackendFactory.Create();
        if (backend is null)
        {
            logger.Warn(
                "No desktop notification backend available. " +
                NotificationBackendFactory.GetInstallHint());
        }
        else
        {
            logger.Info(
                $"Desktop notifications enabled: {backend.Name} " +
                $"({MonitorNotificationEventsParser.Format(events)}).");
        }

        return new NotificationService(
            backend,
            events,
            options.CooldownSeconds,
            logger);
    }

    public async Task<NotificationSendResult> TestAsync(
        CancellationToken cancellationToken)
    {
        if (_backend is null)
        {
            return new NotificationSendResult(
                false,
                Error: NotificationBackendFactory.GetInstallHint());
        }

        var payload = MonitorNotificationPayloadFactory.Test(DateTimeOffset.UtcNow);
        return await _backend.SendAsync(
                new MonitorNotificationMessage(
                    "AMZ CLI Monitor",
                    "Test notification: it works.",
                    MonitorNotificationSeverity.Info,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyAlertAsync(
        MonitorAlert alert,
        MonitorNotificationPayload? payload,
        CancellationToken cancellationToken)
    {
        if (!_events.HasFlag(MonitorNotificationEvents.Alerts))
        {
            return;
        }

        var title = string.Equals(
            alert.Type,
            "group_total",
            StringComparison.Ordinal)
            ? $"AMZ CLI alert: group {alert.GroupId}"
            : $"AMZ CLI alert: {alert.Domain} {alert.Asin}";
        await SendAsync(
                new MonitorNotificationMessage(
                    title,
                    Short(alert.Message, 300),
                    MonitorNotificationSeverity.Warning,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyErrorDigestAsync(
        IReadOnlyList<MonitorErrorEvent> errors,
        MonitorState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_events.HasFlag(MonitorNotificationEvents.Errors)
            || errors.Count == 0)
        {
            return;
        }

        state.Notifications ??= new MonitorNotificationState();
        state.Notifications.Domains ??= [];

        var key = BuildErrorKey(errors);
        if (string.Equals(
                state.Notifications.LastErrorDigestKey,
                key,
                StringComparison.Ordinal)
            && state.Notifications.LastErrorDigestAt is { } last
            && now - last < TimeSpan.FromSeconds(_cooldownSeconds))
        {
            return;
        }

        state.Notifications.LastErrorDigestKey = key;
        state.Notifications.LastErrorDigestAt = now;

        var lines = errors
            .OrderBy(error => error.Domain, StringComparer.Ordinal)
            .ThenBy(error => error.Asin, StringComparer.Ordinal)
            .Take(6)
            .Select(error =>
                $"{error.Domain} {error.Asin}: {ShortLine(error.Message, 90)}")
            .ToList();
        if (errors.Count > 6)
        {
            lines.Add($"... and {errors.Count - 6} more");
        }

        var title = errors.Count == 1
            ? "AMZ CLI Monitor: 1 error"
            : $"AMZ CLI Monitor: {errors.Count} errors";
        var payload = MonitorNotificationPayloadFactory.ErrorDigest(errors, now);
        await SendAsync(
                new MonitorNotificationMessage(
                    title,
                    Short(string.Join('\n', lines), 450),
                    MonitorNotificationSeverity.Warning,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyBlockAsync(
        string domain,
        string reason,
        MonitorState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_events.HasFlag(MonitorNotificationEvents.Blocks))
        {
            return;
        }

        state.Notifications ??= new MonitorNotificationState();
        state.Notifications.Domains ??= [];
        if (!state.Notifications.Domains.TryGetValue(domain, out var domainState))
        {
            domainState = new MonitorDomainNotificationState();
            state.Notifications.Domains[domain] = domainState;
        }

        if (string.Equals(domainState.LastBlockMessage, reason, StringComparison.Ordinal)
            && domainState.LastBlockAt is { } last
            && now - last < TimeSpan.FromSeconds(_cooldownSeconds))
        {
            return;
        }

        domainState.LastBlockAt = now;
        domainState.LastBlockMessage = reason;
        var payload = MonitorNotificationPayloadFactory.Event(
            "block",
            "Domain blocked",
            $"{domain}: {reason}",
            now);
        await SendAsync(
                new MonitorNotificationMessage(
                    "AMZ CLI Monitor: domain blocked",
                    Short($"{domain}: {reason}", 300),
                    MonitorNotificationSeverity.Error,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyDomainDisabledAsync(
        string domain,
        string reason,
        MonitorState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_events.HasFlag(MonitorNotificationEvents.Blocks))
        {
            return;
        }

        state.Notifications ??= new MonitorNotificationState();
        state.Notifications.Domains ??= [];
        if (!state.Notifications.Domains.TryGetValue(domain, out var domainState))
        {
            domainState = new MonitorDomainNotificationState();
            state.Notifications.Domains[domain] = domainState;
        }

        if (domainState.DisabledAt is not null)
        {
            return;
        }

        domainState.DisabledAt = now;
        var payload = MonitorNotificationPayloadFactory.Event(
            "domain_disabled",
            "Domain disabled",
            $"{domain}: {reason}",
            now);
        await SendAsync(
                new MonitorNotificationMessage(
                    "AMZ CLI Monitor: domain disabled",
                    Short($"{domain}: {reason}", 300),
                    MonitorNotificationSeverity.Error,
                    payload),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task NotifyFatalAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (!_events.HasFlag(MonitorNotificationEvents.Fatal))
        {
            return Task.CompletedTask;
        }

        var payload = MonitorNotificationPayloadFactory.Event(
            "fatal",
            "Monitor stopped",
            reason,
            DateTimeOffset.UtcNow);
        return SendAsync(
            new MonitorNotificationMessage(
                "AMZ CLI Monitor: stopped",
                Short(reason, 300),
                MonitorNotificationSeverity.Error,
                payload),
            cancellationToken);
    }

    private async Task<bool> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken)
    {
        if (_backend is null)
        {
            return false;
        }

        try
        {
            var result = await _backend.SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            if (result.Success)
            {
                return true;
            }

            _logger.Warn(
                $"Notification not sent ({BackendName}): " +
                (result.Error ?? "unknown error"));
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Notification not sent ({BackendName}): {ex.Message}");
            return false;
        }
    }

    private static string BuildErrorKey(IEnumerable<MonitorErrorEvent> errors)
    {
        var text = string.Join(
            '|',
            errors.OrderBy(error => error.Domain, StringComparer.Ordinal)
                .ThenBy(error => error.Asin, StringComparer.Ordinal)
                .Select(error => $"{error.Domain}:{error.Asin}:{error.Kind}"));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }

    private static string Short(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 1)] + "…";
    }

    private static string ShortLine(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 1)] + "…";
    }
}
