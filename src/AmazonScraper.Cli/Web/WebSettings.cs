using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Web;

public sealed class WebSettings : CommandSettings
{
    [CommandOption("--port <PORT>")]
    [Description("Fixed port, 0 = automatic (default: automatic)")]
    public int Port { get; set; }

    [CommandOption("--remote")]
    [Description("Listen on all network interfaces so the web UI is reachable on the LAN")]
    public bool Remote { get; set; }

    [CommandOption("--open")]
    [Description("Open the browser after startup")]
    public bool Open { get; set; }

    [CommandOption("--no-open")]
    [Description("Do not open the browser")]
    public bool NoOpen { get; set; }

    [CommandOption("--desktop")]
    [Description("App mode: hides the console on Windows and closes after inactivity")]
    public bool Desktop { get; set; }

    [CommandOption("--dev")]
    [Description("Development mode: allows the Vite origin on localhost:5173")]
    public bool Dev { get; set; }

    [CommandOption("--web-dir <DIR>")]
    [Description("Vite build directory to serve instead of the embedded assets")]
    public string? WebDirectory { get; set; }

    [CommandOption("--log-path <FILE>")]
    [Description("Log file (default: user state/amz-monitor/web/web.log)")]
    public string? LogPath { get; set; }

    [CommandOption("--idle-minutes <MINUTES>")]
    [Description("Minutes of inactivity before closing in desktop mode (0 = never)")]
    public int IdleMinutes { get; set; } = 30;

    [CommandOption("--stop")]
    [Description("Stop the running web interface")]
    public bool Stop { get; set; }

    [CommandOption("--status")]
    [Description("Show the web interface status")]
    public bool Status { get; set; }

    [CommandOption("--quiet")]
    [Description("Reduced output")]
    public bool Quiet { get; set; }

    public override ValidationResult Validate()
    {
        if (Port is < 0 or > 65535)
        {
            return ValidationResult.Error(
                "--port must be between 0 and 65535 (0 = automatic).");
        }
        if (Open && NoOpen)
        {
            return ValidationResult.Error(
                "Use --open or --no-open, not both.");
        }
        if (Stop && Status)
        {
            return ValidationResult.Error(
                "Use --stop or --status, not both.");
        }
        if (IdleMinutes is < 0 or > 1440)
        {
            return ValidationResult.Error(
                "--idle-minutes must be between 0 and 1440.");
        }
        if (WebDirectory is not null
            && !Directory.Exists(WebDirectory))
        {
            return ValidationResult.Error(
                $"The web directory does not exist: {WebDirectory}");
        }

        return ValidationResult.Success();
    }
}
