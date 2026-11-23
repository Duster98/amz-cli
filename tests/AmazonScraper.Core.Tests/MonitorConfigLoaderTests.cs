using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorConfigLoaderTests
{
    [Fact]
    public void Comments_and_trailing_commas_are_accepted()
    {
        var path = TempFile(
            """
            {
              // comment
              "schema_version": 1,
              "items": [
                { "asin": "B000000001", "max_price": 10, },
              ],
            }
            """);

        try
        {
            var config = MonitorConfigLoader.Load(path);
            Assert.Single(config.Items);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Notifications_block_is_loaded()
    {
        var path = TempFile(
            """
            {
              "schema_version": 1,
              "notifications": {
                "enabled": true,
                "on": ["alerts", "errors"],
                "cooldown_seconds": 120
              },
              "items": [
                { "asin": "B000000001" }
              ]
            }
            """);

        try
        {
            var config = MonitorConfigLoader.Load(path);
            Assert.True(config.Notifications?.Enabled);
            Assert.Equal(["alerts", "errors"], config.Notifications?.On);
            Assert.Equal(120, config.Notifications?.CooldownSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Schedule_and_per_unit_gaps_are_loaded()
    {
        var path = TempFile(
            """
            {
              "schema_version": 1,
              "schedule": {
                "mode": "adaptive",
                "window_seconds": 3600,
                "max_requests": 20,
                "min_gap_seconds": 60,
                "max_gap_seconds": 120
              },
              "items": [
                {
                  "asin": "B000000001",
                  "min_gap_seconds": 90,
                  "max_gap_seconds": 180
                }
              ],
              "groups": [
                {
                  "id": "g1",
                  "min_gap_seconds": 120,
                  "max_gap_seconds": 240,
                  "items": [
                    { "asin": "B000000002" }
                  ]
                }
              ]
            }
            """);

        try
        {
            var config = MonitorConfigLoader.Load(path);
            Assert.Equal("adaptive", config.Schedule?.Mode);
            Assert.Equal(20, config.Schedule?.MaxRequests);
            Assert.Equal(90, config.Items[0].MinGapSeconds);
            Assert.Equal(120, config.Groups[0].MinGapSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_fields_are_rejected()
    {
        var path = TempFile(
            """
            {
              "schema_version": 1,
              "items": [
                { "asin": "B000000001", "maxprice": 10 }
              ]
            }
            """);

        try
        {
            Assert.Throws<MonitorException>(() => MonitorConfigLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"amz-monitor-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
