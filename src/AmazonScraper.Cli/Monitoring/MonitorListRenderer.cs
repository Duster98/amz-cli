using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using AmazonScraper.Cli.Rendering;
using Spectre.Console;

namespace AmazonScraper.Cli.Monitoring;

public static class MonitorListRenderer
{
    public static void Render(
        IReadOnlyList<MonitorListEntry> entries,
        bool verbose,
        int width = 200)
    {
        if (entries.Count == 0)
        {
            Console.Out.WriteLine("No active monitors.");
            return;
        }

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("PID", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Status", new Style(Color.Cyan))));
        table.AddColumn(new TableColumn("Mode"));
        table.AddColumn(new TableColumn("Startup"));
        table.AddColumn(new TableColumn("Domains"));
        table.AddColumn(new TableColumn("Target") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Timer"));
        table.AddColumn(new TableColumn("Started"));
        table.AddColumn(new TableColumn("Last cycle"));
        table.AddColumn(new TableColumn("Data"));

        foreach (var entry in entries)
        {
            table.AddRow(
                new Text(entry.Pid > 0
                    ? entry.Pid.ToString(CultureInfo.InvariantCulture)
                    : "—"),
                new Markup(FormatStatus(entry.Status)),
                new Text(entry.Mode ?? "—"),
                new Markup(FormatStartup(entry)),
                new Text(entry.Domains.Count == 0 ? "—" : string.Join(", ", entry.Domains)),
                new Text(entry.Targets.ToString(CultureInfo.InvariantCulture)),
                new Text(FormatSchedule(entry)),
                new Text(FormatDate(entry.StartedAt)),
                new Text(FormatDate(entry.LastSeenAt)),
                new Text(DescribeData(entry, verbose)));
        }

        CliConsole.Stdout(width).Write(table);
        if (verbose)
        {
            foreach (var entry in entries)
            {
                if (entry.Pid > 0 && !string.IsNullOrWhiteSpace(entry.LogPath))
                {
                    Console.Out.WriteLine($"Log PID {entry.Pid}: {entry.LogPath}");
                }
                if (entry.Pid > 0 && !string.IsNullOrWhiteSpace(entry.PidFile))
                {
                    Console.Out.WriteLine($"Pidfile PID {entry.Pid}: {entry.PidFile}");
                }
                if (string.Equals(entry.ScheduleMode, "adaptive", StringComparison.Ordinal))
                {
                    var subject = entry.Pid > 0 ? $"PID {entry.Pid}" : "configured";
                    Console.Out.WriteLine(
                        $"Schedule {subject}: adaptive " +
                        $"{entry.MaxRequests}/{entry.WindowSeconds}s " +
                        $"gap {entry.MinGapSeconds}-{entry.MaxGapSeconds}s");
                }
                if (!string.IsNullOrWhiteSpace(entry.Startup))
                {
                    var subject = entry.Pid > 0
                        ? $"PID {entry.Pid}"
                        : "configured";
                    Console.Out.WriteLine(
                        $"Startup {subject}: {entry.Startup} {entry.StartupName}");
                    if (!string.IsNullOrWhiteSpace(entry.StartupConfigPath))
                    {
                        Console.Out.WriteLine(
                            $"Startup config {subject}: {entry.StartupConfigPath}");
                    }
                    if (!string.IsNullOrWhiteSpace(entry.StartupCommandPreview))
                    {
                        Console.Out.WriteLine(
                            $"Startup command {subject}: {entry.StartupCommandPreview}");
                    }
                }
            }
        }
    }

    public static string ToTsv(IReadOnlyList<MonitorListEntry> entries)
    {
        var lines = new List<string>
        {
            "PID\tStatus\tMode\tDomains\tTargets\tTimerSeconds\tStartedAt\tLastSeenAt\tSavePath\tSaveFile\tDataFiles\tLogPath\tPidFile\tStartup\tStartupEnabled\tStartupRegistered\tStartupName\tStartupId\tStartupConfigPath\tScheduleMode\tWindowSeconds\tMaxRequests\tMinGapSeconds\tMaxGapSeconds\tBindingId",
        };
        foreach (var entry in entries)
        {
            lines.Add(string.Join(
                '\t',
                entry.Pid.ToString(CultureInfo.InvariantCulture),
                entry.Status,
                entry.Mode ?? string.Empty,
                string.Join(',', entry.Domains),
                entry.Targets.ToString(CultureInfo.InvariantCulture),
                entry.TimerSeconds.ToString(CultureInfo.InvariantCulture),
                entry.StartedAt ?? string.Empty,
                entry.LastSeenAt ?? string.Empty,
                entry.SavePath ?? string.Empty,
                entry.SaveFile ?? string.Empty,
                string.Join('|', entry.DataFiles),
                entry.LogPath ?? string.Empty,
                entry.PidFile ?? string.Empty,
                entry.Startup ?? string.Empty,
                entry.StartupEnabled ? "true" : "false",
                entry.StartupRegistered?.ToString().ToLowerInvariant() ?? string.Empty,
                entry.StartupName ?? string.Empty,
                entry.StartupId ?? string.Empty,
                entry.StartupConfigPath ?? string.Empty,
                entry.ScheduleMode ?? string.Empty,
                entry.WindowSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.MaxRequests?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.MinGapSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.MaxGapSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.BindingId ?? string.Empty));
        }

        return string.Join('\n', lines);
    }

    public static string ToJson(IReadOnlyList<MonitorListEntry> entries) =>
        JsonSerializer.Serialize(
            entries,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

    private static string DescribeData(MonitorListEntry entry, bool verbose)
    {
        var first = entry.DataFiles.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
        {
            return "—";
        }

        return verbose ? first : Path.GetFileName(first);
    }

    private static string FormatStatus(string status) =>
        status switch
        {
            "running" => "[green]running[/]",
            "stale" => "[red]stale[/]",
            "configured" => "[yellow]configured[/]",
            _ => status,
        };

    private static string FormatStartup(MonitorListEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Startup))
        {
            return "—";
        }

        var label = entry.Startup switch
        {
            "systemd-user" => "systemd",
            "windows-task" => "task",
            _ => entry.Startup,
        };
        label = Markup.Escape(label);
        if (entry.StartupRegistered == false)
        {
            return entry.Startup == "windows-task"
                ? "[red]task missing[/]"
                : "[red]unit missing[/]";
        }

        return entry.StartupEnabled
            ? $"[green]{label}[/]"
            : $"[yellow]{label} (off)[/]";
    }

    private static string FormatSchedule(MonitorListEntry entry)
    {
        if (string.Equals(entry.ScheduleMode, "adaptive", StringComparison.Ordinal)
            && entry.MaxRequests is { } maxRequests
            && entry.WindowSeconds is { } window)
        {
            return $"adaptive {maxRequests}/{FormatTimer(window)}";
        }

        return FormatTimer(entry.TimerSeconds);
    }

    private static string FormatTimer(int seconds)
    {
        if (seconds <= 0)
        {
            return "—";
        }
        if (seconds % 3600 == 0)
        {
            return $"{seconds / 3600}h";
        }
        if (seconds % 60 == 0)
        {
            return $"{seconds / 60}m";
        }
        return $"{seconds}s";
    }

    private static string FormatDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return "—";
        }

        return parsed.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);
    }
}
