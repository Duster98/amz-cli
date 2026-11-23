using System.Text.Json;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorStatsTests
{
    [Fact]
    public async Task Build_reads_targets_groups_and_alerts_from_state()
    {
        using var directory = new TempDirectory();
        var paths = MonitorPaths.Resolve(directory.Root, "monitor");
        var now = new DateTimeOffset(
            2026,
            11,
            17,
            22,
            0,
            0,
            TimeSpan.Zero);
        var state = new MonitorState();
        state.Targets["it:B000000001"] = new MonitorTargetState
        {
            FirstSeenPrice = 100m,
            MinSeenPrice = 80m,
            MaxSeenPrice = 120m,
            LastPrice = 90m,
            Currency = "EUR",
            LastSeenAt = now,
            Observations = 4,
        };
        state.Targets["it:B000000001"].Rules["rule"] = new MonitorRuleState
        {
            LastAlertAt = now,
        };
        state.Groups["group"] = new MonitorGroupState
        {
            Name = "Group",
            Currency = "EUR",
            Items = 2,
            LastTotal = 200m,
            MinTotal = 180m,
            MaxTotal = 220m,
            LastSeenAt = now,
            Observations = 3,
            Rules =
            {
                ["rule"] = new MonitorRuleState { LastAlertAt = now },
            },
        };
        await MonitorStateStore.SaveAsync(
            paths.StatePath,
            state,
            CancellationToken.None);

        var report = MonitorStatsService.Build(paths);

        Assert.Equal(paths.StatePath, report.StatePath);
        Assert.Equal(1, report.Targets);
        Assert.Equal(1, report.Groups);
        Assert.Equal(2, report.AlertedRules);
        Assert.NotNull(report.LastSeenAt);
        var target = Assert.Single(report.TargetRows);
        Assert.Equal("it", target.Domain);
        Assert.Equal("B000000001", target.Asin);
        Assert.Equal(80m, target.MinPrice);
        Assert.Equal(120m, target.MaxPrice);
        Assert.Equal(90m, target.LastPrice);
        Assert.Equal("EUR", target.Currency);
        Assert.Equal(4, target.Observations);
        var group = Assert.Single(report.GroupRows);
        Assert.Equal("Group", group.Name);
        Assert.Equal(2, group.Items);
        Assert.Equal(180m, group.MinTotal);
        Assert.Equal(220m, group.MaxTotal);
        Assert.Equal(3, group.Observations);
    }

    [Fact]
    public async Task History_counts_are_merged_with_the_state_counter()
    {
        using var directory = new TempDirectory();
        var paths = MonitorPaths.Resolve(directory.Root, "monitor");
        var state = new MonitorState();
        state.Targets["it:B000000001"] = new MonitorTargetState
        {
            Observations = 4,
        };
        state.Targets["it:B000000002"] = new MonitorTargetState();
        state.Groups["group"] = new MonitorGroupState();
        await MonitorStateStore.SaveAsync(
            paths.StatePath,
            state,
            CancellationToken.None);
        var history = Path.Combine(directory.Root, "monitor.items.jsonl");
        await File.WriteAllLinesAsync(
            history,
            [
                """{"record_type":"item","domain":"it","asin":"B000000001"}""",
                """{"record_type":"item","domain":"it","asin":"B000000001"}""",
                """{"record_type":"item","domain":"it","asin":"B000000001"}""",
                """{"record_type":"item","domain":"it","asin":"B000000001"}""",
                """{"record_type":"item","domain":"it","asin":"B000000001"}""",
                """{"record_type":"item","domain":"it","asin":"B000000002"}""",
                """{"record_type":"group","group_id":"group"}""",
                """{"record_type":"group","group_id":"group"}""",
            ],
            CancellationToken.None);

        var report = MonitorStatsService.Build(paths, [history]);

        Assert.Equal(5, report.TargetRows[0].Observations);
        Assert.Equal(1, report.TargetRows[1].Observations);
        Assert.Equal(2, report.GroupRows[0].Observations);
    }

    [Fact]
    public void Build_without_state_returns_an_empty_report()
    {
        using var directory = new TempDirectory();
        var paths = MonitorPaths.Resolve(directory.Root, "monitor");

        var report = MonitorStatsService.Build(paths);

        Assert.Equal(0, report.Targets);
        Assert.Equal(0, report.Groups);
        Assert.Equal(0, report.AlertedRules);
        Assert.Empty(report.TargetRows);
        Assert.Empty(report.GroupRows);
    }

    [Fact]
    public async Task Json_output_is_serializable()
    {
        using var directory = new TempDirectory();
        var paths = MonitorPaths.Resolve(directory.Root, "monitor");
        var state = new MonitorState();
        state.Targets["it:B000000001"] = new MonitorTargetState
        {
            MinSeenPrice = 10m,
            MaxSeenPrice = 20m,
            LastPrice = 15m,
            Currency = "EUR",
        };
        await MonitorStateStore.SaveAsync(
            paths.StatePath,
            state,
            CancellationToken.None);

        var json = MonitorStatsRenderer.ToJson(
            MonitorStatsService.Build(paths));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("targets").GetInt32());
        Assert.Equal(
            10m,
            document.RootElement
                .GetProperty("target_rows")[0]
                .GetProperty("min_price")
                .GetDecimal());
    }
}
