namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Adaptive scheduling plan. Group cycles are scheduled as one unit whose
/// request cost is the number of products in the group. Ungrouped products
/// are scheduled individually.
/// </summary>
public sealed class MonitorSchedulePlan
{
    public required int WindowSeconds { get; init; }

    public required int MaxRequests { get; init; }

    public required int DefaultMinGapSeconds { get; init; }

    public required int DefaultMaxGapSeconds { get; init; }

    public required IReadOnlyList<MonitorUnitPlan> Units { get; init; }

    public int MaxUnitCost =>
        Units.Count == 0 ? 1 : Units.Max(unit => unit.Cost);

    public MonitorUnitPlan? FindUnit(string id) =>
        Units.FirstOrDefault(unit =>
            string.Equals(unit.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// One schedulable item. <see cref="Item"/> is set for an ungrouped product,
/// <see cref="Group"/> for a complete group cycle.
/// </summary>
public sealed class MonitorUnitPlan
{
    public required string Id { get; init; }

    public required MonitorUnitKind Kind { get; init; }

    public required int Cost { get; init; }

    public required int MinGapSeconds { get; init; }

    public required int MaxGapSeconds { get; init; }

    public MonitorTargetPlan? Item { get; init; }

    public MonitorGroupPlan? Group { get; init; }

    public string Describe() =>
        Kind == MonitorUnitKind.Group
            ? $"group {Group?.Id ?? "?"} ({Cost} requests)"
            : $"item {Item?.Target.Key ?? "?"}";
}
