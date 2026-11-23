using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Cli.Skill;
using AmazonScraper.Cli.Web;
using AmazonScraper.Update;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace AmazonScraper.Cli.Commands;

public sealed class UpdateCommand : AsyncCommand<UpdateSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UpdateSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            if (settings.EnableAuto || settings.DisableAuto)
            {
                var config = AutoUpdateConfig.Load();
                config.Enabled = settings.EnableAuto;
                config.Save();
                Console.WriteLine(
                    config.Enabled
                        ? "Auto-update enabled."
                        : "Auto-update disabled.");
                return 0;
            }

            if (settings.Status)
            {
                var installDir = InstallLocation.Resolve();
                var journal = UpdateJournalStore.Read();
                if (journal is not null
                    && !string.Equals(
                        journal.InstallDir,
                        installDir,
                        StringComparison.Ordinal))
                {
                    journal = null;
                }
                UpdateRenderer.RenderStatus(
                    settings,
                    journal,
                    InstallBackup.List(installDir: installDir),
                    AutoUpdateConfig.Load(),
                    AutoUpdateState.Read());
                return 0;
            }

            if (settings.DeleteBackups)
            {
                return DeleteBackups(settings);
            }

            if (settings.Rollback)
            {
                return await RollbackAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            }

            using var api = new UpdateApiClient(
                currentVersion: VersionInfo.Informational);
            var service = new UpdateService(
                api,
                VersionInfo.Informational,
                options: new UpdateOptions
                {
                    CheckTimeout = TimeSpan.FromSeconds(settings.Timeout),
                });

            var check = await service
                .CheckAsync(settings.Channel, cancellationToken)
                .ConfigureAwait(false);

            if (settings.Check || settings.DryRun || !check.CanApply)
            {
                UpdateRenderer.RenderCheck(check, settings, settings.DryRun);
                return 0;
            }

            if (settings.Json && !settings.Yes)
            {
                CliConsole.Error("With --json, use --yes to apply the update.");
                return 1;
            }

            if (!settings.Yes)
            {
                if (Console.IsInputRedirected)
                {
                    CliConsole.Error(
                        "Non-interactive input: use --yes to apply the update.");
                    return 1;
                }

                var confirm = AnsiConsole.Confirm(
                    $"Update amz-cli from {check.CurrentVersion} to {check.LatestVersion}?",
                    defaultValue: false);
                if (!confirm)
                {
                    Console.WriteLine("Update cancelled.");
                    return 0;
                }
            }

            var selfTest = RuntimeDiagnostics.Run(VersionInfo.Informational);
            var progress = new SynchronousProgress<UpdateProgress>(
                item => UpdateRenderer.Progress(item, settings.Json));
            var outcome = await service
                .ApplyAsync(
                    check,
                    requireCurl: selfTest.CurlAvailable
                        || OperatingSystem.IsWindows(),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            UpdateRenderer.RenderOutcome(outcome, settings);
            return 0;
        }
        catch (OperationCanceledException)
        {
            CliConsole.Error("Operation cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is UpdateException or UpdateApiException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CliConsole.Error($"Update error: {ex.Message}");
            return 1;
        }
    }

    private static int DeleteBackups(UpdateSettings settings)
    {
        try
        {
            using var updateLock = UpdateLock.Acquire();
            var installDir = InstallLocation.Resolve();
            var journal = UpdateJournalStore.Read();
            if (journal is not null
                && !string.Equals(
                    journal.InstallDir,
                    installDir,
                    StringComparison.Ordinal))
            {
                journal = null;
            }

            if (journal is not null)
            {
                var activePhase = journal.Phase is
                    UpdatePhase.Applying or UpdatePhase.VerifyingInstall
                    or UpdatePhase.WaitingForCli
                    or UpdatePhase.StoppingMonitors
                    or UpdatePhase.RestartingMonitors;
                var helperAlive = journal.HelperProcessId is { } helperPid
                    && IsProcessAlive(helperPid);
                var recent = journal.UpdatedAt
                    > DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
                if (activePhase && (helperAlive || recent))
                {
                    CliConsole.Error(
                        "Update in progress: try again when it finishes.");
                    return 1;
                }
            }

            var backups = InstallBackup.List(installDir: installDir);
            var staging = UpdatePaths.ListStaleSiblingDirectories(
                installDir,
                null,
                TimeSpan.Zero);
            var totalBytes = backups.Sum(backup => backup.Size);

            if (settings.DryRun)
            {
                UpdateRenderer.RenderDeleteBackupsPreview(
                    backups,
                    staging,
                    settings);
                return 0;
            }

            var hasWork = backups.Count > 0 || staging.Count > 0 || journal is not null;

            if (settings.Json && !settings.Yes && hasWork)
            {
                CliConsole.Error(
                    "With --json, use --yes to delete backups.");
                return 1;
            }

            if (!settings.Yes && hasWork)
            {
                if (Console.IsInputRedirected)
                {
                    CliConsole.Error(
                        "Non-interactive input: use --yes to delete backups.");
                    return 1;
                }

                if (!AnsiConsole.Confirm(
                        $"Delete {backups.Count} backups " +
                        $"({UpdateRenderer.FormatSize(totalBytes)}) and reset " +
                        "the updater state?",
                        defaultValue: false))
                {
                    Console.WriteLine("Deletion cancelled.");
                    return 0;
                }
            }

            var result = InstallBackup.DeleteAll(installDir: installDir);
            UpdateJournalStore.Delete();
            var removedDirectories = UpdatePaths.CleanupStaleSiblingDirectories(
                installDir,
                null,
                TimeSpan.Zero);
            TryDeleteDirectory(UpdatePaths.DownloadsDirectory());
            TryDeleteDirectory(UpdatePaths.PlansDirectory());
            UpdateRenderer.RenderDeleteBackups(
                result,
                removedDirectories,
                settings);
            return result.Failed == 0 ? 0 : 1;
        }
        catch (UpdateException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
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

    private static void TryDeleteDirectory(string path)
    {
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

    private static async Task<int> RollbackAsync(
        UpdateSettings settings,
        CancellationToken cancellationToken)
    {
        var backups = InstallBackup.List(
            installDir: InstallLocation.Resolve());
        var backup = backups.FirstOrDefault();
        if (backup is null)
        {
            CliConsole.Error("No backup available for rollback.");
            return 1;
        }

        if (!settings.Yes)
        {
            if (Console.IsInputRedirected)
            {
                CliConsole.Error("Non-interactive input: use --yes for rollback.");
                return 1;
            }
            if (!AnsiConsole.Confirm(
                    $"Restore version {backup.Version} from the backup " +
                    $"dated {backup.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm}?",
                    defaultValue: false))
            {
                Console.WriteLine("Rollback cancelled.");
                return 0;
            }
        }

        using var api = new UpdateApiClient(
            currentVersion: VersionInfo.Informational);
        var service = new UpdateService(api, VersionInfo.Informational);
        var outcome = await service
            .RollbackAsync(backup.ArchivePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        UpdateRenderer.RenderOutcome(outcome, settings);
        return 0;
    }
}

public sealed class UpdateVerifyCommand : AsyncCommand<UpdateVerifySettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        UpdateVerifySettings settings,
        CancellationToken cancellationToken)
    {
        var version = VersionInfo.Informational;
        var platform = PlatformInfo.Detect();
        var diagnostics = RuntimeDiagnostics.Run(version);
        var errors = new List<string>();

        if (!string.Equals(version, settings.ExpectedVersion, StringComparison.Ordinal))
        {
            errors.Add($"version {version}, expected {settings.ExpectedVersion}");
        }
        if (!string.Equals(platform, settings.ExpectedPlatform, StringComparison.Ordinal))
        {
            errors.Add($"platform {platform}, expected {settings.ExpectedPlatform}");
        }
        if (string.IsNullOrWhiteSpace(SkillDocument.Markdown))
        {
            errors.Add("embedded skill not loaded");
        }
        if (MarketplaceRegistry.AvailableIds.Count == 0)
        {
            errors.Add("marketplaces not loaded");
        }
        if (settings.RequireCurl && !diagnostics.CurlAvailable)
        {
            errors.Add("curl-impersonate unavailable after the update");
        }
        if (settings.RequireCurl
            && OperatingSystem.IsWindows()
            && diagnostics.CurlFiles.Any(file => !file.Exists))
        {
            errors.Add(
                "bundled curl-impersonate files missing after the update: " +
                string.Join(
                    ", ",
                    diagnostics.CurlFiles
                        .Where(file => !file.Exists)
                        .Select(file => file.Name)));
        }

        var ok = errors.Count == 0;
        var payload = new
        {
            ok,
            version,
            platform,
            curl_available = diagnostics.CurlAvailable,
            errors,
        };
        Console.Out.WriteLine(
            System.Text.Json.JsonSerializer.Serialize(
                payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        return Task.FromResult(ok ? 0 : 1);
    }
}

public sealed class UpdateApplyCommand : AsyncCommand<UpdateApplySettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UpdateApplySettings settings,
        CancellationToken cancellationToken)
    {
        UpdateApplyPlan? startupPlan = null;
        try
        {
            UpdatePaths.EnsureCreated();
            var plan = UpdateApplyPlanStore.Read(settings.Plan!);
            startupPlan = plan;
            await WaitForPlanProcessesAsync(plan, cancellationToken)
                .ConfigureAwait(false);

            using var updateLock = UpdateLock.Acquire();
            var journal = UpdateJournalStore.Read() ?? new UpdateJournal
            {
                Phase = UpdatePhase.Applying,
                FromVersion = plan.FromVersion,
                ToVersion = plan.ToVersion,
                Platform = plan.Platform,
                Channel = plan.Channel,
                InstallDir = plan.InstallDir,
                BackupPath = plan.BackupPath,
                BackupSha256 = plan.BackupSha256,
                StagingDir = plan.StagingDir,
                RollbackDir = plan.RollbackDir,
                AutoTrigger = plan.AutoTrigger,
            };
            journal.AutoTrigger = plan.AutoTrigger;

            int Finish(int code)
            {
                RestartWebInterface(plan, journal);
                return code;
            }

            MonitorRestartInspection? inspection = null;
            MonitorRestartResult? stopped = null;
            if (plan.RestartMonitors)
            {
                inspection = MonitorRestartCoordinator.Inspect(plan.InstallDir);
                journal.MonitorsPlanned = inspection.Items
                    .Select(item => Describe(item.Info))
                    .ToList();
                if (inspection.HasForeground || inspection.HasUnrestartable)
                {
                    if (!plan.ForceMonitorRestart)
                    {
                        journal.Phase = UpdatePhase.DeferredForeground;
                        journal.DeferredReason = inspection.BlockReason;
                        journal.Error = inspection.BlockReason;
                        UpdateJournalStore.Write(journal);
                        Log(
                            "Update deferred: " +
                            (inspection.BlockReason ?? "monitor cannot be restarted."));
                        await UpdateNotifier
                            .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                            .ConfigureAwait(false);
                        CleanupDeferred(plan);
                        return Finish(0);
                    }
                    Log(
                        "Forced update: " +
                        (inspection.BlockReason ?? "monitor cannot be restarted."));
                }

                // Validate paths/hashes before stopping any monitor.
                UpdateInstaller.Validate(plan);
                await UpdateInstaller
                    .ValidateStagedAsync(plan, cancellationToken)
                    .ConfigureAwait(false);

                journal.Phase = UpdatePhase.StoppingMonitors;
                UpdateJournalStore.Write(journal);
                stopped = await MonitorRestartCoordinator
                    .StopAllAsync(inspection, cancellationToken)
                    .ConfigureAwait(false);
                journal.MonitorsStopped = stopped.Stopped.ToList();
                if (stopped.Errors.Count > 0)
                {
                    journal.Phase = UpdatePhase.Failed;
                    journal.RestartErrors = stopped.Errors.ToList();
                    journal.Error =
                        "Failed to stop monitors: " +
                        string.Join("; ", stopped.Errors);
                    UpdateJournalStore.Write(journal);
                    await RestartAfterFailureAsync(
                            inspection,
                            stopped,
                            journal,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await UpdateNotifier
                        .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                        .ConfigureAwait(false);
                    return Finish(1);
                }

                var otherProcesses = await UpdateProcessGuard
                    .WaitForOtherProcessesAsync(
                        plan.InstallDir,
                        TimeSpan.FromSeconds(30),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (otherProcesses.Count > 0)
                {
                    journal.Phase = UpdatePhase.Failed;
                    journal.Error =
                        "Other amz-cli instances are still running: " +
                        string.Join(", ", otherProcesses);
                    UpdateJournalStore.Write(journal);
                    await RestartAfterFailureAsync(
                            inspection,
                            stopped,
                            journal,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await UpdateNotifier
                        .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                        .ConfigureAwait(false);
                    return Finish(1);
                }
                UpdateJournalStore.Write(journal);
            }

            try
            {
                await UpdateInstaller
                    .ApplyAsync(plan, journal, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (inspection is not null && stopped is not null)
                {
                    var restart = await MonitorRestartCoordinator
                        .RestartAsync(inspection, stopped.Stopped, cancellationToken)
                        .ConfigureAwait(false);
                    journal.RestartErrors = restart.Errors.ToList();
                    if (restart.Errors.Count > 0)
                    {
                        journal.Phase = UpdatePhase.Failed;
                        journal.Error =
                            $"{ex.Message} Monitor restart failed: " +
                            string.Join("; ", restart.Errors);
                    }
                    else if (journal.Phase is not (
                        UpdatePhase.RolledBack or UpdatePhase.Failed))
                    {
                        journal.Phase = UpdatePhase.Failed;
                        journal.Error ??= ex.Message;
                    }
                    UpdateJournalStore.Write(journal);
                    Log($"Monitors restarted after error: " +
                        $"{restart.Restarted.Count}, errors: {restart.Errors.Count}");
                }
                await UpdateNotifier
                    .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                    .ConfigureAwait(false);
                throw;
            }

            if (inspection is not null && stopped is not null)
            {
                journal.Phase = UpdatePhase.RestartingMonitors;
                UpdateJournalStore.Write(journal);
                var restart = await MonitorRestartCoordinator
                    .RestartAsync(inspection, stopped.Stopped, cancellationToken)
                    .ConfigureAwait(false);
                journal.MonitorsRestarted = restart.Restarted.ToList();
                journal.RestartErrors = restart.Errors
                    .Concat(restart.Skipped)
                    .ToList();
                if (restart.Errors.Count > 0)
                {
                    journal.Phase = UpdatePhase.Failed;
                    journal.Error =
                        "Update applied, but monitor restart failed: " +
                        string.Join("; ", restart.Errors);
                    UpdateJournalStore.Write(journal);
                    Log(journal.Error);
                    await UpdateNotifier
                        .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                        .ConfigureAwait(false);
                    return Finish(1);
                }

                journal.Phase = UpdatePhase.Completed;
                journal.Error = null;
                UpdateJournalStore.Write(journal);
                await UpdateNotifier
                    .TryNotifyAsync(journal, plan.InstallDir, cancellationToken)
                    .ConfigureAwait(false);
            }

            InstallBackup.Prune(UpdatePaths.BackupsDirectory(), 2, plan.InstallDir);
            TryDeleteDirectory(UpdatePaths.DownloadsDirectory());
            TryDelete(settings.Plan);
            return Finish(0);
        }
        catch (Exception ex)
        {
            RestartWebInterface(startupPlan, UpdateJournalStore.Read());
            Log($"Update helper: {ex}");
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    private static void RestartWebInterface(
        UpdateApplyPlan? plan,
        UpdateJournal? journal)
    {
        if (plan is null || !plan.RestartWeb)
        {
            return;
        }

        if (journal is not null)
        {
            journal.WebRestartAttemptedAt = DateTimeOffset.UtcNow;
            journal.WebRestartError = null;
            UpdateJournalStore.Write(journal);
        }
        var launch = WebResumeLauncher.TryStart(plan);
        if (journal is not null)
        {
            journal.WebPid = launch.ProcessId;
            journal.WebRestartError = launch.Error;
            UpdateJournalStore.Write(journal);
        }
        if (launch.Error is not null)
        {
            Log($"Web restart failed: {launch.Error}");
        }
        else if (launch.ProcessId is { } pid)
        {
            Log($"Web interface restart scheduled (PID {pid}).");
        }
    }

    private static async Task WaitForPlanProcessesAsync(
        UpdateApplyPlan plan,
        CancellationToken cancellationToken)
    {
        var waitFor = new HashSet<int>(plan.WaitForProcessIds)
        {
            plan.ParentProcessId,
        };
        waitFor.Remove(Environment.ProcessId);
        foreach (var processId in waitFor)
        {
            await UpdateVerifier.WaitForProcessExitAsync(
                    processId,
                    TimeSpan.FromSeconds(30),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RestartAfterFailureAsync(
        MonitorRestartInspection inspection,
        MonitorRestartResult stopped,
        UpdateJournal journal,
        CancellationToken cancellationToken)
    {
        if (stopped.Stopped.Count == 0)
        {
            return;
        }

        var restart = await MonitorRestartCoordinator
            .RestartAsync(inspection, stopped.Stopped, cancellationToken)
            .ConfigureAwait(false);
        journal.MonitorsRestarted = restart.Restarted.ToList();
        journal.RestartErrors = restart.Errors
            .Concat(restart.Skipped)
            .ToList();
        if (restart.Errors.Count > 0)
        {
            journal.Error =
                (journal.Error ?? "Failed to stop monitors.") +
                " Restart failed: " + string.Join("; ", restart.Errors);
        }
        UpdateJournalStore.Write(journal);
    }

    private static string Describe(MonitorPidInfo info) =>
        $"{info.Id} (PID {info.Pid}, {info.Mode ?? "foreground"})";

    private static void CleanupDeferred(UpdateApplyPlan plan)
    {
        TryDeleteDirectory(plan.StagingDir);
        TryDeleteDirectory(UpdatePaths.DownloadsDirectory());
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
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
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}

public sealed class UpdateRollbackCommand : AsyncCommand<UpdateRollbackSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UpdateRollbackSettings settings,
        CancellationToken cancellationToken)
    {
        UpdateJournal? journal = null;
        try
        {
            if (settings.ParentProcessId > 0
                && settings.ParentProcessId != Environment.ProcessId)
            {
                await UpdateVerifier.WaitForProcessExitAsync(
                        settings.ParentProcessId,
                        TimeSpan.FromSeconds(30),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            UpdatePaths.EnsureCreated();
            using var updateLock = UpdateLock.Acquire();
            journal = UpdateJournalStore.Read() ?? new UpdateJournal();
            journal.Phase = UpdatePhase.Applying;
            journal.ToVersion = settings.Version;
            journal.Platform = settings.Platform;
            journal.InstallDir = settings.InstallDir;
            journal.BackupPath = settings.Backup;
            journal.Error = null;
            UpdateJournalStore.Write(journal);

            await InstallBackup.RestoreAsync(
                    settings.Backup!,
                    settings.InstallDir!,
                    settings.Version!,
                    settings.Platform!,
                    cancellationToken)
                .ConfigureAwait(false);

            InstallManifestUpdater.SyncCurrent(
                settings.InstallDir!,
                settings.Version!,
                settings.Platform!);

            journal.Phase = UpdatePhase.RolledBack;
            journal.Error = null;
            UpdateJournalStore.Write(journal);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                if (journal is not null)
                {
                    journal.Phase = UpdatePhase.Failed;
                    journal.Error = ex.Message;
                    UpdateJournalStore.Write(journal);
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
            CliConsole.Error(ex.Message);
            return 1;
        }
    }
}

internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _action;

    public SynchronousProgress(Action<T> action)
    {
        _action = action;
    }

    public void Report(T value) => _action(value);
}
