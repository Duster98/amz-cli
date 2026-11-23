using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorSeriesReaderTests
{
    [Fact]
    public void Reads_product_jsonl_points_and_alerts()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "items.jsonl");
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var lines = new List<string>
        {
            MonitorJson.Item(Item("it", "B0TEST", start, 100m)),
            MonitorJson.Item(Item("it", "B0TEST", start.AddHours(1), 90m)),
            MonitorJson.Item(Item("it", "B0OTHER", start.AddHours(1), 50m)),
            MonitorJson.Alert(new MonitorAlert
            {
                AlertedAt = start.AddHours(2).ToString("O"),
                Type = "max_price",
                Domain = "it",
                Asin = "B0TEST",
                Currency = "EUR",
                Observed = 80m,
                Message = "below target",
            }),
        };
        File.WriteAllLines(path, lines);

        var result = MonitorSeriesReader.ReadProduct(
            [path],
            "it",
            "B0TEST",
            new MonitorSeriesRange(
                From: start.AddMinutes(-1),
                To: start.AddHours(3)));

        Assert.Equal(2, result.Observations);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal(90m, result.Points[^1].Price);
        Assert.Single(result.Alerts);
        Assert.Equal("max_price", result.Alerts[0].Type);
    }

    [Fact]
    public void Reads_group_jsonl_and_marks_incomplete_totals()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "groups.jsonl");
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        File.WriteAllLines(path,
        [
            MonitorJson.Group(Group("g_test", start, 100m, true)),
            MonitorJson.Group(Group("g_test", start.AddHours(1), 120m, false)),
            MonitorJson.Group(Group("g_other", start.AddHours(1), 999m, true)),
        ]);

        var result = MonitorSeriesReader.ReadGroup(
            [path],
            "g_test",
            new MonitorSeriesRange(
                From: start.AddMinutes(-1),
                To: start.AddHours(2)));

        Assert.Equal(2, result.Points.Count);
        Assert.True(result.Points[0].Complete);
        Assert.False(result.Points[1].Complete);
        Assert.Equal(120m, result.Points[1].Price);
    }

    [Fact]
    public void Reads_csv_item_and_group_rows()
    {
        using var directory = new TempDirectory();
        var itemPath = Path.Combine(directory.Path, "items.csv");
        var groupPath = Path.Combine(directory.Path, "groups.csv");
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        File.WriteAllLines(itemPath,
        [
            MonitorCsv.ItemHeader,
            MonitorCsv.FormatItem(Item("it", "B0CSV", start, 25m)),
        ]);
        File.WriteAllLines(groupPath,
        [
            MonitorCsv.GroupHeader,
            MonitorCsv.FormatGroup(Group("g_csv", start, 75m, true)),
        ]);

        var item = MonitorSeriesReader.ReadProduct(
            [itemPath],
            "it",
            "B0CSV",
            new MonitorSeriesRange());
        var group = MonitorSeriesReader.ReadGroup(
            [groupPath],
            "g_csv",
            new MonitorSeriesRange());

        Assert.Equal(25m, Assert.Single(item.Points).Price);
        Assert.Equal(75m, Assert.Single(group.Points).Price);
    }

    [Fact]
    public void Downsamples_large_series_and_keeps_min_max()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "items.jsonl");
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        File.WriteAllLines(path, Enumerable.Range(0, 120)
            .Select(index => MonitorJson.Item(
                Item("it", "B0BIG", start.AddHours(index), 100m + index))));

        var result = MonitorSeriesReader.ReadProduct(
            [path],
            "it",
            "B0BIG",
            new MonitorSeriesRange(MaxPoints: 50));

        Assert.True(result.Truncated);
        Assert.InRange(result.Points.Count, 1, 50);
        Assert.Equal(120, result.Observations);
        Assert.True(result.Points.Max(point => point.Max) >= result.Points.Max(point => point.Price));
        Assert.True(result.Points.Min(point => point.Min) <= result.Points.Min(point => point.Price));
    }

    private static MonitorItemObservation Item(
        string domain,
        string asin,
        DateTimeOffset at,
        decimal price) =>
        new()
        {
            ObservedAt = at.ToString("O"),
            Domain = domain,
            Asin = asin,
            Price = $"{price:0.00} €",
            PriceAmount = price,
            Currency = "EUR",
        };

    private static MonitorGroupObservation Group(
        string groupId,
        DateTimeOffset at,
        decimal total,
        bool complete) =>
        new()
        {
            ObservedAt = at.ToString("O"),
            GroupId = groupId,
            Name = "Test group",
            Currency = "EUR",
            Total = total,
            Complete = complete,
        };

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = Directory.CreateTempSubdirectory("amz-series-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
