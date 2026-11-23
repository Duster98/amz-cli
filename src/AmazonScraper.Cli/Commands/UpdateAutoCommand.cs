using System.ComponentModel;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Update;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class UpdateAutoSettings : CommandSettings
{
    [CommandOption("--trigger <TRIGGER>")]
    [Description("Check trigger: startup or monitor")]
    [DefaultValue("startup")]
    public string Trigger { get; set; } = "startup";

    [CommandOption("--parent-pid <PID>")]
    public int? ParentPid { get; set; }

    [CommandOption("--channel <CHANNEL>")]
    public string? Channel { get; set; }

    [CommandOption("--quiet")]
    public bool Quiet { get; set; }

    public override ValidationResult Validate()
    {
        if (Trigger is not ("startup" or "monitor"))
        {
            return ValidationResult.Error(
                "--trigger must be startup or monitor.");
        }
        if (ParentPid is < 0)
        {
            return ValidationResult.Error("--parent-pid is invalid.");
        }
        if (Channel is not null and not ("stable" or "beta"))
        {
            return ValidationResult.Error(
                "--channel must be stable or beta.");
        }
        return ValidationResult.Success();
    }
}

/// <summary>
/// Hidden coordinator used by the startup hook and by the monitor timer.
/// It prepares an update and schedules the helper; it never waits for the
/// helper to finish and never writes to the interactive console.
/// </summary>
public sealed class UpdateAutoCommand : AsyncCommand<UpdateAutoSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UpdateAutoSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            UpdatePaths.EnsureCreated();
            var config = AutoUpdateConfig.Load();
            if (!config.Enabled)
            {
                return 0;
            }
            if (settings.Channel is { } channel)
            {
                config.Channel = channel;
            }
            config.Validate();

            using var autoLock = TryAcquireAutoLock();
            if (autoLock is null)
            {
                return 0;
            }
            if (!AutoUpdateState.IsDue(config))
            {
                return 0;
            }

            using var api = new UpdateApiClient(
                currentVersion: VersionInfo.Informational);
            var service = new UpdateService(
                api,
                VersionInfo.Informational,
                options: new UpdateOptions
                {
                    CheckTimeout = TimeSpan.FromSeconds(20),
                });

            UpdateCheckResult check;
            try
            {
                check = await service
                    .CheckAsync(config.Channel, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"Auto-update check failed: {ex.Message}");
                AutoUpdateState.MarkFailed(settings.Trigger, ex.Message);
                return 1;
            }

            AutoUpdateState.MarkChecked(check, settings.Trigger);
            if (!check.CanApply)
            {
                Log($"Auto-update: {check.Status} ({check.Message})");
                return 0;
            }
            if (!config.AutoApply)
            {
                Log(
                    $"Auto-update available ({check.CurrentVersion} -> " +
                    $"{check.LatestVersion}) but auto_apply=false.");
                return 0;
            }

            var installDir = InstallLocation.Resolve();
            var inspection = MonitorRestartCoordinator.Inspect(installDir);
            if (inspection.Items.Count > 0 && !config.RestartMonitors)
            {
                Log(
                    "Auto-update deferred: monitors are active and " +
                    "restart_monitors=false.");
                return 0;
            }
            if (inspection.HasForeground || inspection.HasUnrestartable)
            {
                var journal = new UpdateJournal
                {
                    Phase = UpdatePhase.DeferredForeground,
                    FromVersion = check.CurrentVersion,
                    ToVersion = check.LatestVersion,
                    Platform = service.Platform,
                    Channel = check.Channel,
                    InstallDir = installDir,
                    AutoTrigger = settings.Trigger,
                    Notes = check.Manifest?.Notes,
                    DeferredReason = inspection.BlockReason,
                    Error = inspection.BlockReason,
                    MonitorsPlanned = inspection.Items
                        .Select(item => $"{item.Info.Id} (PID {item.Info.Pid}, " +
                            $"{item.Info.Mode ?? "foreground"})")
                        .ToList(),
                };
                UpdateJournalStore.Write(journal);
                Log(
                    "Auto-update deferred: " +
                    (inspection.BlockReason ?? "monitor cannot be restarted."));
                await UpdateNotifier
                    .TryNotifyAsync(journal, installDir, cancellationToken)
                    .ConfigureAwait(false);
                return 0;
            }

            if (settings.Trigger == "startup"
                && settings.ParentPid is { } parentPid
                && parentPid != Environment.ProcessId)
            {
                try
                {
                    await UpdateVerifier.WaitForProcessExitAsync(
                            parentPid,
                            TimeSpan.FromHours(12),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"Auto-update: waiting for the CLI failed: {ex.Message}");
                    return 1;
                }
            }

            var runtime = RuntimeDiagnostics.Run(VersionInfo.Informational);
            var requireCurl = runtime.CurlAvailable
                || OperatingSystem.IsWindows();
            var outcome = await service
                .ApplyAsync(
                    check,
                    requireCurl,
                    progress: null,
                    cancellationToken,
                    new AutoUpdateContext(
                        settings.Trigger,
                        [],
                        config.RestartMonitors,
                        config.DeferForegroundMonitor))
                .ConfigureAwait(false);
            Log($"Auto-update {outcome.State}: {outcome.Message}");
            return 0;
        }
        catch (UpdateException ex)
        {
            Log($"Auto-update: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Log("Auto-update cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Log($"Auto-update: {ex.Message}");
            return 1;
        }
    }

    private static UpdateLock? TryAcquireAutoLock()
    {
        try
        {
            return UpdateLock.Acquire(UpdatePaths.AutoLockPath());
        }
        catch (UpdateException)
        {
            return null;
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

public sealed class UpdateNotifyCommand : AsyncCommand<UpdateNotifySettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UpdateNotifySettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<MonitorNotificationPayload>(
                await File.ReadAllTextAsync(settings.Payload!, cancellationToken)
                    .ConfigureAwait(false),
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            if (payload is null)
            {
                return 1;
            }

            await UpdateNotifier
                .SendAsync(payload, settings.InstallDir, cancellationToken)
                .ConfigureAwait(false);
            TryDelete(settings.Payload);
            return 0;
        }
        catch (Exception ex)
        {
            TryDelete(settings.Payload);
            Log($"Update notification: {ex.Message}");
            return 1;
        }
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

    private static void Log(string message)
    {
        try
        {
            AmazonScraper.Update.UpdatePaths.EnsureCreated();
            File.AppendAllText(
                AmazonScraper.Update.UpdatePaths.LogPath(),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
