using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public class HistoryOutputSettings : CommandSettings
{
    [CommandOption("--width <COLUMNS>")]
    [Description("Console width for tables (default 200)")]
    [DefaultValue(200)]
    public int Width { get; set; } = 200;

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    public override ValidationResult Validate() =>
        Width < 40
            ? ValidationResult.Error("--width must be at least 40.")
            : ValidationResult.Success();
}

public sealed class HistoryListSettings : HistoryOutputSettings
{
    [CommandOption("--kind <KIND>")]
    [Description("Filter by kind: search or product")]
    public string? Kind { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Filter by source: web, cli, or agent")]
    public string? Source { get; set; }

    [CommandOption("--status <STATUS>")]
    [Description("Filter by status: running, succeeded, failed, cancelled, interrupted")]
    public string? Status { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (Kind is not null && Kind.Trim().ToLowerInvariant() is not ("search" or "product"))
        {
            return ValidationResult.Error("--kind must be search or product.");
        }
        if (Source is not null && Source.Trim().ToLowerInvariant() is not ("web" or "cli" or "agent"))
        {
            return ValidationResult.Error("--source must be web, cli, or agent.");
        }
        if (Status is not null
            && Status.Trim().ToLowerInvariant() is not
                ("running" or "succeeded" or "failed" or "cancelled" or "interrupted"))
        {
            return ValidationResult.Error(
                "--status must be running, succeeded, failed, cancelled, or interrupted.");
        }
        return ValidationResult.Success();
    }
}

public sealed class HistoryRemoveSettings : HistoryOutputSettings
{
    [CommandArgument(0, "<IDS>")]
    [Description("History entry ids to remove (h_...)")]
    public string[] Ids { get; set; } = [];

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        return Ids.Length == 0
            ? ValidationResult.Error("Specify at least one history id.")
            : ValidationResult.Success();
    }
}

public sealed class HistoryClearSettings : HistoryOutputSettings
{
    [CommandOption("--yes")]
    [Description("Confirm deletion of the entire history")]
    public bool Yes { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        return Yes
            ? ValidationResult.Success()
            : ValidationResult.Error(
                "History clear is destructive: add --yes to confirm.");
    }
}
