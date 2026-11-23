using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

/// <summary>
/// Prefers the native AMZ CLI notification helper, but keeps the existing
/// PowerShell/BurntToast backend as a safety net if the helper fails.
/// </summary>
public sealed class WindowsCompositeNotificationBackend : INotificationBackend
{
    private readonly INotificationBackend _native;
    private readonly INotificationBackend? _fallback;

    public WindowsCompositeNotificationBackend(
        INotificationBackend native,
        INotificationBackend? fallback)
    {
        _native = native;
        _fallback = fallback;
    }

    public string Name => "windows/native";

    public async Task<NotificationSendResult> SendAsync(
        MonitorNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var native = await _native.SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        if (native.Success)
        {
            return native;
        }

        if (_fallback is null)
        {
            return native;
        }

        var fallback = await _fallback.SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        if (fallback.Success)
        {
            return fallback with
            {
                Detail = $"native failed; fallback {fallback.Detail ?? "powershell"}",
            };
        }

        return new NotificationSendResult(
            false,
            Error: $"native: {native.Error ?? "error"}; " +
                   $"fallback: {fallback.Error ?? "error"}");
    }
}
