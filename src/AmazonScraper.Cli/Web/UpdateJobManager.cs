using System.Diagnostics;
using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Update;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web;

public interface IUpdateExecutor
{
    Task<UpdateCheckResult> CheckAsync(
        string channel,
        CancellationToken cancellationToken);

    Task<UpdateApplyOutcome> ApplyAsync(
        UpdateCheckResult check,
        bool requireCurl,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken,
        AutoUpdateContext auto,
        WebUpdateContext web);
}

public sealed class UpdateExecutor : IUpdateExecutor
{
    public async Task<UpdateCheckResult> CheckAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        using var api = new UpdateApiClient(
            currentVersion: VersionInfo.Informational);
        var service = new UpdateService(
            api,
            VersionInfo.Informational,
            options: new UpdateOptions
            {
                CheckTimeout = TimeSpan.FromSeconds(30),
            });
        return await service
            .CheckAsync(channel, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UpdateApplyOutcome> ApplyAsync(
        UpdateCheckResult check,
        bool requireCurl,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken,
        AutoUpdateContext auto,
        WebUpdateContext web)
    {
        using var api = new UpdateApiClient(
            currentVersion: VersionInfo.Informational);
        var service = new UpdateService(
            api,
            VersionInfo.Informational,
            options: new UpdateOptions
            {
                CheckTimeout = TimeSpan.FromSeconds(30),
                DownloadTimeout = TimeSpan.FromMinutes(15),
            });
        return await service
            .ApplyAsync(
                check,
                requireCurl,
                progress,
                cancellationToken,
                auto,
                web)
            .ConfigureAwait(false);
    }
}

public sealed class UpdateJobSnapshot
{
    public long Revision { get; init; }

    public string Phase { get; init; } = "idle";

    public string Message { get; init; } = string.Empty;

