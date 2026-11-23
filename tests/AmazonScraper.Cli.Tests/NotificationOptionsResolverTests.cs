using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class NotificationOptionsResolverTests
{
    [Fact]
    public void Config_can_enable_notifications_and_set_events()
    {
        var options = NotificationOptionsResolver.FromSettings(
            new MonitorSettings(),
            new MonitorConfig
            {
                Notifications = new MonitorNotificationConfig
                {
                    Enabled = true,
                    On = ["alerts", "errors"],
                    CooldownSeconds = 120,
                },
            });

        Assert.True(options.Enabled);
        Assert.Equal("alerts,errors", options.On);
        Assert.Equal(120, options.CooldownSeconds);
    }

    [Fact]
    public void Cli_events_override_config_events()
    {
        var options = NotificationOptionsResolver.FromSettings(
            new MonitorSettings
            {
                Notify = true,
                NotifyOn = "blocks",
                NotifyCooldown = 300,
            },
            new MonitorConfig
            {
                Notifications = new MonitorNotificationConfig
                {
                    Enabled = true,
                    On = ["alerts"],
                    CooldownSeconds = 120,
                },
            });

        Assert.Equal("blocks", options.On);
        Assert.Equal(300, options.CooldownSeconds);
    }

    [Fact]
    public void Notify_on_without_enabling_notifications_is_rejected()
    {
        Assert.Throws<MonitorException>(() =>
            NotificationOptionsResolver.FromSettings(
                new MonitorSettings { NotifyOn = "all" },
                config: null));
    }

    [Fact]
    public void Invalid_config_cooldown_is_rejected()
    {
        Assert.Throws<MonitorException>(() =>
            NotificationOptionsResolver.FromSettings(
                new MonitorSettings(),
                new MonitorConfig
                {
                    Notifications = new MonitorNotificationConfig
                    {
                        Enabled = true,
                        CooldownSeconds = 10,
                    },
                }));
    }
}
