namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Routes item, group and alert records to the selected layout. Writers are
/// opened lazily, so an empty alert file is not created until the first alert.
/// </summary>
public sealed class MonitorOutput : IAsyncDisposable
{
    private readonly MonitorPaths _paths;
    private readonly MonitorFormat _format;
    private readonly MonitorLayout _layout;
    private readonly Dictionary<string, MonitorFileWriter> _writers =
        new(StringComparer.Ordinal);

    private MonitorOutput(
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout layout)
    {
        _paths = paths;
        _format = format;
        _layout = layout;
    }

    public MonitorPaths Paths => _paths;

    public MonitorLayout Layout => _layout;

    public IReadOnlyList<string> CreatedFiles =>
        _writers.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();

    public static MonitorOutput Open(
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout requestedLayout,
        MonitorPlan plan)
    {
        var layout = ResolveLayout(requestedLayout, format, plan);
        Directory.CreateDirectory(paths.SavePath);
        if (layout == MonitorLayout.PerGroup)
        {
            Directory.CreateDirectory(paths.PerGroupRoot);
        }

        return new MonitorOutput(paths, format, layout);
    }

    public static MonitorLayout ResolveLayout(
        MonitorLayout requested,
        MonitorFormat format,
        MonitorPlan plan)
    {
        var layout = requested == MonitorLayout.Auto
            ? (plan.Groups.Count > 0 ? MonitorLayout.PerGroup : MonitorLayout.Combined)
            : requested;

        if (layout == MonitorLayout.Single && format == MonitorFormat.Csv)
        {
            throw new MonitorException(
                "--layout single is supported only with --save-format json/jsonl.");
        }

        return layout;
    }

    public static IReadOnlyList<string> DescribeFiles(
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout requestedLayout,
        MonitorPlan plan)
    {
        var layout = ResolveLayout(requestedLayout, format, plan);
        var files = new List<string>();
        switch (layout)
        {
            case MonitorLayout.Single:
                files.Add(paths.SinglePath(format));
                break;
            case MonitorLayout.Combined:
                files.Add(paths.CombinedPath("items", format));
                if (plan.Groups.Count > 0)
                {
                    files.Add(paths.CombinedPath("groups", format));
                }
                files.Add(paths.CombinedPath("alerts", format));
                break;
            case MonitorLayout.PerGroup:
                foreach (var group in plan.Groups)
                {
                    if (format == MonitorFormat.JsonLines)
                    {
                        files.Add(paths.PerGroupJsonlPath(group.Id));
                    }
                    else
                    {
                        files.Add(paths.PerGroupCsvPath(group.Id, "items"));
                        files.Add(paths.PerGroupCsvPath(group.Id, "groups"));
                        files.Add(paths.PerGroupCsvPath(group.Id, "alerts"));
                    }
                }
                if (plan.Ungrouped.Count > 0)
                {
                    if (format == MonitorFormat.JsonLines)
                    {
                        files.Add(paths.PerGroupJsonlPath(null));
                    }
                    else
                    {
                        files.Add(paths.PerGroupCsvPath(null, "items"));
                        files.Add(paths.PerGroupCsvPath(null, "alerts"));
                    }
                }
                break;
        }

        return files;
    }

    public async Task WriteItemAsync(
        MonitorItemObservation observation,
        CancellationToken cancellationToken)
    {
        var writer = GetItemWriter(observation.GroupId);
        var line = _format == MonitorFormat.Csv
            ? MonitorCsv.FormatItem(observation)
            : MonitorJson.Item(observation);
        await writer.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteGroupAsync(
        MonitorGroupObservation observation,
        CancellationToken cancellationToken)
    {
        var writer = GetGroupWriter(observation.GroupId);
        var line = _format == MonitorFormat.Csv
            ? MonitorCsv.FormatGroup(observation)
            : MonitorJson.Group(observation);
        await writer.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAlertAsync(
        MonitorAlert alert,
        CancellationToken cancellationToken)
    {
        var writer = GetAlertWriter(alert.GroupId);
        var line = _format == MonitorFormat.Csv
            ? MonitorCsv.FormatAlert(alert)
            : MonitorJson.Alert(alert);
        await writer.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers.Values)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private MonitorFileWriter GetItemWriter(string? groupId) =>
        _layout switch
        {
            MonitorLayout.Single => GetSingleWriter(),
            MonitorLayout.Combined => GetWriter(
                _paths.CombinedPath("items", _format),
                _format == MonitorFormat.Csv ? MonitorCsv.ItemHeader : null),
            MonitorLayout.PerGroup => GetPerGroupWriter(
                groupId,
                recordType: "items",
                _format == MonitorFormat.Csv ? MonitorCsv.ItemHeader : null),
            _ => throw new InvalidOperationException($"Unsupported layout: {_layout}"),
        };

    private MonitorFileWriter GetGroupWriter(string groupId) =>
        _layout switch
        {
            MonitorLayout.Single => GetSingleWriter(),
            MonitorLayout.Combined => GetWriter(
                _paths.CombinedPath("groups", _format),
                _format == MonitorFormat.Csv ? MonitorCsv.GroupHeader : null),
            MonitorLayout.PerGroup => GetPerGroupWriter(
                groupId,
                recordType: "groups",
                _format == MonitorFormat.Csv ? MonitorCsv.GroupHeader : null),
            _ => throw new InvalidOperationException($"Unsupported layout: {_layout}"),
        };

    private MonitorFileWriter GetAlertWriter(string? groupId) =>
        _layout switch
        {
            MonitorLayout.Single => GetSingleWriter(),
            MonitorLayout.Combined => GetWriter(
                _paths.CombinedPath("alerts", _format),
                _format == MonitorFormat.Csv ? MonitorCsv.AlertHeader : null),
            MonitorLayout.PerGroup => GetPerGroupWriter(
                groupId,
                recordType: "alerts",
                _format == MonitorFormat.Csv ? MonitorCsv.AlertHeader : null),
            _ => throw new InvalidOperationException($"Unsupported layout: {_layout}"),
        };

    private MonitorFileWriter GetSingleWriter() =>
        GetWriter(
            _paths.SinglePath(_format),
            _format == MonitorFormat.Csv ? MonitorCsv.ItemHeader : null);

    private MonitorFileWriter GetPerGroupWriter(
        string? groupId,
        string recordType,
        string? csvHeader)
    {
        var path = _format == MonitorFormat.JsonLines
            ? _paths.PerGroupJsonlPath(groupId)
            : _paths.PerGroupCsvPath(groupId, recordType);
        return GetWriter(path, csvHeader);
    }

    private MonitorFileWriter GetWriter(string path, string? csvHeader)
    {
        if (_writers.TryGetValue(path, out var writer))
        {
            return writer;
        }

        writer = MonitorFileWriter.Open(path, csvHeader);
        _writers[path] = writer;
        return writer;
    }
}
