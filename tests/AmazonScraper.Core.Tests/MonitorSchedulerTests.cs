using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorSchedulerTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-11-17T10:00:00Z");

    [Fact]
    public void Adaptive_plan_builds_item_and_group_units_with_request_costs()
    {
        var plan = BuildPlan(
            new MonitorConfig
            {
                Schedule = new MonitorScheduleConfig
                {
                    Mode = "adaptive",
                    WindowSeconds = 3600,
                    MaxRequests = 20,
                    MinGapSeconds = 60,
                    MaxGapSeconds = 120,
                },
                Items = [new MonitorItemConfig { Asin = "B000000001" }],
                Groups =
                [
                    new MonitorGroupConfig
                    {
                        Id = "g1",
                        Items =
                        [
                            new MonitorGroupItemConfig { Asin = "B000000002" },
                            new MonitorGroupItemConfig { Asin = "B000000003" },
                        ],
                    },
                ],
            });

        var schedule = Assert.IsType<MonitorSchedulePlan>(plan.Schedule);
        Assert.Equal(2, schedule.Units.Count);
        var item = Assert.Single(schedule.Units, unit =>
            unit.Kind == MonitorUnitKind.Item);
        Assert.Equal(1, item.Cost);
        var group = Assert.Single(schedule.Units, unit =>
            unit.Kind == MonitorUnitKind.Group);
        Assert.Equal(2, group.Cost);
        Assert.NotNull(plan.ScheduleHash);
    }

    [Fact]
    public void Adaptive_schedule_requires_max_requests()
    {
        var error = Assert.Throws<MonitorException>(() => BuildPlan(
            new MonitorConfig
            {
                Schedule = new MonitorScheduleConfig
                {
                    Mode = "adaptive",
                    WindowSeconds = 3600,
                },
                Items = [new MonitorItemConfig { Asin = "B000000001" }],
            }));

        Assert.Contains("max_requests", error.Message);
    }

    [Fact]
    public void Adaptive_budget_must_cover_the_largest_group()
    {
        var error = Assert.Throws<MonitorException>(() => BuildPlan(
            new MonitorConfig
            {
                Schedule = new MonitorScheduleConfig
                {
                    Mode = "adaptive",
                    WindowSeconds = 3600,
                    MaxRequests = 1,
                    MinGapSeconds = 60,
                    MaxGapSeconds = 120,
                },
                Groups =
                [
                    new MonitorGroupConfig
                    {
                        Id = "g1",
                        Items =
                        [
                            new MonitorGroupItemConfig { Asin = "B000000002" },
                            new MonitorGroupItemConfig { Asin = "B000000003" },
                        ],
                    },
                ],
            }));

        Assert.Contains("lower than the cost", error.Message);
    }

    [Fact]
    public void Timer_mode_without_schedule_stays_unchanged()
    {
        var plan = BuildPlan(
            new MonitorConfig
            {
                Items = [new MonitorItemConfig { Asin = "B000000001" }],
            });

        Assert.Null(plan.Schedule);
        Assert.Null(plan.ScheduleHash);
    }

    [Fact]
    public void Scheduler_respects_min_and_max_gap_after_a_run()
    {
        var unit = ItemUnit("item:it:B000000001", min: 60, max: 90);
        var schedule = Schedule(units: [unit], maxRequests: 100, window: 3600);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 10,
            scheduleHash: "hash",
            random: new Random(7));
        scheduler.Initialize(Start);

        var selected = scheduler.SelectNext(Start);
        Assert.NotNull(selected);

        scheduler.RecordRun(selected!, Start, Start.AddSeconds(10), 1);
        var next = state.Units[unit.Id].NextDueAt;
        Assert.NotNull(next);
        Assert.InRange(
            (next!.Value - Start.AddSeconds(10)).TotalSeconds,
            60,
            90);
    }

    [Fact]
    public void Scheduler_never_exceeds_the_window_budget()
    {
        var item = ItemUnit("item:it:B000000001", min: 1, max: 1);
        var group = GroupUnit("group:g1", cost: 2, min: 1, max: 1);
        var schedule = Schedule([item, group], maxRequests: 5, window: 60);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(3));
        scheduler.Initialize(Start);
        var now = Start;

        for (var i = 0; i < 100 && now < scheduler.WindowEnd; i++)
        {
            var selected = scheduler.SelectNext(now);
            if (selected is null)
            {
                now += scheduler.NextDelay(now);
                continue;
            }

            scheduler.RecordRun(selected, now, now, selected.Cost);
            now += TimeSpan.FromSeconds(1);
        }

        Assert.True(state.SpentRequests <= 5);
    }

    [Fact]
    public void Group_is_not_selected_until_it_has_enough_tokens()
    {
        var group = GroupUnit("group:g1", cost: 3, min: 1, max: 1);
        var schedule = Schedule([group], maxRequests: 3, window: 60);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(4));
        scheduler.Initialize(Start);

        var selected = scheduler.SelectNext(Start);
        Assert.NotNull(selected);
        scheduler.RecordRun(selected!, Start, Start, selected!.Cost);

        Assert.Equal(3, state.SpentRequests);
        Assert.Null(scheduler.SelectNext(Start.AddSeconds(1)));
    }

    [Fact]
    public void Charged_requests_count_even_when_a_cycle_is_interrupted()
    {
        var item = ItemUnit("item:it:B000000001", min: 1, max: 1);
        var schedule = Schedule([item], maxRequests: 3, window: 3600);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(13));
        scheduler.Initialize(Start);

        scheduler.ChargeRequests(2);

        Assert.Equal(2, state.SpentRequests);
    }

    [Fact]
    public void Scheduler_resets_the_budget_on_a_new_window()
    {
        var item = ItemUnit("item:it:B000000001", min: 1, max: 1);
        var schedule = Schedule([item], maxRequests: 1, window: 60);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(5));
        scheduler.Initialize(Start);

        var selected = scheduler.SelectNext(Start);
        Assert.NotNull(selected);
        scheduler.RecordRun(selected!, Start, Start, 1);
        Assert.Equal(1, state.SpentRequests);

        var nextWindow = Start.AddSeconds(61);
        Assert.Null(scheduler.SelectNext(Start.AddSeconds(59)));
        var next = scheduler.SelectNext(nextWindow);
        Assert.NotNull(next);
        Assert.Equal(0, state.SpentRequests);
        Assert.Equal(1, state.WindowIndex);
    }

    [Fact]
    public void Oldest_last_run_wins_when_two_units_are_due()
    {
        var first = ItemUnit("item:it:B000000001", min: 1, max: 1);
        var second = ItemUnit("item:it:B000000002", min: 1, max: 1);
        var schedule = Schedule([first, second], maxRequests: 60, window: 60);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(6));
        scheduler.Initialize(Start);

        var selected = scheduler.SelectNext(Start);
        Assert.NotNull(selected);
        scheduler.RecordRun(selected!, Start, Start, 1);
        var next = scheduler.SelectNext(Start.AddSeconds(2));
        Assert.NotNull(next);
        Assert.NotEqual(selected!.Id, next!.Id);
    }

    [Fact]
    public void Unit_that_cannot_finish_before_window_end_is_not_selected()
    {
        var group = GroupUnit("group:g1", cost: 10, min: 1, max: 1);
        var schedule = Schedule([group], maxRequests: 10, window: 60);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 10,
            scheduleHash: "hash",
            random: new Random(10));
        scheduler.Initialize(Start);

        Assert.Null(scheduler.SelectNext(Start));
        Assert.True(scheduler.NextDelay(Start) > TimeSpan.Zero);
    }

    [Fact]
    public void Restart_in_the_same_window_keeps_the_spent_budget()
    {
        var item = ItemUnit("item:it:B000000001", min: 1, max: 1);
        var schedule = Schedule([item], maxRequests: 3, window: 3600);
        var state = new MonitorScheduleState();
        var first = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(11));
        first.Initialize(Start);
        var selected = first.SelectNext(Start);
        Assert.NotNull(selected);
        first.RecordRun(selected!, Start, Start, 1);

        var restarted = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash",
            random: new Random(12));
        restarted.Initialize(Start.AddSeconds(5));

        Assert.Equal(1, state.SpentRequests);
        Assert.Equal(0, state.WindowIndex);
    }

    [Fact]
    public void Schedule_change_clamps_next_due_to_new_gap()
    {
        var unit = ItemUnit("item:it:B000000001", min: 60, max: 120);
        var schedule = Schedule([unit], maxRequests: 10, window: 3600);
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash-a",
            random: new Random(8));
        scheduler.Initialize(Start);
        var selected = scheduler.SelectNext(Start);
        Assert.NotNull(selected);
        scheduler.RecordRun(selected!, Start, Start, 1);
        state.Units[unit.Id].NextDueAt = Start.AddSeconds(1000);

        var changed = Schedule(
            [ItemUnit("item:it:B000000001", min: 60, max: 120)],
            maxRequests: 10,
            window: 3600);
        var changedScheduler = new MonitorScheduler(
            changed,
            state,
            requestDelaySeconds: 1,
            scheduleHash: "hash-b",
            random: new Random(9));
        changedScheduler.Initialize(Start.AddSeconds(2));

        Assert.True(
            state.Units[unit.Id].NextDueAt <= Start.AddSeconds(120));
    }

    private static MonitorPlan BuildPlan(MonitorConfig config) =>
        MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            Config = config,
            DefaultDomain = "it",
        });

    private static MonitorSchedulePlan Schedule(
        IReadOnlyList<MonitorUnitPlan> units,
        int maxRequests,
        int window) =>
        new()
        {
            WindowSeconds = window,
            MaxRequests = maxRequests,
            DefaultMinGapSeconds = 1,
            DefaultMaxGapSeconds = 1,
            Units = units,
        };

    private static MonitorUnitPlan ItemUnit(
        string id,
        int min,
        int max) =>
        new()
        {
            Id = id,
            Kind = MonitorUnitKind.Item,
            Cost = 1,
            MinGapSeconds = min,
            MaxGapSeconds = max,
            Item = new MonitorTargetPlan
            {
                Target = new MonitorTarget("it", id.Split(':').Last()),
            },
        };

    private static MonitorUnitPlan GroupUnit(
        string id,
        int cost,
        int min,
        int max) =>
        new()
        {
            Id = id,
            Kind = MonitorUnitKind.Group,
            Cost = cost,
            MinGapSeconds = min,
            MaxGapSeconds = max,
            Group = new MonitorGroupPlan
            {
                Id = id.Split(':').Last(),
                Name = "Group",
                Currency = "EUR",
                Items = Enumerable.Range(1, cost)
                    .Select(index => new MonitorTargetPlan
                    {
                        Target = new MonitorTarget("it", $"B{index:000000000}"),
                    })
                    .ToArray(),
            },
        };
}
