using AmazonScraper.Cli.Commands;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring.Notifications;

public sealed record NotificationOptions(
    bool Enabled,
    string? On,
    int CooldownSeconds);

public static class NotificationOptionsResolver
{
    public static NotificationOptions FromSettings(
        MonitorSettings settings,
        MonitorConfig? config,
        MonitorRuntimeSettings? runtime = null)
    {
        var notifications = config?.Notifications;
        var enabled = settings.Notify
            || runtime?.Notify == true
            || notifications?.Enabled == true;
        var on = settings.NotifyOn;
        if (string.IsNullOrWhiteSpace(on)
            && runtime?.NotifyOn is { Count: > 0 } runtimeEvents)
        {
            on = string.Join(',', runtimeEvents);
        }
        if (string.IsNullOrWhiteSpace(on)
            && notifications?.On is { Count: > 0 } configured)
        {
            on = string.Join(',', configured);
        }

        var cooldown = settings.NotifyCooldown
            ?? runtime?.NotifyCooldownSeconds
            ?? notifications?.CooldownSeconds
            ?? 3600;
        ValidateCooldown(cooldown);
        if (!string.IsNullOrWhiteSpace(on))
        {
            _ = MonitorNotificationEventsParser.Parse(on);
        }

        if (settings.NotifyOn is not null && !enabled)
        {
            throw new MonitorException(
                "--notify-on requires --notify or " +
                "notifications.enabled=true in the configuration.");
        }

        return new NotificationOptions(enabled, on, cooldown);
    }

    public static NotificationOptions ForTest(MonitorSettings settings)
    {
        var cooldown = settings.NotifyCooldown ?? 3600;
        ValidateCooldown(cooldown);
        if (!string.IsNullOrWhiteSpace(settings.NotifyOn))
        {
            _ = MonitorNotificationEventsParser.Parse(settings.NotifyOn);
        }

        return new NotificationOptions(true, settings.NotifyOn, cooldown);
    }

    private static void ValidateCooldown(int cooldown)
    {
        if (cooldown is < 60 or > 604_800)
        {
            throw new MonitorException(
                "notifications.cooldown_seconds/--notify-cooldown " +
                "must be between 60 and 604800 seconds.");
        }
    }
}
