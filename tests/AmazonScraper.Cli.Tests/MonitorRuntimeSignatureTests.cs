using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorRuntimeSignatureTests
{
    [Fact]
    public void Notification_events_only_matter_when_notifications_are_enabled()
    {
        var disabledAlerts = Compute(notify: false, ["alerts"]);
        var disabledErrors = Compute(notify: false, ["errors"]);
        Assert.Equal(disabledAlerts, disabledErrors);

        var enabledAlerts = Compute(notify: true, ["alerts"]);
        var enabledErrors = Compute(notify: true, ["errors"]);
        Assert.NotEqual(enabledAlerts, enabledErrors);
        Assert.NotEqual(disabledAlerts, enabledAlerts);
    }

    [Fact]
    public void Runtime_changes_produce_a_new_signature()
    {
        var baseline = Compute(notify: true, ["alerts"]);
        var differentTimer = MonitorRuntimeSignature.Compute(
            900,
            10,
            0,
            MonitorFormat.JsonLines,
            MonitorLayout.Combined,
            true,
            ["alerts"],
            null,
            false,
            "/tmp/monitor",
            "amz-monitor");

        Assert.NotEqual(baseline, differentTimer);
    }

    private static string Compute(
        bool notify,
        IReadOnlyList<string> events) =>
        MonitorRuntimeSignature.Compute(
            600,
            10,
            0,
            MonitorFormat.JsonLines,
            MonitorLayout.Combined,
            notify,
            events,
            null,
            false,
            "/tmp/monitor",
            "amz-monitor");
}
