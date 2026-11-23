using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class PathsSettings : CommandSettings
{
    [CommandOption("--all")]
    [Description("Also include known but missing paths and empty directories")]
    public bool All { get; set; }

    [CommandOption("--install")]
    [Description("Expand the installation files")]
    public bool Install { get; set; }

    [CommandOption("--save-path <DIR>")]
    [Description("Add monitors found in a directory, even if unregistered")]
    public string[] SavePaths { get; set; } = [];

    [CommandOption("--json")]
    [Description("Structured JSON output")]
    public bool Json { get; set; }

    [CommandOption("--ascii")]
    [Description("Tree with ASCII characters")]
    public bool Ascii { get; set; }

    [CommandOption("--verbose")]
    [Description("Also show the modification date")]
    public bool Verbose { get; set; }

    public override ValidationResult Validate()
    {
        if (SavePaths.Any(string.IsNullOrWhiteSpace))
        {
            return ValidationResult.Error(
                "--save-path cannot be empty.");
        }

        return ValidationResult.Success();
    }
}
