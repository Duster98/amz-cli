using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Update;
using Spectre.Console;

namespace AmazonScraper.Cli.Commands;

public static class UpdateRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void RenderCheck(
        UpdateCheckResult check,
        UpdateSettings settings,
        bool dryRun)
    {
        var showNotes = check.Status is UpdateStatus.UpdateAvailable
            or UpdateStatus.ManualRequired;
        if (settings.Json)
        {
            WriteJson(new UpdateCheckJson
            {
                CurrentVersion = check.CurrentVersion,
                LatestVersion = check.LatestVersion,
                UpdateAvailable = check.CanApply,
                Status = StatusName(check.Status),
                Platform = check.Platform,
                Channel = check.Channel,
                PublishedAt = check.Manifest?.PublishedAt,
                MinSupportedVersion = check.Manifest?.MinSupportedVersion,
                DownloadSize = check.Manifest?.Package?.Size
                    ?? check.Manifest?.Files.Sum(file => file.DownloadSize)
                    ?? 0,
                FileCount = check.Manifest?.Files.Count ?? 0,
                Notes = showNotes ? check.Manifest?.Notes : null,
                NotesMarkdown = showNotes ? check.Manifest?.NotesMarkdown : null,
                Message = check.Message,
                DryRun = dryRun,
                Files = dryRun
                    ? check.Manifest?.Files
                        .Select(file => new UpdateFileJson
                        {
                            Path = file.Path,
                            Size = file.Size,
                            DownloadSize = file.DownloadSize,
                        })
                        .ToList()
                    : null,
            });
            return;
        }

        var console = CliConsole.Stdout(settings.Width);
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn(new TableColumn(new Text("Item", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Value", new Style(Color.Cyan))));
        table.AddRow("Current version", check.CurrentVersion);
        table.AddRow("Platform", check.Platform);
        table.AddRow("Channel", check.Channel);
        table.AddRow("Latest version", check.LatestVersion ?? "-");
        table.AddRow("Status", StatusText(check.Status));
        if (check.Manifest is not null)
        {
            table.AddRow(
                "Published",
                check.Manifest.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"));
            table.AddRow(
                "Download",
                $"{FormatSize(check.Manifest.Files.Sum(file => file.DownloadSize))} " +
                $"({check.Manifest.Files.Count} file)");
            if (!string.IsNullOrWhiteSpace(check.Manifest.MinSupportedVersion))
            {
                table.AddRow("Minimum version", check.Manifest.MinSupportedVersion);
            }
        }
        console.Write(table);

        if (showNotes
            && check.Manifest is not null
            && !string.IsNullOrWhiteSpace(check.Manifest.DisplayNotes))
        {
            var notes = check.Manifest.DisplayNotes!;
            var markup = check.Manifest.HasMarkdownNotes
                ? MarkdownConsoleRenderer.ToMarkup(notes)
                : Markup.Escape(notes);
            console.Write(
                new Panel(new Markup(markup))
                {
                    Header = new PanelHeader("Release notes"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0, 1, 0),
                });
        }

        if (dryRun && check.Manifest is not null)
        {
            var files = new Table
            {
                Border = TableBorder.Simple,
                BorderStyle = new Style(Color.Grey),
            };
            files.AddColumn("File");
            files.AddColumn(new TableColumn("Size").RightAligned());
            foreach (var file in check.Manifest.Files)
            {
                files.AddRow(
                    Markup.Escape(file.Path),
                    FormatSize(file.DownloadSize));
            }
            console.Write(files);
        }

        if (!string.IsNullOrWhiteSpace(check.Message))
        {
            console.MarkupLine(
                $"[grey]{Markup.Escape(check.Message)}[/]");
        }
    }

    public static void RenderOutcome(UpdateApplyOutcome outcome, UpdateSettings settings)
    {
        if (settings.Json)
        {
            WriteJson(new UpdateOutcomeJson
            {
                State = outcome.State == UpdateApplyState.Applied ? "applied" : "scheduled",
                Message = outcome.Message,
                BackupPath = outcome.BackupPath,
                HelperPath = outcome.HelperPath,
                VerificationOk = outcome.Verification?.Success,
                VerificationDetail = outcome.Verification?.Detail,
            });
            return;
        }

        var console = CliConsole.Stdout(settings.Width);
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Green),
        };
        table.AddColumn(new TableColumn(new Text("Item", new Style(Color.Yellow))));
        table.AddColumn(new TableColumn(new Text("Value", new Style(Color.Cyan))));
        table.AddRow("Result", outcome.Message);
        table.AddRow("Backup", Markup.Escape(outcome.BackupPath));
        if (outcome.Verification is not null)
        {
            table.AddRow(
                "Verification",
                outcome.Verification.Success
                    ? "OK"
                    : Markup.Escape(outcome.Verification.Detail));
        }
        if (outcome.HelperPath is not null)
        {
            table.AddRow("Helper", Markup.Escape(outcome.HelperPath));
        }
        console.Write(table);
    }

    public static void RenderDeleteBackupsPreview(
        IReadOnlyList<BackupSummary> backups,
        IReadOnlyList<string> stagingDirectories,
        UpdateSettings settings)
    {
        if (settings.Json)
        {
            WriteJson(new
            {
                state = "dry_run",
                backups_removed = backups.Count,
                bytes_freed = backups.Sum(backup => backup.Size),
                staging_removed = stagingDirectories.Count,
                backups = backups
                    .Select(backup => new UpdateBackupJson
                    {
                        Version = backup.Version,
                        Platform = backup.Platform,
                        CreatedAt = backup.CreatedAt,
                        Size = backup.Size,
                        Path = backup.ArchivePath,
                        InstallDir = backup.InstallDir,
                    })
                    .ToList(),
                staging_directories = stagingDirectories,
            });
            return;
        }

        var console = CliConsole.Stdout(settings.Width);
        console.MarkupLine(
            $"[yellow]Backups to delete:[/] {backups.Count} " +
            $"({FormatSize(backups.Sum(backup => backup.Size))})");
        foreach (var backup in backups)
        {
            console.MarkupLine($"  {Markup.Escape(backup.ArchivePath)}");
        }

        if (stagingDirectories.Count > 0)
        {
            console.MarkupLine(
                $"[yellow]Temporary directories:[/] {stagingDirectories.Count}");
            foreach (var directory in stagingDirectories)
            {
                console.MarkupLine($"  {Markup.Escape(directory)}");
            }
        }

        console.MarkupLine("[grey]Dry-run: no files changed.[/]");
    }

    public static void RenderDeleteBackups(
        BackupDeleteResult result,
        int stagingRemoved,
        UpdateSettings settings)
    {
        if (settings.Json)
        {
            WriteJson(new
            {
                state = "deleted",
                backups_removed = result.Removed,
                bytes_freed = result.Bytes,
                failed = result.Failed,
                staging_removed = stagingRemoved,
                journal_reset = true,
            });
            return;
        }

        var console = CliConsole.Stdout(settings.Width);
        console.MarkupLine(
            $"[green]Backups deleted:[/] {result.Removed} " +
            $"({FormatSize(result.Bytes)})");
        if (result.Failed > 0)
        {
            console.MarkupLine(
                $"[red]Backups not deleted:[/] {result.Failed}");
        }
        console.MarkupLine("Update state reset.");
        if (stagingRemoved > 0)
        {
            console.MarkupLine(
                $"Temporary directories removed: {stagingRemoved}");
        }
    }

    public static void RenderStatus(
        UpdateSettings settings,
        UpdateJournal? journal,
        IReadOnlyList<BackupSummary> backups,
        AutoUpdateConfig? autoConfig = null,
        AutoUpdateState? autoState = null)
    {
        if (settings.Json)
        {
            WriteJson(new UpdateStatusJson
            {
                LastUpdate = journal,
                AutoUpdate = autoConfig is null
                    ? null
                    : new UpdateAutoJson
                    {
                        Enabled = autoConfig.Enabled,
                        Channel = autoConfig.Channel,
                        IntervalSeconds = autoConfig.IntervalSeconds,
                        LastCheck = autoState?.LastCheck,
                        LatestVersion = autoState?.LatestVersion,
                        LastStatus = autoState?.LastStatus,
                        LastTrigger = autoState?.LastTrigger,
                    },
                Backups = backups
                    .Select(backup => new UpdateBackupJson
                    {
                        Version = backup.Version,
                        Platform = backup.Platform,
                        CreatedAt = backup.CreatedAt,
                        Size = backup.Size,
                        Path = backup.ArchivePath,
                        InstallDir = backup.InstallDir,
                    })
                    .ToList(),
            });
            return;
        }

        var console = CliConsole.Stdout(settings.Width);
        if (journal is null)
        {
            console.MarkupLine("[grey]No update recorded.[/]");
        }
        else
        {
            var table = new Table
            {
                Border = TableBorder.HeavyHead,
                BorderStyle = new Style(Color.Cyan),
            };
            table.AddColumn(new TableColumn(new Text("Item", new Style(Color.Yellow))));
            table.AddColumn(new TableColumn(new Text("Value", new Style(Color.Cyan))));
            table.AddRow("Status", journal.Phase);
            table.AddRow("From", journal.FromVersion ?? "-");
            table.AddRow("To", journal.ToVersion ?? "-");
            table.AddRow("Platform", journal.Platform ?? "-");
            table.AddRow("Channel", journal.Channel ?? "-");
            table.AddRow("Updated", journal.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"));
            if (!string.IsNullOrWhiteSpace(journal.AutoTrigger))
            {
                table.AddRow("Source", journal.AutoTrigger);
            }
            if (!string.IsNullOrWhiteSpace(journal.BackupPath))
            {
                table.AddRow("Backup", Markup.Escape(journal.BackupPath));
            }
            if (journal.MonitorsPlanned.Count > 0)
            {
                table.AddRow(
                    "Monitors",
                    $"{journal.MonitorsRestarted.Count}/{journal.MonitorsPlanned.Count} restarted");
            }
            if (!string.IsNullOrWhiteSpace(journal.DeferredReason))
            {
                table.AddRow("Deferred", Markup.Escape(journal.DeferredReason));
            }
            if (!string.IsNullOrWhiteSpace(journal.Error))
            {
                table.AddRow("Error", Markup.Escape(journal.Error));
            }
            console.Write(table);
        }

        if (autoConfig is not null)
        {
            var table = new Table
            {
                Border = TableBorder.Simple,
                BorderStyle = new Style(Color.Grey),
            };
            table.AddColumn("Item");
            table.AddColumn("Value");
            table.AddRow("Auto-update", autoConfig.Enabled ? "enabled" : "disabled");
            table.AddRow("Auto channel", autoConfig.Channel);
            table.AddRow("Interval", $"{autoConfig.IntervalSeconds}s");
            if (autoState?.LastCheck is { } lastCheck)
            {
                table.AddRow(
                    "Last check",
                    lastCheck.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"));
            }
            if (!string.IsNullOrWhiteSpace(autoState?.LatestVersion))
            {
                table.AddRow("Latest version", autoState.LatestVersion);
            }
            if (!string.IsNullOrWhiteSpace(autoState?.LastStatus))
            {
                table.AddRow("Last result", Markup.Escape(autoState.LastStatus));
            }
            console.Write(table);
        }

        if (backups.Count > 0)
        {
            var table = new Table
            {
                Border = TableBorder.Simple,
                BorderStyle = new Style(Color.Grey),
            };
            table.AddColumn("Version");
            table.AddColumn("Platform");
            table.AddColumn("Created");
            table.AddColumn(new TableColumn("Dimensione").RightAligned());
            table.AddColumn("Path");
            foreach (var backup in backups)
            {
                table.AddRow(
                    backup.Version,
                    backup.Platform,
                    backup.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm"),
                    FormatSize(backup.Size),
                    Markup.Escape(backup.ArchivePath));
            }
            console.Write(table);
        }
        else
        {
            console.MarkupLine("[grey]No backups available.[/]");
        }
    }

    public static void RenderRollback(
        BackupSummary backup,
        UpdateSettings settings)
    {
        if (settings.Json)
        {
            WriteJson(new
            {
                state = "rolled_back",
                version = backup.Version,
                platform = backup.Platform,
                backup_path = backup.ArchivePath,
            });
            return;
        }

        CliConsole.Stdout(settings.Width).MarkupLine(
            $"[green]Rollback completed:[/] version {backup.Version}, " +
            $"backup {Markup.Escape(backup.ArchivePath)}");
    }

    public static void Progress(UpdateProgress progress, bool json)
    {
        var message = progress.Message;
        if (progress.Percent is { } percent)
        {
            message += $" ({percent}%)";
        }

        if (json)
        {
            Console.Error.WriteLine(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    private static string StatusName(UpdateStatus status) => status switch
    {
        UpdateStatus.NoRelease => "no_release",
        UpdateStatus.UpToDate => "up_to_date",
        UpdateStatus.UpdateAvailable => "update_available",
        UpdateStatus.ManualRequired => "manual_required",
        _ => "unknown",
    };

    private static string StatusText(UpdateStatus status) => status switch
    {
        UpdateStatus.NoRelease => "no release",
        UpdateStatus.UpToDate => "up to date",
        UpdateStatus.UpdateAvailable => "update available",
        UpdateStatus.ManualRequired => "manual update required",
        _ => "unknown",
    };

    internal static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
            : bytes >= 1024L * 1024
                ? $"{bytes / (1024.0 * 1024):0.0} MB"
                : bytes >= 1024
                    ? $"{bytes / 1024.0:0.0} KB"
                    : $"{bytes} B";

    private static void WriteJson<T>(T value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}

public sealed class UpdateCheckJson
{
    [JsonPropertyName("current_version")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; init; }

    [JsonPropertyName("update_available")]
    public bool UpdateAvailable { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("min_supported_version")]
    public string? MinSupportedVersion { get; init; }

    [JsonPropertyName("download_size")]
    public long DownloadSize { get; init; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("notes_markdown")]
    public string? NotesMarkdown { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; init; }

    [JsonPropertyName("files")]
    public IReadOnlyList<UpdateFileJson>? Files { get; init; }
}

public sealed class UpdateFileJson
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("download_size")]
    public long DownloadSize { get; init; }
}

public sealed class UpdateOutcomeJson
{
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("backup_path")]
    public string? BackupPath { get; init; }

    [JsonPropertyName("helper_path")]
    public string? HelperPath { get; init; }

    [JsonPropertyName("verification_ok")]
    public bool? VerificationOk { get; init; }

    [JsonPropertyName("verification_detail")]
    public string? VerificationDetail { get; init; }
}

public sealed class UpdateStatusJson
{
    [JsonPropertyName("last_update")]
    public UpdateJournal? LastUpdate { get; init; }

    [JsonPropertyName("auto_update")]
    public UpdateAutoJson? AutoUpdate { get; init; }

    [JsonPropertyName("backups")]
    public IReadOnlyList<UpdateBackupJson> Backups { get; init; } = [];
}

public sealed class UpdateAutoJson
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("interval_seconds")]
    public int IntervalSeconds { get; init; }

    [JsonPropertyName("last_check")]
    public DateTimeOffset? LastCheck { get; init; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; init; }

    [JsonPropertyName("last_status")]
    public string? LastStatus { get; init; }

    [JsonPropertyName("last_trigger")]
    public string? LastTrigger { get; init; }
}

public sealed class UpdateBackupJson
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("install_dir")]
    public string InstallDir { get; init; } = string.Empty;
}
