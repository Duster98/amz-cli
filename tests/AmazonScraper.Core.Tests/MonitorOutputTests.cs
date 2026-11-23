using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorOutputTests
{
    [Fact]
    public async Task Per_group_jsonl_keeps_items_groups_and_alerts_together()
    {
        var root = TempRoot();
        try
        {
            var paths = MonitorPaths.Resolve(root, null);
            var plan = BuildPlan();
            await using (var output = MonitorOutput.Open(
                             paths,
                             MonitorFormat.JsonLines,
                             MonitorLayout.PerGroup,
                             plan))
            {
                await output.WriteItemAsync(Item(plan.Groups[0].Items[0], 100m), CancellationToken.None);
                await output.WriteGroupAsync(Group(plan.Groups[0], 100m), CancellationToken.None);
                await output.WriteAlertAsync(Alert(plan.Groups[0]), CancellationToken.None);
                await output.WriteItemAsync(Item(plan.Ungrouped[0], 20m), CancellationToken.None);
            }

            var groupPath = Path.Combine(
                paths.PerGroupRoot,
                "pc-build.jsonl");
            var ungroupedPath = Path.Combine(
                paths.PerGroupRoot,
                "ungrouped.jsonl");
            Assert.True(File.Exists(groupPath));
            Assert.True(File.Exists(ungroupedPath));

            var groupLines = await File.ReadAllLinesAsync(groupPath);
            Assert.Equal(3, groupLines.Length);
            Assert.Contains("\"record_type\":\"item\"", groupLines[0]);
            Assert.Contains("\"record_type\":\"group\"", groupLines[1]);
            Assert.Contains("\"record_type\":\"alert\"", groupLines[2]);

            var ungroupedLines = await File.ReadAllLinesAsync(ungroupedPath);
            Assert.Single(ungroupedLines);
            Assert.Contains("\"record_type\":\"item\"", ungroupedLines[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Combined_csv_creates_one_file_per_record_type()
    {
        var root = TempRoot();
        try
        {
            var paths = MonitorPaths.Resolve(root, null);
            var plan = BuildPlan();
            await using (var output = MonitorOutput.Open(
                             paths,
                             MonitorFormat.Csv,
                             MonitorLayout.Combined,
                             plan))
            {
                await output.WriteItemAsync(Item(plan.Groups[0].Items[0], 100m), CancellationToken.None);
                await output.WriteGroupAsync(Group(plan.Groups[0], 100m), CancellationToken.None);
                await output.WriteAlertAsync(Alert(plan.Groups[0]), CancellationToken.None);
            }

            var items = Path.Combine(root, "amz-monitor.items.csv");
            var groups = Path.Combine(root, "amz-monitor.groups.csv");
            var alerts = Path.Combine(root, "amz-monitor.alerts.csv");
            Assert.True(File.Exists(items));
            Assert.True(File.Exists(groups));
            Assert.True(File.Exists(alerts));
            Assert.StartsWith(MonitorCsv.ItemHeader, await File.ReadAllTextAsync(items));
            Assert.StartsWith(MonitorCsv.GroupHeader, await File.ReadAllTextAsync(groups));
            Assert.StartsWith(MonitorCsv.AlertHeader, await File.ReadAllTextAsync(alerts));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Per_group_csv_creates_a_folder_per_group()
    {
        var root = TempRoot();
        try
        {
            var paths = MonitorPaths.Resolve(root, null);
            var plan = BuildPlan();
            await using (var output = MonitorOutput.Open(
                             paths,
                             MonitorFormat.Csv,
                             MonitorLayout.PerGroup,
                             plan))
            {
                await output.WriteItemAsync(Item(plan.Groups[0].Items[0], 100m), CancellationToken.None);
                await output.WriteGroupAsync(Group(plan.Groups[0], 100m), CancellationToken.None);
                await output.WriteAlertAsync(Alert(plan.Groups[0]), CancellationToken.None);
                await output.WriteItemAsync(Item(plan.Ungrouped[0], 20m), CancellationToken.None);
            }

            Assert.True(File.Exists(Path.Combine(
                paths.PerGroupRoot, "pc-build", "items.csv")));
            Assert.True(File.Exists(Path.Combine(
                paths.PerGroupRoot, "pc-build", "groups.csv")));
            Assert.True(File.Exists(Path.Combine(
                paths.PerGroupRoot, "pc-build", "alerts.csv")));
            Assert.True(File.Exists(Path.Combine(
                paths.PerGroupRoot, "ungrouped", "items.csv")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Single_layout_with_csv_is_rejected()
    {
        var plan = BuildPlan();
        var error = Assert.Throws<MonitorException>(() =>
            MonitorOutput.ResolveLayout(
                MonitorLayout.Single,
                MonitorFormat.Csv,
                plan));

        Assert.Contains("only with", error.Message);
    }

    [Fact]
    public async Task Partial_trailing_line_is_removed_before_appending()
    {
        var root = TempRoot();
        try
        {
            var paths = MonitorPaths.Resolve(root, null);
            var plan = BuildPlan();
            Directory.CreateDirectory(root);
            var itemsPath = paths.CombinedPath("items", MonitorFormat.JsonLines);
            await File.WriteAllTextAsync(
                itemsPath,
                "{\"record_type\":\"item\",\"asin\":\"OLD\"}\n{\"partial\":");

            await using (var output = MonitorOutput.Open(
                             paths,
                             MonitorFormat.JsonLines,
                             MonitorLayout.Combined,
                             plan))
            {
                await output.WriteItemAsync(Item(plan.Groups[0].Items[0], 100m), CancellationToken.None);
            }

            var lines = await File.ReadAllLinesAsync(itemsPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("OLD", lines[0]);
            Assert.Contains("B000000001", lines[1]);
            Assert.DoesNotContain("partial", string.Join('\n', lines));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Existing_csv_with_a_different_header_is_rejected()
    {
        var root = TempRoot();
        try
        {
            var paths = MonitorPaths.Resolve(root, null);
            var plan = BuildPlan();
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                paths.CombinedPath("items", MonitorFormat.Csv),
                "wrong,header\n");

            await using var output = MonitorOutput.Open(
                paths,
                MonitorFormat.Csv,
                MonitorLayout.Combined,
                plan);
            await Assert.ThrowsAsync<MonitorException>(() =>
                output.WriteItemAsync(Item(plan.Groups[0].Items[0], 100m), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-output-{Guid.NewGuid():N}");

    private static MonitorPlan BuildPlan()
    {
        var grouped = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B000000001"),
            GroupId = "pc-build",
            Rule = new MonitorRule(),
        };
        var ungrouped = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B000000002"),
            Rule = new MonitorRule(),
        };
        return new MonitorPlan
        {
            Groups =
            [
                new MonitorGroupPlan
                {
                    Id = "pc-build",
                    Name = "PC Build",
                    Currency = "EUR",
                    TotalMax = 250m,
                    Items = [grouped],
                },
            ],
            Ungrouped = [ungrouped],
            ConfigHash = "hash",
        };
    }

    private static MonitorItemObservation Item(MonitorTargetPlan plan, decimal price) =>
        new()
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            Domain = plan.Target.Domain,
            Asin = plan.Target.Asin,
            GroupId = plan.GroupId,
            Price = $"{price:0.00} EUR",
            PriceAmount = price,
            Currency = "EUR",
        };

    private static MonitorGroupObservation Group(MonitorGroupPlan plan, decimal total) =>
        new()
        {
            ObservedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            GroupId = plan.Id,
            Name = plan.Name,
            Domains = ["it"],
            Currency = plan.Currency,
            TotalMax = plan.TotalMax,
            Total = total,
            Complete = true,
            ItemsCount = plan.Items.Count,
            AvailableCount = plan.Items.Count,
        };

    private static MonitorAlert Alert(MonitorGroupPlan plan) =>
        new()
        {
            AlertedAt = "2026-11-15T10:00:00.000Z",
            RunId = "run",
            Cycle = 1,
            RuleId = $"group:{plan.Id}:total_max",
            Type = "group_total",
            GroupId = plan.Id,
            Currency = plan.Currency,
            Threshold = plan.TotalMax ?? 0,
            Observed = 100m,
            Complete = true,
            Message = "test alert",
        };
}
