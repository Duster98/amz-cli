using System.Globalization;

namespace AmazonScraper.Core.Monitoring;

public sealed record MonitorItemEvaluation(
    IReadOnlyList<MonitorAlert> Alerts,
    IReadOnlyList<string> TriggeredRules);

public sealed record MonitorGroupEvaluation(
    IReadOnlyList<MonitorAlert> Alerts,
    IReadOnlyList<string> TriggeredRules);

/// <summary>
/// Evaluates price thresholds and persists the condition/cooldown state used
/// to avoid alerting on every cycle.
/// </summary>
public static class MonitorRuleEvaluator
{
    public static MonitorItemEvaluation EvaluateItem(
        MonitorTargetPlan plan,
        MonitorItemObservation observation,
        MonitorState state,
        string runId,
        int cycle,
        DateTimeOffset now)
    {
        state.Targets ??= [];
        state.Groups ??= [];
        var targetState = GetTargetState(state, plan.Target.Key);
        targetState.Rules ??= [];
        targetState.Observations++;
        if (observation.PriceAmount is not { } price)
        {
            return new MonitorItemEvaluation([], []);
        }

        var previousLast = targetState.LastPrice;
        targetState.FirstSeenPrice ??= price;
        targetState.MinSeenPrice = targetState.MinSeenPrice is { } minSeen
            ? Math.Min(minSeen, price)
            : price;
        targetState.MaxSeenPrice = targetState.MaxSeenPrice is { } maxSeen
            ? Math.Max(maxSeen, price)
            : price;
        targetState.LastPrice = price;
        targetState.Currency = string.IsNullOrWhiteSpace(observation.Currency)
            ? targetState.Currency
            : observation.Currency;
        targetState.GroupId ??= plan.GroupId;
        targetState.Label ??= plan.Label;
        targetState.LastSeenAt = now;

        var alerts = new List<MonitorAlert>();
        var triggered = new List<string>();
        var currency = observation.Currency;
        var priceText = string.IsNullOrWhiteSpace(observation.Price)
            ? $"{MonitorObservationFactory.FormatAmount(price)} {currency}"
            : observation.Price;

        if (plan.Rule.MaxPrice is { } maxPrice)
        {
            var condition = price <= maxPrice;
            if (condition)
            {
                triggered.Add("max_price");
            }

            var ruleId = $"item:{plan.Target.Key}:max_price";
            if (EvaluateRule(
                    targetState.Rules,
                    ruleId,
                    condition,
                    plan.Rule.CooldownSeconds,
                    now,
                    out var shouldAlert)
                && shouldAlert)
            {
                alerts.Add(new MonitorAlert
                {
                    AlertedAt = MonitorObservationFactory.FormatTimestamp(now),
                    RunId = runId,
                    Cycle = cycle,
                    RuleId = ruleId,
                    Type = "item_max_price",
                    Domain = plan.Target.Domain,
                    Asin = plan.Target.Asin,
                    GroupId = plan.GroupId,
                    Currency = currency,
                    Threshold = maxPrice,
                    Observed = price,
                    Message = $"{plan.Target.Key}: {priceText} <= " +
                              $"{MonitorObservationFactory.FormatAmount(maxPrice)} {currency} " +
                              "(max_price)",
                    Url = observation.Url,
                });
            }
        }

        if (plan.Rule.DropPercent is { } dropPercent)
        {
            var baselineName = BaselineName(plan.Rule.Baseline);
            var baseline = ResolveBaseline(plan.Rule, observation, targetState, previousLast);
            if (baseline is { } basePrice && basePrice > 0)
            {
                var threshold = basePrice * (1 - dropPercent / 100m);
                var condition = price <= threshold;
                if (condition)
                {
                    triggered.Add($"drop_percent:{baselineName}");
                }

                var ruleId = $"item:{plan.Target.Key}:drop_percent:{baselineName}";
                if (EvaluateRule(
                        targetState.Rules,
                        ruleId,
                        condition,
                        plan.Rule.CooldownSeconds,
                        now,
                        out var shouldAlert)
                    && shouldAlert)
                {
                    alerts.Add(new MonitorAlert
                    {
                        AlertedAt = MonitorObservationFactory.FormatTimestamp(now),
                        RunId = runId,
                        Cycle = cycle,
                        RuleId = ruleId,
                        Type = "item_drop_percent",
                        Domain = plan.Target.Domain,
                        Asin = plan.Target.Asin,
                        GroupId = plan.GroupId,
                        Currency = currency,
                        Threshold = threshold,
                        Observed = price,
                        Message = $"{plan.Target.Key}: {priceText} is " +
                                  $"{dropPercent.ToString("0.##", CultureInfo.InvariantCulture)}% " +
                                  $"below {baselineName} " +
                                  $"{MonitorObservationFactory.FormatAmount(basePrice)} {currency} " +
                                  $"(threshold {MonitorObservationFactory.FormatAmount(threshold)} {currency})",
                        Url = observation.Url,
                    });
                }
            }
        }

        if (plan.Rule.RisePercent is { } risePercent)
        {
            var baselineName = BaselineName(plan.Rule.Baseline);
            var baseline = ResolveBaseline(plan.Rule, observation, targetState, previousLast);
            if (baseline is { } basePrice && basePrice > 0)
            {
                var threshold = basePrice * (1 + risePercent / 100m);
                var condition = price >= threshold;
                if (condition)
                {
                    triggered.Add($"rise_percent:{baselineName}");
                }

                var ruleId = $"item:{plan.Target.Key}:rise_percent:{baselineName}";
                if (EvaluateRule(
                        targetState.Rules,
                        ruleId,
                        condition,
                        plan.Rule.CooldownSeconds,
                        now,
                        out var shouldAlert)
                    && shouldAlert)
                {
                    alerts.Add(new MonitorAlert
                    {
                        AlertedAt = MonitorObservationFactory.FormatTimestamp(now),
                        RunId = runId,
                        Cycle = cycle,
                        RuleId = ruleId,
                        Type = "item_rise_percent",
                        Domain = plan.Target.Domain,
                        Asin = plan.Target.Asin,
                        GroupId = plan.GroupId,
                        Currency = currency,
                        Threshold = threshold,
                        Observed = price,
                        Message = $"{plan.Target.Key}: {priceText} is " +
                                  $"{risePercent.ToString("0.##", CultureInfo.InvariantCulture)}% " +
                                  $"above {baselineName} " +
                                  $"{MonitorObservationFactory.FormatAmount(basePrice)} {currency} " +
                                  $"(threshold {MonitorObservationFactory.FormatAmount(threshold)} {currency})",
                        Url = observation.Url,
                    });
                }
            }
        }

        return new MonitorItemEvaluation(alerts, triggered);
    }

