using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorStateStoreTests
{
    [Fact]
    public async Task State_round_trips_prices_and_conditions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            var state = new MonitorState
            {
                ConfigHash = "hash",
                Targets =
                {
                    ["it:B000000001"] = new MonitorTargetState
                    {
                        FirstSeenPrice = 100m,
                        MaxSeenPrice = 120m,
                        LastPrice = 80m,
                        Rules =
                        {
                            ["item:it:B000000001:max_price"] = new MonitorRuleState
                            {
                                LastCondition = true,
                                LastAlertAt = DateTimeOffset.Parse("2026-11-15T10:00:00Z"),
                            },
                        },
                    },
                },
            };

            await MonitorStateStore.SaveAsync(path, state);
            var loaded = MonitorStateStore.Load(path);

            Assert.Equal("hash", loaded.ConfigHash);
            Assert.Equal(100m, loaded.Targets["it:B000000001"].FirstSeenPrice);
            Assert.True(loaded.Targets["it:B000000001"]
                .Rules["item:it:B000000001:max_price"].LastCondition);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task State_round_trips_adaptive_schedule_budget()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            var state = new MonitorState
            {
                Schedule = new MonitorScheduleState
                {
                    ScheduleHash = "schedule-hash",
                    AnchorAt = DateTimeOffset.Parse("2026-11-17T10:00:00Z"),
                    WindowIndex = 2,
                    WindowSeconds = 86_400,
                    WindowStart = DateTimeOffset.Parse("2026-11-19T10:00:00Z"),
                    WindowEnd = DateTimeOffset.Parse("2026-11-20T10:00:00Z"),
                    SpentRequests = 42,
                    Tokens = 3.5,
                    Units =
                    {
                        ["item:it:B000000001"] = new MonitorUnitState
                        {
                            LastRunAt = DateTimeOffset.Parse("2026-11-19T10:01:00Z"),
                            NextDueAt = DateTimeOffset.Parse("2026-11-19T10:11:00Z"),
                            Runs = 3,
                            LastCost = 1,
                        },
                    },
                },
            };

            await MonitorStateStore.SaveAsync(path, state);
            var loaded = MonitorStateStore.Load(path);

            var schedule = Assert.IsType<MonitorScheduleState>(loaded.Schedule);
            Assert.Equal("schedule-hash", schedule.ScheduleHash);
            Assert.Equal(2, schedule.WindowIndex);
            Assert.Equal(42, schedule.SpentRequests);
            Assert.Equal(3, schedule.Units["item:it:B000000001"].Runs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_state_is_backed_up_and_replaced()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            File.WriteAllText(path, "{not-json");
            var warnings = new List<string>();

            var loaded = MonitorStateStore.Load(path, warnings.Add);

            Assert.Empty(loaded.Targets);
            Assert.Single(warnings);
            Assert.Contains(
                Directory.GetFiles(root),
                file => file.Contains(".corrupt-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
