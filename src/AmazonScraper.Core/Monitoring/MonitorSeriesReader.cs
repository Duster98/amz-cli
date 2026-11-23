using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AmazonScraper.Core.Monitoring;

public sealed record MonitorSeriesRange(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int MaxPoints = 500);

public sealed record MonitorSeriesPoint(
    DateTimeOffset ObservedAt,
    decimal Price,
    decimal? Min,
    decimal? Max,
    bool Complete,
    int Count);

public sealed record MonitorSeriesAlert(
    DateTimeOffset At,
    string Type,
    string Message,
    decimal? Observed,
    string? Currency);

public sealed record MonitorSeriesData(
    string Kind,
    string? GroupId,
    string? Domain,
    string? Asin,
    string? Currency,
    IReadOnlyList<MonitorSeriesPoint> Points,
    IReadOnlyList<MonitorSeriesAlert> Alerts,
    int Observations,
    bool Truncated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Reads monitor observation files without contacting Amazon. Supports the
/// JSON Lines and CSV formats emitted by MonitorOutput, including combined,
/// per-group and single layouts.
/// </summary>
public static class MonitorSeriesReader
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static MonitorSeriesData ReadProduct(
        IEnumerable<string> dataFiles,
        string domain,
        string asin,
        MonitorSeriesRange range)
    {
        var state = new ReaderState();
        foreach (var file in CleanFiles(dataFiles))
        {
            ReadFile(file, state, domain, asin, groupId: null, wantGroup: false);
        }
        return Build(
            "product",
            groupId: null,
            domain,
            asin,
            state,
            range);
    }

    public static MonitorSeriesData ReadGroup(
        IEnumerable<string> dataFiles,
        string groupId,
        MonitorSeriesRange range)
    {
        var state = new ReaderState();
        foreach (var file in CleanFiles(dataFiles))
        {
            ReadFile(file, state, domain: null, asin: null, groupId, wantGroup: true);
        }
        return Build(
            "group",
            groupId,
            domain: null,
            asin: null,
            state,
            range);
    }

    private static MonitorSeriesData Build(
        string kind,
        string? groupId,
        string? domain,
        string? asin,
        ReaderState state,
        MonitorSeriesRange range)
    {
        var from = range.From;
        var to = range.To;
        var observations = state.Points.Count(point =>
            (from is null || point.ObservedAt >= from)
            && (to is null || point.ObservedAt <= to));
        var points = state.Points
            .Where(point =>
                (from is null || point.ObservedAt >= from)
                && (to is null || point.ObservedAt <= to))
            .OrderBy(point => point.ObservedAt)
            .ToList();
        var alerts = state.Alerts
            .Where(alert =>
                (from is null || alert.At >= from)
                && (to is null || alert.At <= to))
            .OrderBy(alert => alert.At)
            .Take(500)
            .ToList();

        var maxPoints = Math.Clamp(range.MaxPoints, 50, 2000);
        var truncated = points.Count > maxPoints;
        var sampled = truncated ? Downsample(points, maxPoints) : points
            .Select(point => new MonitorSeriesPoint(
                point.ObservedAt,
                point.Price,
                point.Price,
                point.Price,
                point.Complete,
                1))
            .ToList();

        return new MonitorSeriesData(
            kind,
            groupId,
            domain,
            asin,
            points.LastOrDefault()?.Currency ?? state.Currency,
            sampled,
            alerts,
            observations,
            truncated,
            state.Warnings.ToList());
    }

    private static List<MonitorSeriesPoint> Downsample(
        IReadOnlyList<SamplePoint> points,
        int maxPoints)
    {
        var first = points[0].ObservedAt;
        var last = points[^1].ObservedAt;
        var span = last - first;
        if (span <= TimeSpan.Zero)
        {
            return points.Take(maxPoints).Select(ToPoint).ToList();
        }

        var width = TimeSpan.FromTicks(
            Math.Max(1, span.Ticks / maxPoints));
        var buckets = new Dictionary<long, Bucket>();
        foreach (var point in points)
        {
            var key = Math.Min(
                maxPoints - 1L,
                (point.ObservedAt - first).Ticks / width.Ticks);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket(point);
                buckets[key] = bucket;
            }
            else
            {
                bucket.Add(point);
            }
        }

        return buckets
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToPoint())
            .ToList();
    }

    private static MonitorSeriesPoint ToPoint(SamplePoint point) =>
        new(point.ObservedAt, point.Price, point.Price, point.Price,
            point.Complete, 1);

    private static IEnumerable<string> CleanFiles(IEnumerable<string> files) =>
        files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static void ReadFile(
        string path,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId,
        bool wantGroup)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (string.Equals(Path.GetExtension(path), ".csv",
                    StringComparison.OrdinalIgnoreCase))
            {
                ReadCsv(path, state, domain, asin, groupId, wantGroup);
            }
            else
            {
                ReadJsonLines(path, state, domain, asin, groupId, wantGroup);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            state.Warnings.Add($"Could not read {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void ReadJsonLines(
        string path,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId,
        bool wantGroup)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var malformed = 0;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = GetString(root, "record_type");
                if (string.Equals(type, "item", StringComparison.Ordinal))
                {
                    ReadItemJson(root, state, domain, asin, groupId, wantGroup);
                }
                else if (string.Equals(type, "group", StringComparison.Ordinal))
                {
                    ReadGroupJson(root, state, groupId, wantGroup);
                }
                else if (string.Equals(type, "alert", StringComparison.Ordinal))
                {
                    ReadAlertJson(root, state, domain, asin, groupId);
                }
            }
            catch (JsonException)
            {
                malformed++;
            }
        }
        if (malformed > 0)
        {
            state.Warnings.Add(
                $"{malformed} malformed line(s) ignored in {Path.GetFileName(path)}.");
        }
    }

    private static void ReadItemJson(
        JsonElement root,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId,
        bool wantGroup)
    {
        if (wantGroup)
        {
            return;
        }
        if (!string.Equals(GetString(root, "domain"), domain,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GetString(root, "asin"), asin,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(GetString(root, "error")))
        {
            return;
        }
        if (!TryDate(root, "observed_at", out var at)
            || !TryDecimal(root, "price_amount", out var price)
            || price <= 0)
        {
            return;
        }
        state.Add(new SamplePoint(
            at,
            price,
            Complete: true,
            GetString(root, "currency") ?? state.Currency));
    }

    private static void ReadGroupJson(
        JsonElement root,
        ReaderState state,
        string? groupId,
        bool wantGroup)
    {
        if (!wantGroup
            || !string.Equals(GetString(root, "group_id"), groupId,
                StringComparison.Ordinal))
        {
            return;
        }
        if (!TryDate(root, "observed_at", out var at)
            || !TryDecimal(root, "total", out var total)
            || total <= 0)
        {
            return;
        }
        state.Add(new SamplePoint(
            at,
            total,
            Complete: GetBool(root, "complete") ?? true,
            GetString(root, "currency") ?? state.Currency));
    }

    private static void ReadAlertJson(
        JsonElement root,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId)
    {
        var alertDomain = GetString(root, "domain");
        var alertAsin = GetString(root, "asin");
        var alertGroup = GetString(root, "group_id");
        var matches = groupId is not null
            ? string.Equals(alertGroup, groupId, StringComparison.Ordinal)
            : string.Equals(alertDomain, domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alertAsin, asin, StringComparison.OrdinalIgnoreCase);
        if (!matches || !TryDate(root, "alerted_at", out var at))
        {
            return;
        }
        state.Alerts.Add(new MonitorSeriesAlert(
            at,
            GetString(root, "type") ?? "alert",
            GetString(root, "message") ?? string.Empty,
            TryDecimal(root, "observed", out var observed) ? observed : null,
            GetString(root, "currency")));
    }

    private static void ReadCsv(
        string path,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId,
        bool wantGroup)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var headerLine = reader.ReadLine();
        if (headerLine is null)
        {
            return;
        }
        var header = ParseCsv(headerLine)
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .Where(item => item.Name.Length > 0)
            .ToDictionary(item => item.Name, item => item.Index,
                StringComparer.OrdinalIgnoreCase);
        var malformed = 0;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var fields = ParseCsv(line);
            if (fields.Length == 0)
            {
                continue;
            }
            var type = Field(header, fields, "record_type");
            try
            {
                if (string.Equals(type, "item", StringComparison.Ordinal))
                {
                    ReadItemCsv(header, fields, state, domain, asin);
                }
                else if (string.Equals(type, "group", StringComparison.Ordinal))
                {
                    ReadGroupCsv(header, fields, state, groupId, wantGroup);
                }
                else if (string.Equals(type, "alert", StringComparison.Ordinal))
                {
                    ReadAlertCsv(header, fields, state, domain, asin, groupId);
                }
            }
            catch (FormatException)
            {
                malformed++;
            }
        }
        if (malformed > 0)
        {
            state.Warnings.Add(
                $"{malformed} malformed row(s) ignored in {Path.GetFileName(path)}.");
        }
    }

    private static void ReadItemCsv(
        IReadOnlyDictionary<string, int> header,
        string[] fields,
        ReaderState state,
        string? domain,
        string? asin)
    {
        if (!string.Equals(Field(header, fields, "domain"), domain,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Field(header, fields, "asin"), asin,
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(Field(header, fields, "error")))
        {
            return;
        }
        if (!TryParseDate(Field(header, fields, "observed_at"), out var at)
            || !TryParseDecimal(Field(header, fields, "price_amount"), out var price)
            || price <= 0)
        {
            return;
        }
        state.Add(new SamplePoint(
            at,
            price,
            Complete: true,
            Field(header, fields, "currency")));
    }

    private static void ReadGroupCsv(
        IReadOnlyDictionary<string, int> header,
        string[] fields,
        ReaderState state,
        string? groupId,
        bool wantGroup)
    {
        if (!wantGroup
            || !string.Equals(Field(header, fields, "group_id"), groupId,
                StringComparison.Ordinal))
        {
            return;
        }
        if (!TryParseDate(Field(header, fields, "observed_at"), out var at)
            || !TryParseDecimal(Field(header, fields, "total"), out var total)
            || total <= 0)
        {
            return;
        }
        var completeText = Field(header, fields, "complete");
        var complete = !string.Equals(completeText, "false",
            StringComparison.OrdinalIgnoreCase);
        state.Add(new SamplePoint(
            at,
            total,
            complete,
            Field(header, fields, "currency")));
    }

    private static void ReadAlertCsv(
        IReadOnlyDictionary<string, int> header,
        string[] fields,
        ReaderState state,
        string? domain,
        string? asin,
        string? groupId)
    {
        var alertDomain = Field(header, fields, "domain");
        var alertAsin = Field(header, fields, "asin");
        var alertGroup = Field(header, fields, "group_id");
        var matches = groupId is not null
            ? string.Equals(alertGroup, groupId, StringComparison.Ordinal)
            : string.Equals(alertDomain, domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alertAsin, asin, StringComparison.OrdinalIgnoreCase);
        if (!matches
            || !TryParseDate(Field(header, fields, "alerted_at"), out var at))
        {
            return;
        }
        state.Alerts.Add(new MonitorSeriesAlert(
            at,
            Field(header, fields, "type"),
            Field(header, fields, "message"),
            TryParseDecimal(Field(header, fields, "observed"), out var observed)
                ? observed
                : null,
            Field(header, fields, "currency")));
    }

    private static string Field(
        IReadOnlyDictionary<string, int> header,
        string[] fields,
        string name) =>
        header.TryGetValue(name, out var index)
            && index < fields.Length
            ? fields[index]
            : string.Empty;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static bool TryDate(
        JsonElement root,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && TryParseDate(element.GetString(), out value);
    }

    private static bool TryDecimal(
        JsonElement root,
        string name,
        out decimal value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind is JsonValueKind.Number
            && element.TryGetDecimal(out value);
    }

    private static bool TryParseDate(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            Invariant,
            DateTimeStyles.AssumeUniversal,
            out result);

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            Invariant,
            out result);

    private static string[] ParseCsv(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        builder.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    builder.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }
        fields.Add(builder.ToString());
        return fields.ToArray();
    }

    private sealed class ReaderState
    {
        public List<SamplePoint> Points { get; } = [];

        public List<MonitorSeriesAlert> Alerts { get; } = [];

        public List<string> Warnings { get; } = [];

        public string? Currency { get; set; }

        public void Add(SamplePoint point)
        {
            Points.Add(point);
            if (!string.IsNullOrWhiteSpace(point.Currency))
            {
                Currency = point.Currency;
            }
        }
    }

    private sealed record SamplePoint(
        DateTimeOffset ObservedAt,
        decimal Price,
        bool Complete,
        string? Currency);

    private sealed class Bucket
    {
        private DateTimeOffset _lastAt;
        private decimal _lastPrice;
        private decimal _min;
        private decimal _max;
        private bool _complete = true;
        private int _count;

        public Bucket(SamplePoint point)
        {
            _lastAt = point.ObservedAt;
            _lastPrice = point.Price;
            _min = point.Price;
            _max = point.Price;
            _complete = point.Complete;
            _count = 1;
        }

        public void Add(SamplePoint point)
        {
            _lastAt = point.ObservedAt;
            _lastPrice = point.Price;
            _min = Math.Min(_min, point.Price);
            _max = Math.Max(_max, point.Price);
            _complete &= point.Complete;
            _count++;
        }

        public MonitorSeriesPoint ToPoint() =>
            new(_lastAt, _lastPrice, _min, _max, _complete, _count);
    }
}
