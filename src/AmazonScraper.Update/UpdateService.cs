using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AmazonScraper.Update;

public sealed class UpdateOptions
{
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public int BackupRetention { get; init; } = 2;
}

public enum UpdateApplyState
{
    Applied,
    Scheduled,
}

public sealed record UpdateApplyOutcome(
    UpdateApplyState State,
    string BackupPath,
    string? HelperPath,
    VerifyResult? Verification,
    string Message);

public sealed class UpdateService
{
    private readonly UpdateApiClient _api;
    private readonly UpdateOptions _options;
    private readonly string _currentVersion;
    private readonly string _platform;

    public UpdateService(
        UpdateApiClient api,
        string currentVersion,
        string? platform = null,
        UpdateOptions? options = null)
    {
        _api = api;
        _currentVersion = currentVersion;
        _platform = platform ?? PlatformInfo.Detect();
        _options = options ?? new UpdateOptions();
    }

    public string Platform => _platform;

    public async Task<UpdateCheckResult> CheckAsync(
        string channel,
        CancellationToken cancellationToken = default)
    {
        if (!PlatformInfo.IsSupported(_platform))
        {
            throw new UpdateException(
                $"Platform is not supported for updates: '{_platform}'.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CheckTimeout);
        var envelope = await _api
            .GetLatestAsync(_platform, channel, timeout.Token)
            .ConfigureAwait(false);
        return UpdateDecision.Evaluate(
            _currentVersion,
            _platform,
            channel,
            envelope);
    }

    public async Task<UpdateApplyOutcome> ApplyAsync(
        UpdateCheckResult check,
        bool requireCurl,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default,
        AutoUpdateContext? auto = null,
        WebUpdateContext? web = null)
    {
        if (!check.CanApply || check.Envelope is null || check.Manifest is null)
        {
            throw new UpdateException(check.Message);
        }
        if (!string.Equals(check.Platform, _platform, StringComparison.Ordinal))
        {
            throw new UpdateException("The release does not match the current platform.");
        }

        UpdatePaths.EnsureCreated();
        using var updateLock = UpdateLock.Acquire();
        var installDir = InstallLocation.Resolve();
        await RecoverIfNeededAsync(check, installDir, progress, cancellationToken)
            .ConfigureAwait(false);

        var refreshed = await CheckAsync(check.Channel, cancellationToken)
            .ConfigureAwait(false);
        if (!refreshed.CanApply
            || refreshed.Envelope is null
            || refreshed.Envelope.ReleaseId != check.Envelope.ReleaseId)
        {
            throw new UpdateException(
                "The release changed or is no longer available. Check again.");
        }
        check = refreshed;
        var manifest = check.Manifest!;

        Preflight(installDir, manifest, progress, auto?.RestartMonitors == true);

        var releaseRoot = Path.Combine(
            UpdatePaths.DownloadsDirectory(),
            check.Envelope!.ReleaseId.ToString("N"));
        var stagingDir = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(installDir))
                ?? throw new UpdateException("Invalid installation directory."),
            ".amz-cli-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingDir);
            UpdatePaths.MarkHidden(stagingDir);
        }
        catch (Exception ex)
        {
            throw new UpdateException(
                $"Unable to create the staging directory: {ex.Message}",
                ex);
        }
        var journal = new UpdateJournal
        {
            Phase = UpdatePhase.Downloading,
            FromVersion = check.CurrentVersion,
            ToVersion = manifest.Version,
            Platform = _platform,
            Channel = check.Channel,
            InstallDir = installDir,
            StagingDir = stagingDir,
            Notes = manifest.Notes,
        };
        UpdateJournalStore.Write(journal);

        BackupResult? backup = null;
        try
        {
            using var downloadTimeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            downloadTimeout.CancelAfter(_options.DownloadTimeout);
            var urls = check.Envelope.DownloadUrls;
            using var downloader = new ReleaseDownloader();
            var download = await downloader.DownloadAsync(
                    check.Envelope.ReleaseId,
                    manifest,
                    urls,
                    stagingDir,
                    releaseRoot,
                    progress,
                    downloadTimeout.Token)
                .ConfigureAwait(false);

            progress?.Report(new UpdateProgress(
                "backup",
                "Creating the installation backup"));
            backup = await InstallBackup
                .CreateAsync(
                    installDir,
                    check.CurrentVersion,
                    _platform,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            journal.Phase = UpdatePhase.BackedUp;
            journal.BackupPath = backup.ArchivePath;
            journal.BackupSha256 = backup.Sha256;
            UpdateJournalStore.Write(journal);

            var plan = BuildPlan(
                check,
                manifest,
                installDir,
                stagingDir,
                backup,
                requireCurl,
                download,
                auto,
                web);
            if (plan.Files.Count == 0)
            {
                throw new UpdateException(
                    "No files differ from the current installation: release cannot be applied.");
            }

            var planPath = Path.Combine(
                UpdatePaths.PlansDirectory(),
                $"update-{check.Envelope.ReleaseId:N}-{Guid.NewGuid():N}.json");
            UpdateApplyPlanStore.Write(planPath, plan);

            journal.Phase = UpdatePhase.Applying;
            journal.StagingDir = stagingDir;
            journal.RollbackDir = plan.RollbackDir;
            UpdateJournalStore.Write(journal);

            var helper = UpdateHelperLauncher.Schedule(plan, planPath);
            journal.HelperPath = helper.HelperPath;
            journal.HelperProcessId = helper.ProcessId;
            UpdateJournalStore.Write(journal);
            return new UpdateApplyOutcome(
                UpdateApplyState.Scheduled,
                backup.ArchivePath,
                helper.HelperPath,
                null,
                "Update started in the background. Check with 'amz-cli --version' " +
                "in a few seconds; the result is available in 'amz-cli update --status'.");
        }
        catch (Exception ex)
        {
            journal.Phase = journal.Phase is UpdatePhase.RolledBack
                ? UpdatePhase.RolledBack
                : UpdatePhase.Failed;
            journal.Error ??= ex.Message;
            UpdateJournalStore.Write(journal);
            Log($"Update failed: {ex}");
            TryDeleteDirectory(stagingDir);
            throw;
        }
    }

    public async Task<UpdateApplyOutcome> RollbackAsync(
        string? backupPath = null,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        UpdatePaths.EnsureCreated();
        using var updateLock = UpdateLock.Acquire();
        var installDir = InstallLocation.Resolve();
        var result = FindLatestBackup(backupPath, installDir)
            ?? throw new UpdateException(
                "No backup is available for this installation.");

        progress?.Report(new UpdateProgress(
            "rollback",
            $"Preparing to restore version {result.Version}"));

        var journal = UpdateJournalStore.Read() ?? new UpdateJournal();
        journal.Phase = UpdatePhase.Applying;
        journal.FromVersion = _currentVersion;
        journal.ToVersion = result.Version;
        journal.Platform = result.Platform;
        journal.BackupPath = result.ArchivePath;
        journal.BackupSha256 = result.Sha256;
        journal.Error = null;
        UpdateJournalStore.Write(journal);

        var helper = UpdateHelperLauncher.ScheduleRollback(
            result,
            installDir);
        journal.HelperPath = helper.HelperPath;
        journal.HelperProcessId = helper.ProcessId;
        UpdateJournalStore.Write(journal);

        await Task.CompletedTask.ConfigureAwait(false);
        return new UpdateApplyOutcome(
            UpdateApplyState.Scheduled,
            result.ArchivePath,
            helper.HelperPath,
            null,
            $"Rollback to version {result.Version} started in the background. " +
            "Check with 'amz-cli --version' in a few seconds.");
    }

    private UpdateApplyPlan BuildPlan(
        UpdateCheckResult check,
        ReleaseManifest manifest,
        string installDir,
        string stagingDir,
        BackupResult backup,
        bool requireCurl,
        DownloadResult download,
        AutoUpdateContext? auto,
        WebUpdateContext? web)
    {
        var stagedByPath = download.Files.ToDictionary(
            file => file.RelativePath,
            StringComparer.Ordinal);
        var files = new List<UpdateApplyFile>();

        foreach (var releaseFile in manifest.Files)
        {
            var target = PathSafety.CombineSafe(installDir, releaseFile.Path);
            if (File.Exists(target)
                && new FileInfo(target).Length == releaseFile.Size
                && string.Equals(
                    FileHash.Sha256Async(target).GetAwaiter().GetResult(),
                    releaseFile.Sha256,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!stagedByPath.TryGetValue(releaseFile.Path, out var staged))
            {
                throw new UpdateException(
                    $"Downloaded file is missing for '{releaseFile.Path}'.");
            }

            files.Add(new UpdateApplyFile
            {
                Path = releaseFile.Path,
                SourcePath = staged.SourcePath,
                Mode = releaseFile.Mode,
                Size = releaseFile.Size,
                Sha256 = releaseFile.Sha256,
            });
        }

        return new UpdateApplyPlan
        {
            InstallDir = installDir,
            Platform = _platform,
            FromVersion = check.CurrentVersion,
            ToVersion = manifest.Version,
            Channel = check.Channel,
            ReleasePublishedAt = manifest.PublishedAt,
            Manifest = check.Envelope!.Manifest,
            Signature = check.Envelope.Signature,
            BackupPath = backup.ArchivePath,
            BackupSha256 = backup.Sha256,
            StagingDir = stagingDir,
            RollbackDir = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(installDir))!,
                ".amz-cli-update-rollback-" + Guid.NewGuid().ToString("N")),
            RequireCurl = requireCurl,
            ParentProcessId = Environment.ProcessId,
            RestartMonitors = auto?.RestartMonitors ?? false,
            DeferForegroundMonitor = auto?.DeferForegroundMonitor ?? true,
            ForceMonitorRestart = web?.ForceMonitorRestart ?? false,
            RestartWeb = web is not null,
            WebResumePath = web?.ResumePath,
            WaitForProcessIds = auto?.WaitForProcessIds.ToList() ?? [],
            AutoTrigger = auto?.Trigger,
            Files = files,
        };
    }