    public static MonitorGroupEvaluation EvaluateGroup(
        MonitorGroupPlan group,
        MonitorGroupObservation observation,
        MonitorState state,
        string runId,
        int cycle,
        DateTimeOffset now)
    {
        state.Targets ??= [];
        state.Groups ??= [];
        var groupState = GetGroupState(state, group.Id);
        groupState.Rules ??= [];
        groupState.Observations++;
        if (observation.Total is not { } total)
        {
            return new MonitorGroupEvaluation([], []);
        }

        groupState.Name = group.Name;
        groupState.Currency = string.IsNullOrWhiteSpace(group.Currency)
            ? groupState.Currency
            : group.Currency;
        groupState.Items = observation.ItemsCount;
        groupState.LastTotal = total;
        groupState.MinTotal = groupState.MinTotal is { } minTotal
            ? Math.Min(minTotal, total)
            : total;
        groupState.MaxTotal = groupState.MaxTotal is { } maxTotal
            ? Math.Max(maxTotal, total)
            : total;
        groupState.LastSeenAt = now;

        if (group.TotalMax is not { } totalMax)
        {
            return new MonitorGroupEvaluation([], []);
        }

        var condition = total <= totalMax;
        var triggered = condition ? new List<string> { "total_max" } : [];
        var ruleId = $"group:{group.Id}:total_max";
        var alerts = new List<MonitorAlert>();

        if (EvaluateRule(
                groupState.Rules,
                ruleId,
                condition,
                group.CooldownSeconds,
                now,
                out var shouldAlert)
            && shouldAlert)
        {
            var suffix = observation.Complete ? string.Empty : " (partial)";
            alerts.Add(new MonitorAlert
            {
                AlertedAt = MonitorObservationFactory.FormatTimestamp(now),
                RunId = runId,
                Cycle = cycle,
                RuleId = ruleId,
                Type = "group_total",
                GroupId = group.Id,
                Currency = group.Currency,
                Threshold = totalMax,
                Observed = total,
                Complete = observation.Complete,
                Message = $"Group {group.Id}: " +
                          $"{MonitorObservationFactory.FormatAmount(total)} {group.Currency} <= " +
                          $"{MonitorObservationFactory.FormatAmount(totalMax)} {group.Currency}" +
                          suffix,
            });
        }

        return new MonitorGroupEvaluation(alerts, triggered);
    }

    private static bool EvaluateRule(
        IDictionary<string, MonitorRuleState> rules,
        string ruleId,
        bool condition,
        int cooldownSeconds,
        DateTimeOffset now,
        out bool shouldAlert)
    {
        var ruleState = rules.TryGetValue(ruleId, out var existing)
            ? existing
            : new MonitorRuleState();

        var transition = condition && !ruleState.LastCondition;
        var cooldownElapsed = ruleState.LastAlertAt is not { } lastAlert
            || now - lastAlert >= TimeSpan.FromSeconds(cooldownSeconds);

        shouldAlert = condition && (transition || cooldownElapsed);
        ruleState.LastCondition = condition;
        if (shouldAlert)
        {
            ruleState.LastAlertAt = now;
        }

        rules[ruleId] = ruleState;
        return true;
    }

    private static decimal? ResolveBaseline(
        MonitorRule rule,
        MonitorItemObservation observation,
        MonitorTargetState state,
        decimal? previousLast) =>
        rule.Baseline switch
        {
            PriceBaseline.Original => observation.OriginalPriceAmount,
            PriceBaseline.FirstSeen => state.FirstSeenPrice,
            PriceBaseline.MaxSeen => state.MaxSeenPrice,
            PriceBaseline.LastSeen => previousLast,
            PriceBaseline.Fixed => rule.BaselinePrice,
            _ => null,
        };

    private static string BaselineName(PriceBaseline baseline) =>
        baseline switch
        {
            PriceBaseline.Original => "original",
            PriceBaseline.FirstSeen => "first_seen",
            PriceBaseline.MaxSeen => "max_seen",
            PriceBaseline.LastSeen => "last_seen",
            PriceBaseline.Fixed => "fixed",
            _ => "unknown",
        };

    private static MonitorTargetState GetTargetState(MonitorState state, string key)
    {
        if (!state.Targets.TryGetValue(key, out var targetState))
        {
            targetState = new MonitorTargetState();
            state.Targets[key] = targetState;
        }

        return targetState;
    }

    private static MonitorGroupState GetGroupState(MonitorState state, string id)
    {
        if (!state.Groups.TryGetValue(id, out var groupState))
        {
            groupState = new MonitorGroupState();
            state.Groups[id] = groupState;
        }

        return groupState;
    }
}
