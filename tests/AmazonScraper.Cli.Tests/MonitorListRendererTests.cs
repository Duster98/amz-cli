using System.Text.Json;
using AmazonScraper.Cli.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class MonitorListRendererTests
{
    [Fact]
    public void Json_uses_snake_case_and_keeps_all_fields()
    {
        var entries = new[]
        {
            new MonitorListEntry
            {
                Pid = 1234,
                Status = "running",
                StartedAt = "2026-11-15T22:00:00.000Z",
                LastSeenAt = "2026-11-15T22:01:00.000Z",
                Mode = "detach",
                Startup = "systemd-user",
                StartupEnabled = true,
                StartupRegistered = true,
                StartupName = "amz-monitor-abc123.service",
                StartupId = "abc123",
                StartupConfigPath = "/tmp/monitor.json",
                Domains = ["it", "de"],
                Targets = 3,
                TimerSeconds = 3600,
                SavePath = "/tmp/monitor",
                SaveFile = "amz-monitor",
                DataFiles = ["/tmp/monitor/amz-monitor.items.jsonl"],
                LogPath = "/tmp/monitor/amz-monitor.log",
                PidFile = "/tmp/monitor/amz-monitor.pid",
            },
        };

        using var document = JsonDocument.Parse(
            MonitorListRenderer.ToJson(entries));
        var item = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(1234, item.GetProperty("pid").GetInt32());
        Assert.Equal("running", item.GetProperty("status").GetString());
        Assert.Equal(
            "2026-11-15T22:01:00.000Z",
            item.GetProperty("last_seen_at").GetString());
        Assert.Equal("detach", item.GetProperty("mode").GetString());
        Assert.Equal(
            2,
            item.GetProperty("domains").GetArrayLength());
        Assert.Equal(
            "/tmp/monitor/amz-monitor.items.jsonl",
            item.GetProperty("data_files")[0].GetString());
        Assert.Equal(
            "systemd-user",
            item.GetProperty("startup").GetString());
        Assert.True(item.GetProperty("startup_enabled").GetBoolean());
        Assert.True(item.GetProperty("startup_registered").GetBoolean());
        Assert.Equal(
            "amz-monitor-abc123.service",
            item.GetProperty("startup_name").GetString());
        Assert.Equal(
            "/tmp/monitor.json",
            item.GetProperty("startup_config_path").GetString());
    }

    [Fact]
    public void Json_of_an_empty_list_is_an_empty_array()
    {
        Assert.Equal("[]", MonitorListRenderer.ToJson([]));
    }

    [Fact]
    public void Json_and_tsv_include_adaptive_schedule_metadata()
    {
        var entries = new[]
        {
            new MonitorListEntry
            {
                Pid = 42,
                Status = "running",
                Mode = "detach",
                Domains = ["it"],
                Targets = 2,
                ScheduleMode = "adaptive",
                WindowSeconds = 86_400,
                MaxRequests = 500,
                MinGapSeconds = 600,
                MaxGapSeconds = 1200,
            },
        };

        using var document = JsonDocument.Parse(
            MonitorListRenderer.ToJson(entries));
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("adaptive", item.GetProperty("schedule_mode").GetString());
        Assert.Equal(500, item.GetProperty("max_requests").GetInt32());

        var row = MonitorListRenderer.ToTsv(entries)
            .Split('\n')[1]
            .Split('\t');
        Assert.Equal("adaptive", row[19]);
        Assert.Equal("86400", row[20]);
        Assert.Equal("500", row[21]);
        Assert.Equal("600", row[22]);
        Assert.Equal("1200", row[23]);
    }

    [Fact]
    public void Tsv_has_a_header_and_one_row_per_entry()
    {
        var entries = new[]
        {
            new MonitorListEntry
            {
                Pid = 1234,
                Status = "running",
                Mode = "detach",
                Startup = "systemd-user",
                StartupEnabled = true,
                StartupRegistered = true,
                StartupName = "amz-monitor-abc123.service",
                StartupId = "abc123",
                StartupConfigPath = "/tmp/monitor.json",
                Domains = ["it", "de"],
                Targets = 3,
                TimerSeconds = 3600,
                StartedAt = "2026-11-15T22:00:00.000Z",
                LastSeenAt = "2026-11-15T22:01:00.000Z",
                SavePath = "/tmp/monitor",
                SaveFile = "amz-monitor",
                DataFiles =
                [
                    "/tmp/monitor/amz-monitor.items.jsonl",
                    "/tmp/monitor/amz-monitor.alerts.jsonl",
                ],
                LogPath = "/tmp/monitor/amz-monitor.log",
                PidFile = "/tmp/monitor/amz-monitor.pid",
            },
        };

        var lines = MonitorListRenderer.ToTsv(entries)
            .Split('\n', StringSplitOptions.None);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("PID\tStatus\tMode\tDomains\t", lines[0]);
        var fields = lines[1].Split('\t');
        Assert.Equal("1234", fields[0]);
        Assert.Equal("running", fields[1]);
        Assert.Equal("detach", fields[2]);
        Assert.Equal("it,de", fields[3]);
        Assert.Equal("3", fields[4]);
        Assert.Equal("3600", fields[5]);
        Assert.Equal(
            "/tmp/monitor/amz-monitor.items.jsonl|" +
            "/tmp/monitor/amz-monitor.alerts.jsonl",
            fields[10]);
        Assert.Equal("systemd-user", fields[13]);
        Assert.Equal("true", fields[14]);
        Assert.Equal("true", fields[15]);
        Assert.Equal("amz-monitor-abc123.service", fields[16]);
        Assert.Equal("abc123", fields[17]);
        Assert.Equal("/tmp/monitor.json", fields[18]);
    }

    [Fact]
    public void Json_and_tsv_include_a_configured_startup_without_a_process()
    {
        var entries = new[]
        {
            new MonitorListEntry
            {
                Pid = 0,
                Status = "configured",
                Mode = "startup",
                Startup = "windows-task",
                StartupEnabled = true,
                StartupRegistered = false,
                StartupName = "AmzCLI-abc123",
                StartupId = "abc123",
                Domains = ["it"],
                Targets = 2,
                TimerSeconds = 3600,
                SavePath = "/tmp/monitor",
                SaveFile = "amz-monitor",
                LogPath = "/tmp/monitor/amz-monitor.log",
            },
        };

        using var document = JsonDocument.Parse(
            MonitorListRenderer.ToJson(entries));
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(0, item.GetProperty("pid").GetInt32());
        Assert.Equal("configured", item.GetProperty("status").GetString());
        Assert.Equal(
            "windows-task",
            item.GetProperty("startup").GetString());

        var row = MonitorListRenderer.ToTsv(entries)
            .Split('\n')[1]
            .Split('\t');
        Assert.Equal("configured", row[1]);
        Assert.Equal("windows-task", row[13]);
        Assert.Equal("false", row[15]);
        Assert.Equal("AmzCLI-abc123", row[16]);
    }

    [Fact]
    public void Tsv_of_an_empty_list_contains_only_the_header()
    {
        var lines = MonitorListRenderer.ToTsv([])
            .Split('\n', StringSplitOptions.None);

        Assert.Single(lines);
        Assert.StartsWith("PID\tStatus\t", lines[0]);
    }
}
