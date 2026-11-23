using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class DoctorSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Structured JSON output")]
    public bool Json { get; set; }

    [CommandOption("--offline")]
    [Description("Skip the online update check")]
    public bool Offline { get; set; }

    [CommandOption("--channel <CHANNEL>")]
    [Description("Channel to check: stable (default) or beta")]
    public string Channel { get; set; } = "stable";

    [CommandOption("--install-dir <DIR>")]
    [Description("Installation directory to verify")]
    public string? InstallDir { get; set; }

    [CommandOption("--state-dir <DIR>")]
    [Description("User state root directory")]
    public string? StateRoot { get; set; }

    [CommandOption("--skill-dir <DIR>")]
    [Description("Agent skill directory")]
    public string? SkillDirectory { get; set; }

    public override ValidationResult Validate()
    {
        var channel = Channel?.Trim().ToLowerInvariant();
        if (channel is not ("stable" or "beta"))
        {
            return ValidationResult.Error(
                "--channel must be stable or beta.");
        }

        if (InstallDir is not null && string.IsNullOrWhiteSpace(InstallDir))
        {
            return ValidationResult.Error("--install-dir cannot be empty.");
        }
        if (StateRoot is not null && string.IsNullOrWhiteSpace(StateRoot))
        {
            return ValidationResult.Error("--state-dir cannot be empty.");
        }
        if (SkillDirectory is not null
            && string.IsNullOrWhiteSpace(SkillDirectory))
        {
            return ValidationResult.Error("--skill-dir cannot be empty.");
        }

        return ValidationResult.Success();
    }
}
