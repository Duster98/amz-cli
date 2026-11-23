using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class SkillSettings : CommandSettings
{
    [CommandOption("--install")]
    [Description("Install or update the skill in the agent directory")]
    public bool Install { get; set; }

    [CommandOption("--check")]
    [Description("Check whether the installed skill is up to date")]
    public bool Check { get; set; }

    [CommandOption("--dir <DIR>")]
    [Description("Skill directory (default ~/.agents/skills/amz-cli)")]
    public string? Directory { get; set; }

    [CommandOption("--force")]
    [Description("Also replace a file that is not recognized as an AMZ CLI skill")]
    public bool Force { get; set; }

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    public override ValidationResult Validate()
    {
        if (Install && Check)
        {
            return ValidationResult.Error(
                "--install and --check cannot be used together.");
        }

        if (Force && !Install)
        {
            return ValidationResult.Error("--force requires --install.");
        }

        if (Directory is not null && string.IsNullOrWhiteSpace(Directory))
        {
            return ValidationResult.Error("--dir cannot be empty.");
        }

        return ValidationResult.Success();
    }
}
