using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorCsvTests
{
    [Fact]
    public void Item_row_quotes_commas_and_keeps_nullable_fields_empty()
    {
        var item = new MonitorItemObservation
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            Domain = "it",
            Asin = "B000000001",
            Title = "Prodotto, con virgola",
            Price = "28,99 €",
            PriceAmount = 28.99m,
            Currency = "EUR",
            TriggeredRules = ["max_price", "drop_percent:original"],
        };

        var row = MonitorCsv.FormatItem(item);

        Assert.Contains("\"Prodotto, con virgola\"", row);
        Assert.Contains("\"28,99 €\"", row);
        Assert.Contains("max_price|drop_percent:original", row);
        Assert.EndsWith(",,,false", row);
    }

    [Fact]
    public void Item_row_includes_deal_metadata()
    {
        var item = new MonitorItemObservation
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            Domain = "it",
            Asin = "B000000001",
            Deal = "Offerta a tempo",
            DealId = "amzn1.deal.86ef82ae",
            DealEndsText = "Questa offerta termina il 18 novembre 2026",
            DealEndsOn = "2026-11-18",
            DealEndsInDays = 2,
            DealHasCountdown = true,
        };

        var row = MonitorCsv.FormatItem(item);

        Assert.Contains("amzn1.deal.86ef82ae", row);
        Assert.Contains("Questa offerta termina il 18 novembre 2026", row);
        Assert.Contains("2026-11-18", row);
        Assert.EndsWith(",2,true,,,false", row);
        Assert.Contains("deal_ends_on", MonitorCsv.ItemHeader);
    }

    [Fact]
    public void Item_row_includes_prime_fields()
    {
        var item = new MonitorItemObservation
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            Domain = "it",
            Asin = "B000000001",
            Price = "149,99 €",
            PriceAmount = 149.99m,
            PrimePrice = "129,99 €",
            PrimePriceAmount = 129.99m,
            PrimeOnly = true,
        };

        var row = MonitorCsv.FormatItem(item);

        Assert.Contains("\"129,99 €\"", row);
        Assert.EndsWith(",129.99,true", row);
        Assert.Contains("prime_price", MonitorCsv.ItemHeader);
        Assert.Contains("prime_only", MonitorCsv.ItemHeader);
    }

    [Fact]
    public void Group_row_joins_domains_and_rules()
    {
        var group = new MonitorGroupObservation
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            GroupId = "pc-build",
            Name = "PC, build",
            Domains = ["de", "it"],
            Currency = "EUR",
            TotalMax = 1200m,
            Total = 1150m,
            Complete = true,
            ItemsCount = 2,
            AvailableCount = 2,
            MissingCount = 0,
            TriggeredRules = ["total_max"],
        };

        var row = MonitorCsv.FormatGroup(group);

        Assert.Contains("de|it", row);
        Assert.Contains("\"PC, build\"", row);
        Assert.Contains("total_max", row);
    }

    [Fact]
    public void Alert_row_quotes_message()
    {
        var alert = new MonitorAlert
        {
            AlertedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            RuleId = "group:pc-build:total_max",
            Type = "group_total",
            GroupId = "pc-build",
            Currency = "EUR",
            Threshold = 1200m,
            Observed = 1150m,
            Complete = true,
            Message = "Group pc-build: 1.150,00 € <= 1.200,00 €",
        };

        var row = MonitorCsv.FormatAlert(alert);

        Assert.Contains("\"Group pc-build: 1.150,00 € <= 1.200,00 €\"", row);
    }
}
