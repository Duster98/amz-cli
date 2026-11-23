using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class UpdateSettings : CommandSettings
{
    [CommandOption("--check")]
    [Description("Check the available version without changing anything")]
    public bool Check { get; set; }

    [CommandOption("--yes|-y")]
    [Description("Confirm the update without prompting")]
    public bool Yes { get; set; }

    [CommandOption("--channel <CHANNEL>")]
    [Description("Release channel: stable (default) or beta")]
    [DefaultValue("stable")]
    public string Channel { get; set; } = "stable";

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--dry-run")]
    [Description("Show the plan without downloading or modifying files")]
    public bool DryRun { get; set; }

    [CommandOption("--status")]
    [Description("Show the last updater result and available backups")]
    public bool Status { get; set; }

    [CommandOption("--rollback")]
    [Description("Restore the most recent available backup")]
    public bool Rollback { get; set; }

    [CommandOption("--delete-backups")]
    [Description("Delete rollback backups and reset the updater state")]
    public bool DeleteBackups { get; set; }

    [CommandOption("--enable-auto")]
    [Description("Enable automatic update checks")]
    public bool EnableAuto { get; set; }

    [CommandOption("--disable-auto")]
    [Description("Disable automatic update checks")]
    public bool DisableAuto { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("Version check timeout in seconds")]
    [DefaultValue(30.0)]
    public double Timeout { get; set; } = 30.0;

    [CommandOption("--width <COLUMNS>")]
    [Description("Console width for tables (default 200)")]
    [DefaultValue(200)]
    public int Width { get; set; } = 200;

    public override ValidationResult Validate()
    {
        var modes = (Check ? 1 : 0) + (Status ? 1 : 0) + (Rollback ? 1 : 0)
            + (EnableAuto ? 1 : 0) + (DisableAuto ? 1 : 0)
            + (DeleteBackups ? 1 : 0);
        if (modes > 1)
        {
            return ValidationResult.Error(
                "Use only one of --check, --status, --rollback, " +
                "--delete-backups, --enable-auto, and --disable-auto.");
        }
        if (EnableAuto && DisableAuto)
        {
            return ValidationResult.Error(
                "Use --enable-auto or --disable-auto, not both.");
        }
        if (Yes && (Check || Status || EnableAuto || DisableAuto))
        {
            return ValidationResult.Error(
                "--yes is only valid for an update or rollback.");
        }
        if (DryRun && (Status || Rollback))
        {
            return ValidationResult.Error(
                "--dry-run is not compatible with --status or --rollback.");
        }
        if (Channel is not ("stable" or "beta"))
        {
            return ValidationResult.Error("--channel must be stable or beta.");
        }
        if (Timeout is < 1 or > 300)
        {
            return ValidationResult.Error("--timeout must be between 1 and 300 seconds.");
        }
        if (Width < 40)
        {
            return ValidationResult.Error("--width must be at least 40.");
        }
        return ValidationResult.Success();
    }
}

public sealed class UpdateVerifySettings : CommandSettings
{
    [CommandOption("--expected-version <VERSION>")]
    public string? ExpectedVersion { get; set; }

    [CommandOption("--expected-platform <PLATFORM>")]
    public string? ExpectedPlatform { get; set; }

    [CommandOption("--require-curl")]
    public bool RequireCurl { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ExpectedVersion))
        {
            return ValidationResult.Error("--expected-version is required.");
        }
        if (string.IsNullOrWhiteSpace(ExpectedPlatform))
        {
            return ValidationResult.Error("--expected-platform is required.");
        }
        return ValidationResult.Success();
    }
}

public sealed class UpdateApplySettings : CommandSettings
{
    [CommandOption("--plan <FILE>")]
    [Description("Path to the update plan prepared by the CLI")]
    public string? Plan { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Plan))
        {
            return ValidationResult.Error("--plan is required.");
        }
        return ValidationResult.Success();
    }
}

public sealed class UpdateRollbackSettings : CommandSettings
{
    [CommandOption("--backup <FILE>")]
    public string? Backup { get; set; }

    [CommandOption("--version <VERSION>")]
    public string? Version { get; set; }

    [CommandOption("--platform <PLATFORM>")]
    public string? Platform { get; set; }

    [CommandOption("--install-dir <DIR>")]
    public string? InstallDir { get; set; }

    [CommandOption("--parent-pid <PID>")]
    public int ParentProcessId { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Backup))
        {
            return ValidationResult.Error("--backup is required.");
        }
        if (string.IsNullOrWhiteSpace(Version))
        {
            return ValidationResult.Error("--version is required.");
        }
        if (string.IsNullOrWhiteSpace(Platform))
        {
            return ValidationResult.Error("--platform is required.");
        }
        if (string.IsNullOrWhiteSpace(InstallDir))
        {
            return ValidationResult.Error("--install-dir is required.");
        }
        return ValidationResult.Success();
    }
}

public sealed class UpdateNotifySettings : CommandSettings
{
    [CommandOption("--payload <FILE>")]
    public string? Payload { get; set; }

    [CommandOption("--install-dir <DIR>")]
    public string? InstallDir { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Payload))
        {
            return ValidationResult.Error("--payload is required.");
        }
        return ValidationResult.Success();
    }
}
