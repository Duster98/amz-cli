using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorNotificationEventsTests
{
    [Fact]
    public void Default_is_alerts()
    {
        Assert.Equal(
            MonitorNotificationEvents.Alerts,
            MonitorNotificationEventsParser.Parse(null));
    }

    [Theory]
    [InlineData("alerts", MonitorNotificationEvents.Alerts)]
    [InlineData("errors", MonitorNotificationEvents.Errors)]
    [InlineData("blocks", MonitorNotificationEvents.Blocks)]
    [InlineData("fatal", MonitorNotificationEvents.Fatal)]
    [InlineData("alerts,errors", MonitorNotificationEvents.Alerts | MonitorNotificationEvents.Errors)]
    [InlineData("all", MonitorNotificationEvents.All)]
    public void Parses_events(string value, MonitorNotificationEvents expected)
    {
        Assert.Equal(expected, MonitorNotificationEventsParser.Parse(value));
    }

    [Fact]
    public void Invalid_event_is_rejected()
    {
        Assert.Throws<MonitorException>(() =>
            MonitorNotificationEventsParser.Parse("price"));
    }

    [Fact]
    public void Formats_all_and_mixed_events()
    {
        Assert.Equal("all", MonitorNotificationEventsParser.Format(MonitorNotificationEvents.All));
        Assert.Equal(
            "alerts,blocks",
            MonitorNotificationEventsParser.Format(
                MonitorNotificationEvents.Alerts | MonitorNotificationEvents.Blocks));
    }
}
