using System.Globalization;
using System.Text.Json;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Core.Formatting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class HistoryListCommand : Command<HistoryListSettings>
{
    public override int Execute(
        CommandContext context,
        HistoryListSettings settings,
        CancellationToken cancellationToken)
    {
        return HistoryCommandSupport.Run(() =>
        {
            var store = new HistoryStore();
            store.MarkStaleRunning(TimeSpan.FromHours(6));
            var document = store.Read();
            var entries = document.Entries.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(settings.Kind))
            {
                var kind = settings.Kind.Trim().ToLowerInvariant();
                entries = entries.Where(entry => entry.Kind == kind);
            }
            if (!string.IsNullOrWhiteSpace(settings.Source))
            {
                var source = settings.Source.Trim().ToLowerInvariant();
                entries = entries.Where(entry => entry.Source == source);
            }
            if (!string.IsNullOrWhiteSpace(settings.Status))
            {
                var status = settings.Status.Trim().ToLowerInvariant();
                entries = entries.Where(entry => entry.Status == status);
            }
            HistoryOutput.Write(
                document,
                entries.ToList(),
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class HistoryRemoveCommand : Command<HistoryRemoveSettings>
{
    public override int Execute(
        CommandContext context,
        HistoryRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        return HistoryCommandSupport.Run(() =>
        {
            var store = new HistoryStore();
            var cache = new HistoryCacheStore();
            var document = store.Read();
            var requested = settings.Ids
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var removed = new List<HistoryEntry>();
            foreach (var id in requested)
            {
                var entry = document.Entries.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.Ordinal));
                if (entry is null)
                {
                    CliConsole.Error($"History entry not found: {id}");
                    continue;
                }
                document = store.Delete(id);
                cache.Delete(id);
                removed.Add(entry);
            }

            if (removed.Count == 0)
            {
                return 1;
            }

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        removed = removed.Select(entry => entry.Id),
                        revision = document.Revision,
                        entries = document.Entries,
                    },
                    HistoryStore.JsonOptions));
            }
            else if (settings.TkOpt)
            {
                foreach (var entry in removed)
                {
                    Console.WriteLine(entry.Id);
                }
            }
            else
            {
                Console.WriteLine(
                    removed.Count == 1
                        ? "Removed 1 history entry."
                        : $"Removed {removed.Count} history entries.");
            }
            return removed.Count == requested.Count ? 0 : 1;
        });
    }
}

public sealed class HistoryClearCommand : Command<HistoryClearSettings>
{
    public override int Execute(
        CommandContext context,
        HistoryClearSettings settings,
        CancellationToken cancellationToken)
    {
        return HistoryCommandSupport.Run(() =>
        {
            var store = new HistoryStore();
            var cache = new HistoryCacheStore();
            var before = store.Read();
            var document = store.Clear();
            cache.Clear();
            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        removed = before.Entries.Count,
                        revision = document.Revision,
                    },
                    HistoryStore.JsonOptions));
            }
            else if (settings.TkOpt)
            {
                Console.WriteLine(before.Entries.Count.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                Console.WriteLine(
                    before.Entries.Count == 1
                        ? "Cleared 1 history entry."
                        : $"Cleared {before.Entries.Count} history entries.");
            }
            return 0;
        });
    }
}

internal static class HistoryCommandSupport
{
    public static int Run(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is HistoryException or ArgumentException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }
}

internal static class HistoryOutput
{
    public static void Write(
        HistoryDocument document,
        IReadOnlyList<HistoryEntry> entries,
        OutputMode mode,
        int width)
    {
        if (mode == OutputMode.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    schema = document.Schema,
                    revision = document.Revision,
                    updated_at = document.UpdatedAt,
                    entries,
                },
                HistoryStore.JsonOptions));
            return;
        }

        if (mode == OutputMode.Tsv)
        {
            Console.WriteLine("id\tkind\tsource\tstatus\tat\ttitle\tresult_count");
            foreach (var entry in entries)
            {
                Console.WriteLine(string.Join('\t',
                    Escape(entry.Id),
                    Escape(entry.Kind),
                    Escape(entry.Source),
                    Escape(entry.Status),
                    Escape(entry.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                    Escape(entry.Kind == HistoryKind.Search
                        ? entry.Query ?? string.Empty
                        : entry.Snapshot?.Title ?? entry.Asin ?? string.Empty),
                    entry.ResultCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            }
            return;
        }

        if (entries.Count == 0)
        {
            CliConsole.Stdout(width).MarkupLine("[grey]No history entries.[/]");
            return;
        }

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("ID", new Style(Color.Yellow)))
        {
            Width = 16,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Kind")) { Width = 9 });
        table.AddColumn(new TableColumn(new Text("Source")) { Width = 9 });
        table.AddColumn(new TableColumn(new Text("Status")) { Width = 12 });
        table.AddColumn(new TableColumn(new Text("When", new Style(Color.Grey)))
        {
            Width = 17,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Title")) { Width = 60 });
        table.AddColumn(new TableColumn(new Text("Results", new Style(Color.Green)))
        {
            Width = 9,
            NoWrap = true,
        });
        foreach (var entry in entries)
        {
            var title = entry.Kind == HistoryKind.Search
                ? entry.Query ?? string.Empty
                : entry.Snapshot?.Title ?? entry.Asin ?? string.Empty;
            table.AddRow(
                new Text(entry.Id, new Style(Color.Yellow)),
                new Text(entry.Kind),
                new Text(entry.Source),
                new Text(entry.Status),
                new Text(entry.At.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture),
                    new Style(Color.Grey)),
                new Text(TextCleaner.Clean(title, 58))
                    .Overflow(Overflow.Ellipsis),
                new Text(entry.ResultCount?.ToString(
                    CultureInfo.InvariantCulture) ?? "-",
                    new Style(Color.Green)));
        }
        CliConsole.Stdout(width).Write(table);
    }

    private static string Escape(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
