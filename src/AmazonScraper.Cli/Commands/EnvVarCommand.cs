using System.ComponentModel;
using System.Runtime.Versioning;
using AmazonScraper.Cli.EnvSetup;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class EnvVarSettings : CommandSettings
{
    [CommandOption("--install")]
    [Description("Add the amz-cli directory to PATH")]
    public bool Install { get; set; }

    [CommandOption("--uninstall")]
    [Description("Remove the amz-cli directory from PATH")]
    public bool Uninstall { get; set; }

    [CommandOption("--status")]
    [Description("Show user PATH, system PATH, and where amz-cli")]
    public bool Status { get; set; }

    [CommandOption("--system")]
    [Description("Use the system PATH instead of the user PATH (requires admin)")]
    public bool System { get; set; }

    public override ValidationResult Validate()
    {
        var actions = new[] { Install, Uninstall, Status }.Count(value => value);
        if (actions != 1)
        {
            return ValidationResult.Error(
                "Use exactly one of --install, --uninstall, and --status.");
        }

        if (System && Status)
        {
            return ValidationResult.Error(
                "--system only applies to --install or --uninstall; " +
                "--status shows both PATHs.");
        }

        return ValidationResult.Success();
    }
}

[SupportedOSPlatform("windows")]
public sealed class EnvVarCommand : Command<EnvVarSettings>
{
    public override int Execute(
        CommandContext context,
        EnvVarSettings settings,
        CancellationToken cancellationToken)
    {
        var result = settings.Install
            ? WindowsPathInstaller.Install(settings.System)
            : settings.Uninstall
                ? WindowsPathInstaller.Uninstall(settings.System)
                : WindowsPathInstaller.Status();

        if (result.Success)
        {
            Console.Out.WriteLine(result.Message);
        }
        else
        {
            Console.Error.WriteLine(result.Message);
        }

        return result.Success ? 0 : 1;
    }
}
