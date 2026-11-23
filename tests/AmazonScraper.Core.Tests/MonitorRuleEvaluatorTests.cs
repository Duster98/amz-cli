using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorRuleEvaluatorTests
{
    [Fact]
    public void Max_price_alerts_on_the_first_observation_and_respects_cooldown()
    {
        var plan = Target(new MonitorRule { MaxPrice = 30m });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        var first = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 29m),
            state,
            "run",
            1,
            now);
        Assert.Single(first.Alerts);
        Assert.Contains("max_price", first.TriggeredRules);

        var second = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 29m),
            state,
            "run",
            2,
            now.AddHours(1));
        Assert.Empty(second.Alerts);
        Assert.Contains("max_price", second.TriggeredRules);

        var afterCooldown = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 29m),
            state,
            "run",
            3,
            now.AddHours(7));
        Assert.Single(afterCooldown.Alerts);
    }

    [Fact]
    public void Condition_that_goes_false_and_true_alerts_again()
    {
        var plan = Target(new MonitorRule { MaxPrice = 30m });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        _ = MonitorRuleEvaluator.EvaluateItem(plan, Item(plan, 29m), state, "run", 1, now);
        var noCondition = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 35m),
            state,
            "run",
            2,
            now.AddMinutes(1));
        Assert.Empty(noCondition.TriggeredRules);

        var again = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 29m),
            state,
            "run",
            3,
            now.AddMinutes(2));
        Assert.Single(again.Alerts);
    }

    [Fact]
    public void Drop_percent_uses_original_price()
    {
        var plan = Target(new MonitorRule
        {
            DropPercent = 30m,
            Baseline = PriceBaseline.Original,
        });
        var state = new MonitorState();

        var result = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 69m, original: 100m),
            state,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.Contains("drop_percent:original", result.TriggeredRules);
        var alert = Assert.Single(result.Alerts);
        Assert.Equal(70m, alert.Threshold);
        Assert.Equal(69m, alert.Observed);
    }

    [Fact]
    public void First_seen_does_not_alert_on_the_first_observation()
    {
        var plan = Target(new MonitorRule
        {
            DropPercent = 30m,
            Baseline = PriceBaseline.FirstSeen,
        });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        var first = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 100m),
            state,
            "run",
            1,
            now);
        Assert.Empty(first.Alerts);
        Assert.Empty(first.TriggeredRules);

        var second = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 69m),
            state,
            "run",
            2,
            now.AddHours(1));
        Assert.Single(second.Alerts);
        Assert.Contains("drop_percent:first_seen", second.TriggeredRules);
    }

    [Fact]
    public void Rise_percent_alerts_above_the_baseline()
    {
        var plan = Target(new MonitorRule
        {
            RisePercent = 10m,
            Baseline = PriceBaseline.Original,
        });
        var state = new MonitorState();

        var result = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 111m, original: 100m),
            state,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.Contains("rise_percent:original", result.TriggeredRules);
        var alert = Assert.Single(result.Alerts);
        Assert.Equal("item_rise_percent", alert.Type);
        Assert.Equal(110m, alert.Threshold);
        Assert.Equal(111m, alert.Observed);
    }

    [Fact]
    public void Rise_percent_with_first_seen_starts_from_the_first_price()
    {
        var plan = Target(new MonitorRule
        {
            RisePercent = 10m,
            Baseline = PriceBaseline.FirstSeen,
        });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        var first = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 100m),
            state,
            "run",
            1,
            now);
        Assert.Empty(first.Alerts);

        var below = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 109m),
            state,
            "run",
            2,
            now.AddMinutes(10));
        Assert.Empty(below.Alerts);

        var above = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 110m),
            state,
            "run",
            3,
            now.AddMinutes(20));
        var alert = Assert.Single(above.Alerts);
        Assert.Equal(110m, alert.Threshold);
    }

    [Fact]
    public void Drop_and_rise_rules_can_be_active_together()
    {
        var plan = Target(new MonitorRule
        {
            DropPercent = 5m,
            RisePercent = 10m,
            Baseline = PriceBaseline.Original,
        });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        var drop = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 95m, original: 100m),
            state,
            "run",
            1,
            now);
        Assert.Contains("drop_percent:original", drop.TriggeredRules);
        Assert.DoesNotContain("rise_percent:original", drop.TriggeredRules);

        var rise = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 110m, original: 100m),
            state,
            "run",
            2,
            now.AddMinutes(10));
        Assert.DoesNotContain("drop_percent:original", rise.TriggeredRules);
        Assert.Contains("rise_percent:original", rise.TriggeredRules);
        Assert.Equal("item_rise_percent", Assert.Single(rise.Alerts).Type);
    }

    [Fact]
    public void Max_seen_compares_against_the_historical_peak()
    {
        var plan = Target(new MonitorRule
        {
            DropPercent = 30m,
            Baseline = PriceBaseline.MaxSeen,
        });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        _ = MonitorRuleEvaluator.EvaluateItem(plan, Item(plan, 100m), state, "run", 1, now);
        var result = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 69m),
            state,
            "run",
            2,
            now.AddHours(1));

        Assert.Single(result.Alerts);
    }

    [Fact]
    public void Historical_bounds_and_currency_are_tracked()
    {
        var plan = Target(new MonitorRule());
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        _ = MonitorRuleEvaluator.EvaluateItem(plan, Item(plan, 100m), state, "run", 1, now);
        _ = MonitorRuleEvaluator.EvaluateItem(
            plan, Item(plan, 80m), state, "run", 2, now.AddHours(1));
        _ = MonitorRuleEvaluator.EvaluateItem(
            plan, Item(plan, 120m), state, "run", 3, now.AddHours(2));

        var target = state.Targets[plan.Target.Key];
        Assert.Equal(100m, target.FirstSeenPrice);
        Assert.Equal(80m, target.MinSeenPrice);
        Assert.Equal(120m, target.MaxSeenPrice);
        Assert.Equal(120m, target.LastPrice);
        Assert.Equal("EUR", target.Currency);
        Assert.Equal(3, target.Observations);
    }

    [Fact]
    public void Group_bounds_and_metadata_are_tracked()
    {
        var group = BuildGroup(totalMax: 250m, allowIncomplete: false);
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        var first = MonitorObservationFactory.FromGroup(
            group,
            new Dictionary<string, MonitorItemObservation>
            {
                ["it:B000000001"] = Item(group.Items[0], 100m),
                ["it:B000000002"] = Item(group.Items[1], 60m),
            },
            "run",
            1,
            now);
        _ = MonitorRuleEvaluator.EvaluateGroup(group, first, state, "run", 1, now);

        var second = MonitorObservationFactory.FromGroup(
            group,
            new Dictionary<string, MonitorItemObservation>
            {
                ["it:B000000001"] = Item(group.Items[0], 50m),
                ["it:B000000002"] = Item(group.Items[1], 40m),
            },
            "run",
            2,
            now.AddHours(1));
        _ = MonitorRuleEvaluator.EvaluateGroup(group, second, state, "run", 2, now.AddHours(1));

        var groupState = state.Groups[group.Id];
        Assert.Equal("Group", groupState.Name);
        Assert.Equal("EUR", groupState.Currency);
        Assert.Equal(2, groupState.Items);
        Assert.Equal(130m, groupState.MinTotal);
        Assert.Equal(220m, groupState.MaxTotal);
        Assert.Equal(130m, groupState.LastTotal);
        Assert.Equal(2, groupState.Observations);
    }

    [Fact]
    public void Last_seen_compares_against_the_previous_cycle()
    {
        var plan = Target(new MonitorRule
        {
            DropPercent = 30m,
            Baseline = PriceBaseline.LastSeen,
        });
        var state = new MonitorState();
        var now = DateTimeOffset.Parse("2026-11-15T10:00:00Z");

        _ = MonitorRuleEvaluator.EvaluateItem(plan, Item(plan, 100m), state, "run", 1, now);
        var result = MonitorRuleEvaluator.EvaluateItem(
            plan,
            Item(plan, 69m),
            state,
            "run",
            2,
            now.AddHours(1));

        Assert.Single(result.Alerts);
    }

    [Fact]
    public void Group_total_uses_quantities()
    {
        var group = BuildGroup(totalMax: 250m, allowIncomplete: false);
        var observations = new Dictionary<string, MonitorItemObservation>
        {
            ["it:B000000001"] = Item(group.Items[0], 100m),
            ["it:B000000002"] = Item(group.Items[1], 60m),
        };
        var observation = MonitorObservationFactory.FromGroup(
            group,
            observations,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.True(observation.Complete);
        Assert.Equal(220m, observation.Total);

        var evaluation = MonitorRuleEvaluator.EvaluateGroup(
            group,
            observation,
            new MonitorState(),
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.Contains("total_max", evaluation.TriggeredRules);
        Assert.Single(evaluation.Alerts);
        Assert.Equal(220m, evaluation.Alerts[0].Observed);
    }

    [Fact]
    public void Incomplete_group_does_not_trigger_by_default()
    {
        var group = BuildGroup(totalMax: 250m, allowIncomplete: false);
        var observations = new Dictionary<string, MonitorItemObservation>
        {
            ["it:B000000001"] = Item(group.Items[0], 100m),
        };
        var observation = MonitorObservationFactory.FromGroup(
            group,
            observations,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.False(observation.Complete);
        Assert.Null(observation.Total);

        var evaluation = MonitorRuleEvaluator.EvaluateGroup(
            group,
            observation,
            new MonitorState(),
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.Empty(evaluation.Alerts);
        Assert.Empty(evaluation.TriggeredRules);
    }

    [Fact]
    public void Incomplete_group_can_trigger_when_allowed()
    {
        var group = BuildGroup(totalMax: 250m, allowIncomplete: true);
        var observations = new Dictionary<string, MonitorItemObservation>
        {
            ["it:B000000001"] = Item(group.Items[0], 100m),
        };
        var observation = MonitorObservationFactory.FromGroup(
            group,
            observations,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.False(observation.Complete);
        Assert.Equal(100m, observation.Total);

        var evaluation = MonitorRuleEvaluator.EvaluateGroup(
            group,
            observation,
            new MonitorState(),
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.Single(evaluation.Alerts);
        Assert.False(evaluation.Alerts[0].Complete);
    }

    [Fact]
    public void Group_total_converts_multiple_currencies()
    {
        var group = new MonitorGroupPlan
        {
            Id = "group",
            Name = "Group",
            Currency = "EUR",
            FxRates = new Dictionary<string, decimal>
            {
                ["EUR"] = 1m,
                ["GBP"] = 1.2m,
            },
            TotalMax = 300m,
            Items =
            [
                new MonitorTargetPlan
                {
                    Target = new MonitorTarget("it", "B000000001"),
                    GroupId = "group",
                    Rule = new MonitorRule(),
                },
                new MonitorTargetPlan
                {
                    Target = new MonitorTarget("uk", "B000000002"),
                    GroupId = "group",
                    Quantity = 2,
                    Rule = new MonitorRule(),
                },
            ],
        };
        var observations = new Dictionary<string, MonitorItemObservation>
        {
            ["it:B000000001"] = Item(group.Items[0], 100m),
            ["uk:B000000002"] = Item(group.Items[1], 50m, currency: "GBP"),
        };

        var observation = MonitorObservationFactory.FromGroup(
            group,
            observations,
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.True(observation.Complete);
        Assert.Equal("EUR", observation.Currency);
        Assert.Equal(220m, observation.Total);
        Assert.Equal(60m, observation.Items[1].PriceAmount);
        Assert.Equal(120m, observation.Items[1].LineTotal);
        Assert.Equal("GBP", observation.Items[1].OriginalCurrency);
        Assert.Equal(50m, observation.Items[1].OriginalPriceAmount);
        Assert.Equal(1.2m, observation.Items[1].FxRate);
    }

    [Fact]
    public void Group_total_is_incomplete_without_an_fx_rate()
    {
        var group = new MonitorGroupPlan
        {
            Id = "group",
            Name = "Group",
            Currency = "EUR",
            TotalMax = 300m,
            Items =
            [
                new MonitorTargetPlan
                {
                    Target = new MonitorTarget("uk", "B000000002"),
                    GroupId = "group",
                    Rule = new MonitorRule(),
                },
            ],
        };

        var observation = MonitorObservationFactory.FromGroup(
            group,
            new Dictionary<string, MonitorItemObservation>
            {
                ["uk:B000000002"] = Item(
                    group.Items[0],
                    50m,
                    currency: "GBP"),
            },
            "run",
            1,
            DateTimeOffset.UtcNow);

        Assert.False(observation.Complete);
        Assert.Null(observation.Total);
        Assert.Null(observation.Items[0].PriceAmount);
        Assert.Equal("GBP", observation.Items[0].OriginalCurrency);
    }

    private static MonitorTargetPlan Target(MonitorRule rule) =>
        new()
        {
            Target = new MonitorTarget("it", "B000000001"),
            Rule = rule,
        };

    private static MonitorItemObservation Item(
        MonitorTargetPlan plan,
        decimal price,
        decimal? original = null,
        string currency = "EUR") =>
        new()
        {
            Domain = plan.Target.Domain,
            Asin = plan.Target.Asin,
            GroupId = plan.GroupId,
            Currency = currency,
            Price = $"{price:0.00} {currency}",
            PriceAmount = price,
            OriginalPriceAmount = original,
            Url = "https://www.amazon.it/dp/B000000001",
        };

    private static MonitorGroupPlan BuildGroup(decimal totalMax, bool allowIncomplete)
    {
        var first = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B000000001"),
            GroupId = "group",
            Rule = new MonitorRule(),
        };
        var second = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B000000002"),
            GroupId = "group",
            Quantity = 2,
            Rule = new MonitorRule(),
        };

        return new MonitorGroupPlan
        {
            Id = "group",
            Name = "Group",
            Currency = "EUR",
            TotalMax = totalMax,
            AllowIncomplete = allowIncomplete,
            Items = [first, second],
        };
    }
}
