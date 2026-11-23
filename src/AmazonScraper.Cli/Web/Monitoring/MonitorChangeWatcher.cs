using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Cli.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web.Monitoring;

/// <summary>
/// Detects changes made by monitor processes and by the CLI. A short polling
/// interval is used for run/data files because monitors can write to arbitrary
/// save directories; bindings are also included in the fingerprint.
/// </summary>
public sealed class MonitorChangeWatcher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly MonitorBindingStore _bindings;
    private readonly MonitorRegistry _registry;
    private readonly StartupRegistry _startups = new();
    private readonly IMonitorRealtimeNotifier _notifier;
    private readonly ILogger<MonitorChangeWatcher> _logger;

    public MonitorChangeWatcher(
        MonitorBindingStore bindings,
        MonitorRegistry registry,
        IMonitorRealtimeNotifier notifier,
        ILogger<MonitorChangeWatcher> logger)
    {
        _bindings = bindings;
        _registry = registry;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var last = string.Empty;
        var revision = 0L;
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var bindings = _bindings.Read();
                    var runs = new MonitorDiscovery(
                            _registry,
                            [UserStatePaths.MonitorDataRoot()])
                        .Discover(includeStale: true, prune: false)
                        .Entries;
                    var startups = _startups.ReadAll();
                    var signature = BuildSignature(bindings, runs, startups);
                    if (!string.Equals(signature, last, StringComparison.Ordinal))
                    {
                        last = signature;
                        revision = Math.Max(revision + 1, bindings.Revision);
                        await _notifier
                            .NotifyChangedAsync(revision, stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not refresh monitor change signature.");
                }
                if (!await timer.WaitForNextTickAsync(stoppingToken)
                        .ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monitor change watcher stopped unexpectedly.");
        }
    }

    private static string BuildSignature(
        MonitorBindingDocument bindings,
        IReadOnlyList<MonitorListEntry> runs,
        IReadOnlyList<StartupDefinition> startups)
    {
        var parts = new List<string>
        {
            bindings.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bindings.UpdatedAt.ToString("O"),
        };
        foreach (var startup in startups
                     .OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            parts.Add(string.Join('|',
                "startup",
                startup.Id,
                startup.Kind,
                startup.Enabled ? "enabled" : "disabled",
                startup.BindingId ?? string.Empty,
                startup.SavePath,
                startup.SaveFile,
                startup.TimerSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                startup.CommandPreview));
        }
        foreach (var run in runs
                     .OrderBy(entry => entry.BindingId, StringComparer.Ordinal)
                     .ThenBy(entry => entry.Pid))
        {
            parts.Add(string.Join('|',
                run.BindingId ?? string.Empty,
                run.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Status,
                run.LastSeenAt ?? string.Empty,
                run.SavePath ?? string.Empty,
                run.SaveFile ?? string.Empty));
            foreach (var file in run.DataFiles.OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    var info = new FileInfo(file);
                    parts.Add($"{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
                }
                catch (Exception)
                {
                    parts.Add(file + "|missing");
                }
            }
        }
        return string.Join('\n', parts);
    }
}