    public UpdateProgress? Progress { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class UpdateCheckView
{
    public string Status { get; init; } = string.Empty;

    public bool CanApply { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string? LatestVersion { get; init; }

    public string Platform { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; init; }

    public string? MinSupportedVersion { get; init; }

    public long DownloadSize { get; init; }

    public int FileCount { get; init; }

    public string? Notes { get; init; }

    public string? NotesMarkdown { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class CurlStatusView
{
    public bool Available { get; init; }

    public string Mode { get; init; } = "unavailable";

    public string Version { get; init; } = "unavailable";

    public string Path { get; init; } = "unavailable";

    public string RuntimeId { get; init; } = "unavailable";

    public string Error { get; init; } = string.Empty;

    public IReadOnlyList<CurlRuntimeFile> Files { get; init; } = [];
}

public sealed class UpdateBackupView
{
    public string Version { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public long Size { get; init; }
}

public sealed class UpdateStatusView
{
    public string CurrentVersion { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public string InstallDir { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public UpdateCheckView? Check { get; init; }

    public UpdateJobSnapshot Job { get; init; } = new();

    public UpdateJournal? Journal { get; init; }

    public IReadOnlyList<UpdateBackupView> Backups { get; init; } = [];

    public AutoUpdateConfig? AutoUpdate { get; init; }

    public AutoUpdateState? AutoState { get; init; }

    public CurlStatusView Curl { get; init; } = new();
}

public sealed class UpdateJobManager : IDisposable
{
    private readonly object _gate = new();
    private readonly IUpdateExecutor _executor;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IUpdateRealtimeNotifier _notifier;
    private readonly SearchJobManager _jobs;
    private readonly WebOptions _options;
    private readonly WebSessionState _session;
    private readonly ILogger _logger;
    private readonly string _resumePath;
    private readonly SelfTestResult _runtime;
    private readonly CurlStatusView _curl;
    private UpdateCheckResult? _check;
    private UpdateJobSnapshot _snapshot = new();
    private long _revision;
    private bool _busy;
    private bool _disposed;
    private long _lastProgressTimestamp;
    private int? _lastPercent;
    private long _lastBytes = -1;

    public UpdateJobManager(
        IUpdateExecutor executor,
        IHostApplicationLifetime lifetime,
        IUpdateRealtimeNotifier notifier,
        SearchJobManager jobs,
        WebOptions options,
        WebSessionState session,
        ILogger logger,
        string? resumePath = null)
    {
        _executor = executor;
        _lifetime = lifetime;
        _notifier = notifier;
        _jobs = jobs;
        _options = options;
        _session = session;
        _logger = logger;
        _resumePath = resumePath ?? WebPaths.UpdateResumePath();
        _runtime = RuntimeDiagnostics.Run(VersionInfo.Informational);
        _curl = new CurlStatusView
        {
            Available = _runtime.CurlAvailable,
            Mode = _runtime.CurlMode,
            Version = _runtime.CurlVersion,
            Path = OperatingSystem.IsWindows()
                ? AppContext.BaseDirectory
                : _runtime.CurlRuntimeIdentifier,
            RuntimeId = _runtime.CurlRuntimeIdentifier,
            Error = _runtime.CurlError,
            Files = _runtime.CurlFiles,
        };
    }

    public UpdateJobSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public UpdateStatusView GetStatus()
    {
        var report = VersionReport.Read();
        var installDir = report.InstallDir;
        UpdateCheckView? check;
        UpdateJobSnapshot job;
        lock (_gate)
        {
            check = _check is null ? null : ToView(_check);
            job = _snapshot;
        }

        var journal = UpdateJournalStore.Read();
        if (journal is not null
            && !string.IsNullOrWhiteSpace(journal.InstallDir)
            && !string.Equals(
                journal.InstallDir,
                installDir,
                StringComparison.Ordinal))
        {
            journal = null;
        }
        var backups = InstallBackup
            .List(installDir: installDir)
            .Select(backup => new UpdateBackupView
            {
                Version = backup.Version,
                Platform = backup.Platform,
                CreatedAt = backup.CreatedAt,
                Size = backup.Size,
            })
            .ToList();
        var auto = AutoUpdateConfig.Load();

        return new UpdateStatusView
        {
            CurrentVersion = report.Version,
            Platform = report.Platform,
            Channel = string.IsNullOrWhiteSpace(auto.Channel)
                ? "stable"
                : auto.Channel,
            InstallDir = installDir,
            Source = report.Source,
            Check = check,
            Job = job,
            Journal = journal,
            Backups = backups,
            AutoUpdate = auto,
            AutoState = AutoUpdateState.Read(),
            Curl = _curl,
        };
    }

    public bool TryStartCheck(out string? error)
    {
        lock (_gate)
        {
            if (_busy)
            {
                error = "update_busy";
                return false;
            }
            _busy = true;
            _check = null;
        }

        error = null;
        SetState("checking", "Checking for updates");
        _ = Task.Run(CheckCoreAsync);
        return true;
    }

    public bool TryStartApply(out string? error)
    {
        UpdateCheckResult? check;
        lock (_gate)
        {
            if (_busy)
            {
                error = "update_busy";
                return false;
            }
            check = _check;
            _busy = true;
        }

        if (check is null || !check.CanApply)
        {
            lock (_gate)
            {
                _busy = false;
            }
            error = "update_not_available";
            return false;
        }
        if (_jobs.HasActiveJobs)
        {
            lock (_gate)
            {
                _busy = false;
            }
            error = "search_in_progress";
            return false;
        }

        WebResumeState resume;
        try
        {
            resume = new WebResumeState
            {
                Port = _session.Port,
                Token = _session.Token,
                InstanceId = _session.InstanceId,
                LogPath = _options.LogPath,
                Desktop = _options.Desktop,
                Remote = _options.Remote,
                Development = _options.Development,
                WebDirectory = _options.WebDirectory,
                IdleMinutes = _options.IdleMinutes,
            };
            WebResumeStateStore.Write(resume, _resumePath);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _busy = false;
            }
            _logger.LogWarning(ex, "Could not prepare the Web update resume state.");
            error = "update_resume_failed";
            return false;
        }

        error = null;
        SetState("preparing", "Preparing the update");
        _ = Task.Run(() => ApplyCoreAsync(check, resume));
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
    }

    private async Task CheckCoreAsync()
    {
        try
        {
            var channel = AutoUpdateConfig.Load().Channel;
            if (channel is not ("stable" or "beta"))
            {
                channel = "stable";
            }
            var check = await _executor
                .CheckAsync(channel, _lifetime.ApplicationStopping)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _check = check;
            }
            var phase = check.Status switch
            {
                UpdateStatus.UpdateAvailable => "update_available",
                UpdateStatus.UpToDate => "up_to_date",
                UpdateStatus.ManualRequired => "manual_required",
                _ => "no_release",
            };
            SetState(phase, check.Message);
        }
        catch (OperationCanceledException)
        {
            SetState("cancelled", "Update check cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web update check failed.");
            SetState("failed", "Update check failed", ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    private async Task ApplyCoreAsync(
        UpdateCheckResult check,
        WebResumeState resume)
    {
        var resumePath = _resumePath;
        try
        {
            var progress = new Progress<UpdateProgress>(ReportProgress);
            var outcome = await _executor
                .ApplyAsync(
                    check,
                    _runtime.CurlAvailable || OperatingSystem.IsWindows(),
                    progress,
                    _lifetime.ApplicationStopping,
                    new AutoUpdateContext(
                        "web",
                        [],
                        RestartMonitors: true,
                        DeferForegroundMonitor: false),
                    new WebUpdateContext(
                        resumePath,
                        ForceMonitorRestart: true))
                .ConfigureAwait(false);

            SetState(
                "scheduled",
                outcome.Message,
                progress: null);
            TryNotifyChanged();
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1200))
                    .ConfigureAwait(false);
                _lifetime.StopApplication();
            });
        }
        catch (OperationCanceledException)
        {
            WebResumeStateStore.Delete(_resumePath);
            SetState("cancelled", "Update cancelled");
        }
        catch (Exception ex)
        {
            WebResumeStateStore.Delete(_resumePath);
            _logger.LogWarning(ex, "Web update failed before scheduling.");
            SetState("failed", "Update failed", ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    private void ReportProgress(UpdateProgress progress)
    {
        lock (_gate)
        {
            if (!_busy)
            {
                return;
            }
        }

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastProgressTimestamp);
        var elapsed = last == 0
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds(
                (now - last) / (double)Stopwatch.Frequency);
        if (elapsed < TimeSpan.FromMilliseconds(250)
            && progress.Percent == _lastPercent
            && progress.Bytes == _lastBytes)
        {
            return;
        }
        _lastPercent = progress.Percent;
        _lastBytes = progress.Bytes;
        Interlocked.Exchange(ref _lastProgressTimestamp, now);
        SetProgress(progress);
    }

    private void SetProgress(UpdateProgress progress)
    {
        UpdateProgressEvent? updateEvent = null;
        lock (_gate)
        {
            _revision++;
            _snapshot = new UpdateJobSnapshot
            {
                Revision = _revision,
                Phase = "preparing",
                Message = progress.Message,
                Progress = progress,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            updateEvent = new UpdateProgressEvent
            {
                Revision = _revision,
                Phase = _snapshot.Phase,
                Stage = progress.Stage,
                Message = progress.Message,
                Percent = progress.Percent,
                Bytes = progress.Bytes,
                TotalBytes = progress.TotalBytes,
                UpdatedAt = _snapshot.UpdatedAt.ToString("O"),
            };
        }
        if (updateEvent is not null)
        {
            _ = _notifier.NotifyProgressAsync(updateEvent);
        }
    }

    private void SetState(
        string phase,
        string message,
        string? error = null,
        UpdateProgress? progress = null)
    {
        UpdateChangedEvent? changed = null;
        lock (_gate)
        {
            _revision++;
            _snapshot = new UpdateJobSnapshot
            {
                Revision = _revision,
                Phase = phase,
                Message = message,
                Progress = progress,
                Error = error,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            changed = new UpdateChangedEvent
            {
                Revision = _revision,
                Phase = phase,
                UpdatedAt = _snapshot.UpdatedAt.ToString("O"),
            };
        }
        if (changed is not null)
        {
            _ = _notifier.NotifyChangedAsync(changed);
        }
    }

    private void TryNotifyChanged()
    {
        UpdateChangedEvent change;
        lock (_gate)
        {
            change = new UpdateChangedEvent
            {
                Revision = _revision,
                Phase = _snapshot.Phase,
                UpdatedAt = _snapshot.UpdatedAt.ToString("O"),
            };
        }
        _ = _notifier.NotifyChangedAsync(change);
    }

    private static string StatusName(UpdateStatus status) => status switch
    {
        UpdateStatus.UpdateAvailable => "update_available",
        UpdateStatus.UpToDate => "up_to_date",
        UpdateStatus.ManualRequired => "manual_required",
        _ => "no_release",
    };

    private static UpdateCheckView ToView(UpdateCheckResult check) =>
        new()
        {
            Status = StatusName(check.Status),
            CanApply = check.CanApply,
            CurrentVersion = check.CurrentVersion,
            LatestVersion = check.LatestVersion,
            Platform = check.Platform,
            Channel = check.Channel,
            PublishedAt = check.Manifest?.PublishedAt,
            MinSupportedVersion = check.Manifest?.MinSupportedVersion,
            DownloadSize = check.Manifest?.Package?.Size
                ?? check.Manifest?.Files.Sum(file => file.DownloadSize)
                ?? 0,
            FileCount = check.Manifest?.Files.Count ?? 0,
            Notes = check.Manifest?.DisplayNotes,
            NotesMarkdown = check.Manifest?.HasMarkdownNotes == true
                ? check.Manifest.NotesMarkdown
                : null,
            Message = check.Message,
        };
}
