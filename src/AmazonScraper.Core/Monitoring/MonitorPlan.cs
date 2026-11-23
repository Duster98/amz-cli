namespace AmazonScraper.Core.Monitoring;

public sealed class MonitorTargetPlan
{
    public required MonitorTarget Target { get; init; }

    public string? Label { get; init; }

    public string? GroupId { get; init; }

    /// <summary>Only meaningful inside a group; defaults to one unit.</summary>
    public int Quantity { get; init; } = 1;

    public int? MinGapSeconds { get; init; }

    public int? MaxGapSeconds { get; init; }

    public MonitorRule Rule { get; init; } = new();
}

public sealed class MonitorGroupPlan
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Multipliers from each source currency to <see cref="Currency"/>. The
    /// target currency maps to 1. Empty for legacy single-currency plans.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> FxRates { get; init; } =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    public decimal? TotalMax { get; init; }

    public bool AllowIncomplete { get; init; }

    public int CooldownSeconds { get; init; } = 21_600;

    public int? MinGapSeconds { get; init; }

    public int? MaxGapSeconds { get; init; }

    public required IReadOnlyList<MonitorTargetPlan> Items { get; init; }

    public IReadOnlyList<string> Domains =>
        Items.Select(item => item.Target.Domain)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray();
}

public sealed class MonitorPlan
{
    public required IReadOnlyList<MonitorGroupPlan> Groups { get; init; }

    public required IReadOnlyList<MonitorTargetPlan> Ungrouped { get; init; }

    public required string ConfigHash { get; init; }

    public MonitorSchedulePlan? Schedule { get; init; }

    public string? ScheduleHash { get; init; }

    public IEnumerable<MonitorTargetPlan> AllTargets =>
        Groups.SelectMany(group => group.Items).Concat(Ungrouped);

    public IEnumerable<string> Domains =>
        AllTargets.Select(target => target.Target.Domain)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal);

    public MonitorTargetPlan? FirstTargetFor(string domain) =>
        AllTargets.FirstOrDefault(target =>
            string.Equals(target.Target.Domain, domain, StringComparison.Ordinal));
}
