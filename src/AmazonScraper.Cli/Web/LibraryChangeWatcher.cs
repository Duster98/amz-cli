using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web;

/// <summary>
/// Watches the shared library file written by either the web process or a
/// separate CLI process. Events are debounced and deduplicated by revision.
/// </summary>
public sealed class LibraryChangeWatcher : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);

    private readonly string _path;
    private readonly string _directory;
    private readonly ILibraryRealtimeNotifier _notifier;
    private readonly ILogger<LibraryChangeWatcher> _logger;
    private readonly Channel<byte> _signals =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public LibraryChangeWatcher(
        AmazonScraper.Cli.Library.SavedLibraryStore store,
        ILibraryRealtimeNotifier notifier,
        ILogger<LibraryChangeWatcher> logger)
    {
        _path = Path.GetFullPath(store.FilePath);
        _directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException(
                $"Library path has no directory: {_path}");
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_directory);
        var lastRevision = ReadRevision() ?? 0;
        using var watcher = new FileSystemWatcher(
            _directory,
            Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        FileSystemEventHandler signal = (_, _) => Signal();
        RenamedEventHandler renamed = (_, _) => Signal();
        watcher.Changed += signal;
        watcher.Created += signal;
        watcher.Deleted += signal;
        watcher.Renamed += renamed;
        watcher.Error += (_, args) =>
            _logger.LogWarning(
                args.GetException(),
                "Library file watcher error.");
        watcher.EnableRaisingEvents = true;

        using var heartbeat = new PeriodicTimer(Heartbeat);
        var signalTask = _signals.Reader.WaitToReadAsync(stoppingToken).AsTask();
        var heartbeatTask = heartbeat.WaitForNextTickAsync(stoppingToken).AsTask();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(signalTask, heartbeatTask);
                if (completed == signalTask)
                {
                    if (!await signalTask)
                    {
                        break;
                    }
                    while (_signals.Reader.TryRead(out _))
                    {
                    }
                    await Task.Delay(Debounce, stoppingToken);
                    lastRevision = await CheckRevisionAsync(
                        lastRevision,
                        stoppingToken);
                    signalTask = _signals.Reader
                        .WaitToReadAsync(stoppingToken)
                        .AsTask();
                }
                else
                {
                    if (!await heartbeatTask)
                    {
                        break;
                    }
                    lastRevision = await CheckRevisionAsync(
                        lastRevision,
                        stoppingToken);
                    heartbeatTask = heartbeat
                        .WaitForNextTickAsync(stoppingToken)
                        .AsTask();
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library change watcher stopped unexpectedly.");
        }
    }

    private async Task<long> CheckRevisionAsync(
        long lastRevision,
        CancellationToken cancellationToken)
    {
        var revision = ReadRevision();
        if (revision is not { } value || value == lastRevision)
        {
            return lastRevision;
        }

        try
        {
            await _notifier
                .NotifyLibraryChangedAsync(value, cancellationToken)
                .ConfigureAwait(false);
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not broadcast library revision {Revision}.",
                value);
            return lastRevision;
        }
    }

    private long? ReadRevision()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return 0;
                }
                using var document = JsonDocument.Parse(
                    File.ReadAllText(_path));
                return document.RootElement.TryGetProperty(
                    "revision",
                    out var revision)
                    && revision.TryGetInt64(out var value)
                    ? value
                    : 0;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                if (attempt == 2)
                {
                    _logger.LogDebug(
                        ex,
                        "Could not read library revision from {Path}.",
                        _path);
                    return null;
                }
                Thread.Sleep(50);
            }
        }
        return null;
    }

    private void Signal()
    {
        _signals.Writer.TryWrite(0);
    }
}