    private void Preflight(
        string installDir,
        ReleaseManifest manifest,
        IProgress<UpdateProgress>? progress,
        bool allowRunningMonitors)
    {
        if (!Directory.Exists(installDir))
        {
            throw new UpdateException($"Installation directory not found: {installDir}");
        }

        var probe = Path.Combine(
            installDir,
            ".amz-cli-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new UpdateException(
                $"The directory {installDir} is not writable: {ex.Message}. " +
                "Run the update manually or move the installation.");
        }

        var downloadBytes = manifest.Package?.Size
            ?? manifest.Files.Sum(file => file.DownloadSize);
        var required = downloadBytes * 2
            + DirectorySize(installDir)
            + 20L * 1024 * 1024;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(installDir));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.AvailableFreeSpace > 0 && drive.AvailableFreeSpace < required)
                {
                    throw new UpdateException(
                        $"Insufficient free space: at least " +
                        $"{required / (1024 * 1024)} MB.");
                }
            }
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception)
        {
            // Filesystem without DriveInfo: the write probe is enough.
        }

        if (OperatingSystem.IsWindows())
        {
            if (!allowRunningMonitors)
            {
                EnsureNoOtherProcess(installDir, "amz-cli");
            }
            EnsureNoOtherProcess(installDir, "amz-notify");
        }
        else
        {
            progress?.Report(new UpdateProgress(
                "preflight",
                "Any active monitors will continue with the old binary until restart"));
        }
    }

    private static void EnsureNoOtherProcess(string installDir, string processName)
    {
        var executable = Path.Combine(
            installDir,
            processName + ".exe");
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }
                var path = process.MainModule?.FileName;
                if (path is not null
                    && string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(executable),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException(
                        $"'{processName}.exe' is running from the same installation. " +
                        "Stop the monitors (amz-cli monitor --stop) and try again.");
                }
            }
            catch (UpdateException)
            {
                throw;
            }
            catch (Exception)
            {
                // Access to another process can be denied; best effort.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task RecoverIfNeededAsync(
        UpdateCheckResult check,
        string installDir,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var journal = UpdateJournalStore.Read();
        if (journal is not null
            && !string.Equals(
                journal.InstallDir,
                installDir,
                StringComparison.Ordinal))
        {
            journal = null;
        }

        UpdatePaths.CleanupStaleSiblingDirectories(
            installDir,
            journal,
            TimeSpan.FromHours(1));

        if (journal is null)
        {
            return;
        }

        if (journal.HelperProcessId is { } helperPid && IsProcessAlive(helperPid))
        {
            throw new UpdateException(
                "An update is already in progress. Wait and try again.");
        }

        if (IsTerminalPhase(journal.Phase))
        {
            TryDeleteDirectory(journal.StagingDir);
            TryDeleteDirectory(journal.RollbackDir);
            UpdatePaths.CleanupStaleSiblingDirectories(
                installDir,
                journal,
                TimeSpan.Zero);
            return;
        }

        if (journal.Phase is not (UpdatePhase.Applying or UpdatePhase.VerifyingInstall))
        {
            return;
        }

        if (string.Equals(journal.ToVersion, check.CurrentVersion, StringComparison.Ordinal))
        {
            var verify = await UpdateVerifier.VerifyAsync(
                    InstallLocation.ExecutablePath(
                        journal.InstallDir ?? InstallLocation.Resolve(),
                        journal.Platform ?? _platform),
                    journal.ToVersion!,
                    journal.Platform ?? _platform,
                    requireCurl: false,
                    cancellationToken)
                .ConfigureAwait(false);
            if (verify.Success)
            {
                journal.Phase = UpdatePhase.Completed;
                journal.Error = null;
                UpdateJournalStore.Write(journal);
                progress?.Report(new UpdateProgress(
                    "recovery",
                    "Previous update completed and verified"));
                return;
            }
        }

        if (string.Equals(journal.FromVersion, check.CurrentVersion, StringComparison.Ordinal))
        {
            journal.Phase = UpdatePhase.RolledBack;
            journal.Error = "Update interrupted before replacement.";
            UpdateJournalStore.Write(journal);
            return;
        }

        throw new UpdateException(
            "An interrupted update is present. " +
            $"Available backup: {journal.BackupPath ?? "none"}. " +
            "Run 'amz-cli update --rollback' or reinstall manually.");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static BackupResult? FindLatestBackup(
        string? explicitPath,
        string installDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var metadataPath = explicitPath + ".json";
            try
            {
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(
                    File.ReadAllText(metadataPath),
                    UpdateJson.Options);
                if (metadata is not null
                    && string.Equals(
                        metadata.InstallDir,
                        installDir,
                        StringComparison.Ordinal))
                {
                    return new BackupResult(
                        explicitPath,
                        metadataPath,
                        metadata.ArchiveSha256,
                        metadata.ArchiveSize,
                        metadata.Version,
                        metadata.Platform,
                        metadata.CreatedAt,
                        metadata.InstallDir);
                }
            }
            catch (Exception)
            {
                // Fall through to the normal lookup.
            }
        }

        var directory = UpdatePaths.BackupsDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var archive in Directory
                     .EnumerateFiles(directory, "amz-cli-*.tar.gz")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var metadataPath = archive + ".json";
            try
            {
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(
                    File.ReadAllText(metadataPath),
                    UpdateJson.Options);
                if (metadata is not null
                    && string.Equals(
                        metadata.InstallDir,
                        installDir,
                        StringComparison.Ordinal))
                {
                    return new BackupResult(
                        archive,
                        metadataPath,
                        metadata.ArchiveSha256,
                        metadata.ArchiveSize,
                        metadata.Version,
                        metadata.Platform,
                        metadata.CreatedAt,
                        metadata.InstallDir);
                }
            }
            catch (Exception)
            {
                // Try the next backup.
            }
        }
        return null;
    }

    private static long DirectorySize(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path =>
                {
                    try
                    {
                        return new FileInfo(path).Length;
                    }
                    catch (Exception)
                    {
                        return 0L;
                    }
                });
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool IsTerminalPhase(string phase) =>
        phase is UpdatePhase.Completed
            or UpdatePhase.RolledBack
            or UpdatePhase.Failed
            or UpdatePhase.DeferredForeground;

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void Log(string message)
    {
        try
        {
            UpdatePaths.EnsureCreated();
            File.AppendAllText(
                UpdatePaths.LogPath(),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch (Exception)
        {
            // Logging is best effort.
        }
    }
}

internal sealed record HelperLaunch(string HelperPath, int ProcessId);

internal static class UpdateHelperLauncher
{
    public static HelperLaunch Schedule(UpdateApplyPlan plan, string planPath)
    {
        var helperPath = CopyHelper();
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = plan.InstallDir,
        };
        startInfo.ArgumentList.Add("__update-apply");
        startInfo.ArgumentList.Add("--plan");
        startInfo.ArgumentList.Add(planPath);
        return Start(helperPath, startInfo);
    }

    public static HelperLaunch ScheduleRollback(BackupResult backup, string installDir)
    {
        var helperPath = CopyHelper();
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = installDir,
        };
        startInfo.ArgumentList.Add("__update-rollback");
        startInfo.ArgumentList.Add("--backup");
        startInfo.ArgumentList.Add(backup.ArchivePath);
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(backup.Version);
        startInfo.ArgumentList.Add("--platform");
        startInfo.ArgumentList.Add(backup.Platform);
        startInfo.ArgumentList.Add("--install-dir");
        startInfo.ArgumentList.Add(installDir);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        return Start(helperPath, startInfo);
    }

    private static string CopyHelper()
    {
        var processPath = Environment.ProcessPath
            ?? throw new UpdateException("Unable to determine the current executable.");
        Directory.CreateDirectory(UpdatePaths.HelpersDirectory());
        CleanupOldHelpers();
        var helperName = OperatingSystem.IsWindows()
            ? "amz-cli-update-helper-" + Guid.NewGuid().ToString("N") + ".exe"
            : "amz-cli-update-helper-" + Guid.NewGuid().ToString("N");
        var helperPath = Path.Combine(UpdatePaths.HelpersDirectory(), helperName);
        File.Copy(processPath, helperPath, overwrite: false);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    helperPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception)
            {
                // The source binary is already executable on normal installs.
            }
        }
        return helperPath;
    }

    private static void CleanupOldHelpers()
    {
        try
        {
            var directory = UpdatePaths.HelpersDirectory();
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var file in Directory.EnumerateFiles(
                         directory,
                         "amz-cli-update-helper-*"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // A running helper cannot be removed; try next time.
                }
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static HelperLaunch Start(string helperPath, ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new UpdateException("Failed to start the update helper.");
        var processId = process.Id;
        process.Dispose();
        return new HelperLaunch(helperPath, processId);
    }
}
