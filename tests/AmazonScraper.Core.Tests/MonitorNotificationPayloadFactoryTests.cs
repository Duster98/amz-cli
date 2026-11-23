using System.Text.Json;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorNotificationPayloadFactoryTests
{
    [Fact]
    public void Item_alert_payload_contains_price_and_product_details()
    {
        var alert = new MonitorAlert
        {
            AlertedAt = "2026-11-15T10:00:00Z",
            RunId = "run",
            Cycle = 1,
            RuleId = "item:it:B000000001:drop_percent:original",
            Type = "item_drop_percent",
            Domain = "it",
            Asin = "B000000001",
            Currency = "EUR",
            Threshold = 33.76m,
            Observed = 28.99m,
            Message = "calo",
        };
        var item = new MonitorItemObservation
        {
            Domain = "it",
            Asin = "B000000001",
            Title = "Prodotto",
            Url = "https://www.amazon.it/dp/B000000001",
            Price = "28,99 €",
            OriginalPrice = "48,23 €",
            Discount = "-40%",
            Deal = "Black Friday",
            DealId = "amzn1.deal.5866291a",
            DealEndsText = "Questa offerta termina il 30 novembre 2026",
            DealEndsOn = "2026-11-30",
            Coupon = "-10%",
            Currency = "EUR",
        };

        var payload = MonitorNotificationPayloadFactory.ItemAlert(
            alert,
            item,
            DateTimeOffset.UtcNow);

        Assert.Equal("alert", payload.Kind);
        Assert.Equal("Price drop detected", payload.Title);
        var row = Assert.Single(payload.Items);
        Assert.Equal("B000000001", row.Asin);
        Assert.Equal("28,99 €", row.Price);
        Assert.Equal("48,23 €", row.OriginalPrice);
        Assert.Equal("-40%", row.Discount);
        Assert.Equal("Black Friday", row.Deal);
        Assert.Equal("amzn1.deal.5866291a", row.DealId);
        Assert.Equal(
            "Questa offerta termina il 30 novembre 2026",
            row.DealEndsText);
        Assert.Equal("2026-11-30", row.DealEndsOn);
    }

    [Fact]
    public void Rise_alert_uses_an_increase_title()
    {
        var alert = new MonitorAlert
        {
            AlertedAt = "2026-11-15T10:00:00Z",
            RunId = "run",
            Cycle = 1,
            RuleId = "item:it:B000000001:rise_percent:first_seen",
            Type = "item_rise_percent",
            Domain = "it",
            Asin = "B000000001",
            Currency = "EUR",
            Threshold = 110m,
            Observed = 112m,
            Message = "rialzo",
        };
        var item = new MonitorItemObservation
        {
            Domain = "it",
            Asin = "B000000001",
            Title = "Prodotto",
            Currency = "EUR",
        };

        var payload = MonitorNotificationPayloadFactory.ItemAlert(
            alert,
            item,
            DateTimeOffset.UtcNow);

        Assert.Equal("Price increase detected", payload.Title);
    }

    [Fact]
    public void Group_payload_enriches_items_from_observations()
    {
        var alert = new MonitorAlert
        {
            AlertedAt = "2026-11-15T10:00:00Z",
            RunId = "run",
            Cycle = 1,
            RuleId = "group:build:total_max",
            Type = "group_total",
            GroupId = "build",
            Currency = "EUR",
            Threshold = 1200m,
            Observed = 1150m,
            Message = "gruppo",
            Complete = true,
        };
        var group = new MonitorGroupObservation
        {
            GroupId = "build",
            Name = "PC Build",
            Total = 1150m,
            TotalMax = 1200m,
            Currency = "EUR",
            Items =
            [
                new MonitorGroupItemObservation
                {
                    Domain = "it",
                    Asin = "B000000001",
                    Quantity = 1,
                    PriceAmount = 1150m,
                    LineTotal = 1150m,
                },
            ],
        };
        var observations = new Dictionary<string, MonitorItemObservation>
        {
            ["it:B000000001"] = new()
            {
                Domain = "it",
                Asin = "B000000001",
                Title = "GPU",
                Price = "1.150,00 €",
                Currency = "EUR",
            },
        };

        var payload = MonitorNotificationPayloadFactory.GroupAlert(
            alert,
            group,
            observations,
            DateTimeOffset.UtcNow);

        Assert.Equal("group_alert", payload.Kind);
        Assert.Equal("PC Build", payload.GroupName);
        Assert.Equal(1150m, payload.GroupTotal);
        Assert.Equal("GPU", Assert.Single(payload.Items).Title);
    }

    [Fact]
    public void Error_digest_payload_lists_errors()
    {
        var payload = MonitorNotificationPayloadFactory.ErrorDigest(
            [
                new MonitorErrorEvent("it", "B000000001", "error", "timeout"),
                new MonitorErrorEvent("de", "B000000002", "not_found", "404"),
            ],
            DateTimeOffset.UtcNow);

        Assert.Equal("errors", payload.Kind);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal("timeout", payload.Items[0].Error);
    }

    [Fact]
    public void Payload_json_uses_the_contract_read_by_the_windows_viewer()
    {
        var payload = MonitorNotificationPayloadFactory.Test(DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"Kind\":\"test\"", json);
        Assert.Contains("\"Title\":", json);
        Assert.Contains("\"Items\":", json);
    }

    [Fact]
    public void Test_payload_is_marked_as_test()
    {
        var payload = MonitorNotificationPayloadFactory.Test(DateTimeOffset.UtcNow);

        Assert.Equal("test", payload.Kind);
        Assert.Equal("Test notification", payload.Title);
    }
}
