using System.ComponentModel;
using AmazonScraper.Core.Monitoring;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class MonitorSettings : CommonSettings
{
    [CommandArgument(0, "[ASINS]")]
    [Description("ASIN, it:ASIN, de:ASIN, or Amazon URL to monitor")]
    public string[] Asins { get; set; } = [];

    [CommandOption("--config <FILE>")]
    [Description("JSON configuration with items, groups, and thresholds")]
    public string? Config { get; set; }

    [CommandOption("--asin-file <FILE>")]
    [Description("ASIN file: plain text, or CSV/TSV with a domain,asin header")]
    public string? AsinFile { get; set; }

    [CommandOption("--library-product <PRODUCT>")]
    [Description("Monitor a saved product by ASIN, domain:ASIN, or URL")]
    public string? LibraryProduct { get; set; }

    [CommandOption("--library-group <GROUP>")]
    [Description("Monitor a saved group by id or unique name")]
    public string? LibraryGroup { get; set; }

    [CommandOption("--binding <ID>")]
    [Description("Run a saved monitor binding")]
    public string? Binding { get; set; }

    [CommandOption("--list-bindings")]
    [Description("List saved monitor bindings")]
    public bool ListBindings { get; set; }

    [CommandOption("--remove-binding <ID>")]
    [Description("Remove a saved monitor binding")]
    public string? RemoveBinding { get; set; }

    [CommandOption("--timer <SECONDS>")]
    [Description("Seconds between the start of one cycle and the next (min 10, default 3600)")]
    public int? Timer { get; set; }

    [CommandOption("--request-delay <SECONDS>")]
    [Description("Seconds between product requests (min 6, default 10)")]
    public int? RequestDelay { get; set; }

    [CommandOption("--jitter <SECONDS>")]
    [Description("Additional random delay before the next cycle (default 0)")]
    public int Jitter { get; set; }

    [CommandOption("--schedule <MODE>")]
    [Description("Scheduling mode: adaptive (default: timer)")]
    public string? Schedule { get; set; }

    [CommandOption("--window <SECONDS>")]
    [Description("Adaptive window duration (default 86400)")]
    public int? Window { get; set; }

    [CommandOption("--max-requests <N>")]
    [Description("Maximum product requests per adaptive window")]
    public int? MaxRequests { get; set; }

    [CommandOption("--min-gap <SECONDS>")]
    [Description("Minimum gap between two runs of the same unit (default 600)")]
    public int? MinGap { get; set; }

    [CommandOption("--max-gap <SECONDS>")]
    [Description("Maximum randomized gap (default 2 x min-gap)")]
    public int? MaxGap { get; set; }

    [CommandOption("--save-format <FORMAT>")]
    [Description("json/jsonl (JSON Lines) or csv; default json")]
    public string? SaveFormat { get; set; }

    [CommandOption("--save-path <DIR>")]
    [Description("Destination directory (default .)")]
    public string? SavePath { get; set; }

    [CommandOption("--save-file <BASE>")]
    [Description("Base file name, without extension (default amz-monitor)")]
    public string? SaveFile { get; set; }

    [CommandOption("--layout <LAYOUT>")]
    [Description("auto, combined, per-group, or single (default auto)")]
    public string? Layout { get; set; }

    [CommandOption("--once")]
    [Description("Run a single cycle and exit (for cron/systemd)")]
    public bool Once { get; set; }

    [CommandOption("--cycles <N>")]
    [Description("Number of cycles before exiting (default infinite)")]
    public int? Cycles { get; set; }

    [CommandOption("--quiet")]
    [Description("Show only errors, alerts, and the summary")]
    public bool Quiet { get; set; }

    [CommandOption("--html <FILE>")]
    [Description("Read a local HTML page, single ASIN, single cycle (debug/test)")]
    public string? HtmlFile { get; set; }

    [CommandOption("--cap <DOMAIN:POSTAL>")]
    [Description("Postal code per domain, repeatable; without a domain only with a single domain")]
    public string[] Caps { get; set; } = [];

    [CommandOption("--country <DOMAIN:ISO>")]
    [Description("Delivery country per domain, repeatable")]
    public string[] Countries { get; set; } = [];

    [CommandOption("--domain-cookie <DOMAIN:HEADER>")]
    [Description("Cookie per domain, repeatable; requires the domain prefix")]
    public string[] DomainCookies { get; set; } = [];

    [CommandOption("--max-price <AMOUNT>")]
    [Description("Item price threshold, in the marketplace currency")]
    public decimal? MaxPrice { get; set; }

    [CommandOption("--total-max <AMOUNT>")]
    [Description("Group total threshold, in the target group currency")]
    public decimal? TotalMax { get; set; }

    [CommandOption("--currency <CODE>")]
    [Description("Target currency for a multi-currency group")]
    public string? Currency { get; set; }

    [CommandOption("--fx <SOURCE=RATE>")]
    [Description("FX multiplier to the target currency, repeatable (e.g. GBP=1.17)")]
    public string[] Fx { get; set; } = [];

    [CommandOption("--allow-incomplete")]
    [Description("Allow group totals when some products have no price")]
    public bool AllowIncomplete { get; set; }

    [CommandOption("--drop-percent <PCT>")]
    [Description("Alert when the price drops this percentage below the baseline (0-100)")]
    public decimal? DropPercent { get; set; }

    [CommandOption("--rise-percent <PCT>")]
    [Description("Alert when the price rises this percentage above the baseline (0-100)")]
    public decimal? RisePercent { get; set; }

    [CommandOption("--baseline <BASELINE>")]
    [Description("Price baseline: original, first_seen, max_seen, last_seen")]
    public string? Baseline { get; set; }

    [CommandOption("--cooldown <SECONDS>")]
    [Description("Minimum seconds between repeated alerts (default 21600)")]
    public int? Cooldown { get; set; }

    [CommandOption("--blocked-cooldown <SECONDS>")]
    [Description("Delay after a block or domain setup error (default 900)")]
    public int BlockedCooldown { get; set; } = 900;

    [CommandOption("--max-consecutive-domain-failures <N>")]
    [Description("Disable a domain after N consecutive blocks/setup failures (default 3)")]
    public int MaxConsecutiveDomainFailures { get; set; } = 3;

    [CommandOption("--exit-on-alert")]
    [Description("Exit with code 3 if at least one rule triggered an alert")]
    public bool ExitOnAlert { get; set; }

    [CommandOption("--dry-run")]
    [Description("Validate configuration, targets, groups, thresholds, and files without network access")]
    public bool DryRun { get; set; }

    [CommandOption("--detach")]
    [Description("Start the monitor in the background and return immediately")]
    public bool Detach { get; set; }

    [CommandOption("--startup")]
    [Description("Install auto-start at login and start the monitor")]
    public bool Startup { get; set; }

    [CommandOption("--startup-remove")]
    [Description("Remove the monitor auto-start")]
    public bool StartupRemove { get; set; }

    [CommandOption("--status")]
    [Description("Show the background monitor status")]
    public bool Status { get; set; }

    [CommandOption("--list")]
    [Description("List known active monitors")]
    public bool List { get; set; }

    [CommandOption("--all")]
    [Description("With --list, also include stale entries")]
    public bool All { get; set; }

    [CommandOption("--prune")]
    [Description("With --list, remove stale entries")]
    public bool Prune { get; set; }

    [CommandOption("--verify")]
    [Description("With --list, verify that startup tasks/units actually exist")]
    public bool Verify { get; set; }

    [CommandOption("--stats")]
    [Description("Show a local summary of monitored prices")]
    public bool Stats { get; set; }

    [CommandOption("--restart")]
    [Description("Restart the monitors of the current installation")]
    public bool Restart { get; set; }

    [CommandOption("--json")]
    [Description("JSON output for --list, --list-bindings, --stats, --restart, or --remove-binding")]
    public bool Json { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output for --list, --list-bindings, --stats, --restart, or --remove-binding")]
    public bool TkOpt { get; set; }

    [CommandOption("--verbose")]
    [Description("With --list, show full paths")]
    public bool Verbose { get; set; }

    [CommandOption("--stop")]
    [Description("Stop the background monitor")]
    public bool Stop { get; set; }

    [CommandOption("--notify")]
    [Description("Enable desktop notifications for alerts")]
    public bool Notify { get; set; }

    [CommandOption("--notify-on <EVENTS>")]
    [Description("Events to notify: alerts,errors,blocks,fatal,all (default alerts)")]
    public string? NotifyOn { get; set; }

    [CommandOption("--notify-cooldown <SECONDS>")]
    [Description("Cooldown for error/block notifications (default 3600)")]
    public int? NotifyCooldown { get; set; }

    [CommandOption("--notify-test")]
    [Description("Send a test notification and exit, without network access")]
    public bool NotifyTest { get; set; }

    [CommandOption("--no-auto-update")]
    [Description("Disable the hourly update check for this monitor")]
    public bool NoAutoUpdate { get; set; }

    [CommandOption("--log-path <FILE>")]
    [Description("Additional log file (default: stdout/stderr)")]
    public string? LogPath { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }

        if (SaveHtml is not null)
        {
            return ValidationResult.Error(
                "--save-html is not supported by monitor; use --html for offline tests.");
        }

        var libraryTargets = new[]
        {
            Binding,
            LibraryProduct,
            LibraryGroup,
        }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (libraryTargets > 1)
        {
            return ValidationResult.Error(
                "Use only one of --binding, --library-product, or --library-group.");
        }
        if (Binding is not null
            && (MaxPrice is not null
                || TotalMax is not null
                || DropPercent is not null
                || RisePercent is not null
                || Baseline is not null
                || Cooldown is not null
                || Currency is not null
                || Fx.Length > 0
                || AllowIncomplete))
        {
            return ValidationResult.Error(
                "Thresholds are stored in the binding. Use --library-product/" +
                "--library-group to create or update them.");
        }
        if (ListBindings && !string.IsNullOrWhiteSpace(RemoveBinding))
        {
            return ValidationResult.Error(
                "Use --list-bindings or --remove-binding, not both.");
        }
        if (ListBindings || !string.IsNullOrWhiteSpace(RemoveBinding))
        {
            if (Asins.Length > 0
                || AsinFile is not null
                || Config is not null
                || LibraryProduct is not null
                || LibraryGroup is not null
                || Binding is not null
                || HtmlFile is not null
                || Startup
                || StartupRemove
                || Status
                || Stop
                || List
                || Stats
                || Restart
                || Detach
                || DryRun
                || Once
                || Cycles is not null)
            {
                return ValidationResult.Error(
                    "--list-bindings/--remove-binding are control commands and " +
                    "cannot be combined with targets or monitor actions.");
            }
        }
        if (libraryTargets > 0)
        {
            if (Asins.Length > 0 || AsinFile is not null || Config is not null)
            {
                return ValidationResult.Error(
                    "Library monitoring cannot be combined with ASINs, " +
                    "--asin-file, or --config.");
            }
            if (HtmlFile is not null)
            {
                return ValidationResult.Error(
                    "--html is not supported with library monitoring.");
            }
            if (StartupRemove)
            {
                return ValidationResult.Error(
                    "--startup-remove does not accept library targets.");
            }
            if (LibraryProduct is not null
                && (TotalMax is not null
                    || Currency is not null
                    || Fx.Length > 0
                    || AllowIncomplete))
            {
                return ValidationResult.Error(
                    "Group total options require --library-group.");
            }
            if (Schedule is not null
                || Window is not null
                || MaxRequests is not null
                || MinGap is not null
                || MaxGap is not null)
            {
                return ValidationResult.Error(
                    "Adaptive scheduling is not supported with library " +
                    "monitoring; use --timer and --once/--cycles if needed.");
            }
        }
        else if (TotalMax is not null
                 || Currency is not null
                 || Fx.Length > 0
                 || AllowIncomplete)
        {
            return ValidationResult.Error(
                "--total-max/--currency/--fx/--allow-incomplete require " +
                "--library-group or a group binding.");
        }
        if (TotalMax is <= 0)
        {
            return ValidationResult.Error(
                "--total-max must be greater than zero.");
        }
        if (Currency is not null
            && (Currency.Trim().Length != 3
                || !Currency.Trim().All(char.IsAsciiLetter)))
        {
            return ValidationResult.Error(
                "--currency must be a three-letter ISO currency code.");
        }
        foreach (var fx in Fx)
        {
            var separator = fx.IndexOf('=');
            if (separator <= 0
                || separator == fx.Length - 1
                || !decimal.TryParse(
                    fx[(separator + 1)..],
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rate)
                || rate <= 0)
            {
                return ValidationResult.Error(
                    $"Invalid --fx value '{fx}'. Use CUR=RATE, for example GBP=1.17.");
            }
        }

        if (Stats && Restart)
        {
            return ValidationResult.Error(
                "--stats and --restart cannot be used together.");
        }
        if (Stats || Restart)
        {
            if (List
                || Status
                || Stop
                || Startup
                || StartupRemove
                || NotifyTest
                || Detach
                || DryRun
                || Once
                || Cycles is not null
                || HtmlFile is not null
                || Asins.Length > 0
                || AsinFile is not null
                || All
                || Prune
                || TkOpt
                || Verbose
                || ListBindings
                || !string.IsNullOrWhiteSpace(RemoveBinding)
                || libraryTargets > 0)
            {
                return ValidationResult.Error(
                    (Stats ? "--stats" : "--restart") +
                    " is not compatible with --list, --status, --stop, " +
                    "--startup, --startup-remove, --notify-test, --detach, " +
                    "--dry-run, --once, --cycles, --html, target, --all, " +
                    "--prune, --tk-opt, or --verbose.");
            }
        }
        if (Verify && !List)
        {
            return ValidationResult.Error("--verify requires --list.");
        }

        if (NotifyOn is not null)
        {
            try
            {
                _ = MonitorNotificationEventsParser.Parse(NotifyOn);
            }
            catch (MonitorException ex)
            {
                return ValidationResult.Error(ex.Message);
            }
        }
        if (NotifyCooldown is { } notifyCooldown
            && notifyCooldown is < 60 or > 604_800)
        {
            return ValidationResult.Error(
                "--notify-cooldown must be between 60 and 604800 seconds.");
        }
        if (NotifyTest)
        {
            if (Asins.Length > 0
                || AsinFile is not null
                || Config is not null
                || HtmlFile is not null
                || Detach
                || Startup
                || StartupRemove
                || Status
                || Stop
                || Once
                || Cycles is not null
                || DryRun
                || Schedule is not null
                || Window is not null
                || MaxRequests is not null
                || MinGap is not null
                || MaxGap is not null
                || ListBindings
                || !string.IsNullOrWhiteSpace(RemoveBinding)
                || libraryTargets > 0)
            {
                return ValidationResult.Error(
                    "--notify-test is not compatible with targets, config, " +
                    "--html, --detach, --startup, --startup-remove, --status, " +
                    "--stop, --once, --cycles, or --dry-run.");
            }
        }

        if (Startup && StartupRemove)
        {
            return ValidationResult.Error(
                "Use --startup or --startup-remove, not both.");
        }
        if ((Startup || StartupRemove)
            && (Status || Stop || List || NotifyTest))
        {
            return ValidationResult.Error(
                "--startup/--startup-remove are not compatible with " +
                "--status, --stop, --list, or --notify-test.");
        }
        if ((Startup || StartupRemove)
            && (Detach || DryRun || Once || Cycles is not null || HtmlFile is not null))
        {
            return ValidationResult.Error(
                "--startup/--startup-remove are not compatible with " +
                "--detach, --dry-run, --once, --cycles, or --html.");
        }
        if ((Startup || StartupRemove) && ExitOnAlert)
        {
            return ValidationResult.Error(
                "--startup/--startup-remove are not compatible with " +
                "--exit-on-alert.");
        }
        if (StartupRemove && (Asins.Length > 0 || AsinFile is not null))
        {
            return ValidationResult.Error(
                "--startup-remove does not accept ASINs or --asin-file; use " +
                "--config/--save-path to identify the monitor.");
        }

        if (Status && Stop)
        {
            return ValidationResult.Error("Use --status or --stop, not both.");
        }
        if (List && (Status || Stop))
        {
            return ValidationResult.Error(
                "--list is not compatible with --status or --stop.");
        }
        if (List && (Detach || DryRun || Once || Cycles is not null || HtmlFile is not null))
        {
            return ValidationResult.Error(
                "--list is not compatible with --detach, --dry-run, --once, " +
                "--cycles, or --html.");
        }
        if (List && (Asins.Length > 0
                     || AsinFile is not null
                     || libraryTargets > 0))
        {
            return ValidationResult.Error(
                "--list does not accept ASINs, --asin-file, or library targets.");
        }
        if (List && NotifyTest)
        {
            return ValidationResult.Error(
                "--list is not compatible with --notify-test.");
        }
        if (TkOpt && !List && !ListBindings && string.IsNullOrWhiteSpace(RemoveBinding))
        {
            return ValidationResult.Error(
                "--tk-opt requires --list, --list-bindings, or --remove-binding.");
        }
        if (!List && (All || Prune || Verbose))
        {
            return ValidationResult.Error(
                "--all, --prune, and --verbose require --list.");
        }
        if (!List
            && !Stats
            && !Restart
            && !ListBindings
            && string.IsNullOrWhiteSpace(RemoveBinding)
            && Json)
        {
            return ValidationResult.Error(
                "--json requires --list, --stats, --restart, " +
                "--list-bindings, or --remove-binding.");
        }
        if (Json && TkOpt)
        {
            return ValidationResult.Error("Use --json or --tk-opt, not both.");
        }
        if ((Status || Stop || StartupRemove)
            && (HtmlFile is not null
                || Asins.Length > 0
                || AsinFile is not null
                || libraryTargets > 0))
        {
            return ValidationResult.Error(
                "--status/--stop/--startup-remove do not accept ASINs or --html; " +
                "use --config/--save-path.");
        }
        if ((Status || Stop) && (Detach || DryRun || Once || Cycles is not null))
        {
            return ValidationResult.Error(
                "--status/--stop are not compatible with --detach/--dry-run/--once/--cycles.");
        }
        if (Detach && (HtmlFile is not null || Once || DryRun || Status || Stop))
        {
            return ValidationResult.Error(
                "--detach is not compatible with --html/--once/--dry-run/--status/--stop.");
        }

        if (HtmlFile is not null)
        {
            if (Asins.Length != 1)
            {
                return ValidationResult.Error("--html requires exactly one ASIN.");
            }
            if (Config is not null || AsinFile is not null)
            {
                return ValidationResult.Error(
                    "--html is not compatible with --config or --asin-file.");
            }
            if (Caps.Length > 0 || Countries.Length > 0 || DomainCookies.Length > 0)
            {
                return ValidationResult.Error(
                    "--html does not use the network: remove --cap/--country/--domain-cookie.");
            }
            if (Cycles is not null)
            {
                return ValidationResult.Error("--html runs a single cycle: remove --cycles.");
            }
        }
        else if (!Status && !Stop && !List && !StartupRemove && !NotifyTest
                 && !Stats && !Restart
                 && Asins.Length == 0
                 && AsinFile is null
                 && Config is null
                 && libraryTargets == 0
                 && !ListBindings
                 && string.IsNullOrWhiteSpace(RemoveBinding))
        {
            return ValidationResult.Error(
                "At least one ASIN, --asin-file, --config, library target, " +
                "or binding is required.");
        }

        if (Timer is { } timer && timer is < 10 or > 2_592_000)
        {
            return ValidationResult.Error(
                "--timer must be between 10 and 2592000 seconds.");
        }

        if (RequestDelay is { } delay)
        {
            var minimum = HtmlFile is null ? 6 : 0;
            if (delay < minimum || delay > 3600)
            {
                return ValidationResult.Error(
                    $"--request-delay must be between {minimum} and 3600 seconds.");
            }
        }

        if (Jitter is < 0 or > 3600)
        {
            return ValidationResult.Error("--jitter must be between 0 and 3600 seconds.");
        }

        if (Once && Cycles is not null)
        {
            return ValidationResult.Error("Use --once or --cycles, not both.");
        }
        if (Cycles is < 1)
        {
            return ValidationResult.Error("--cycles must be at least 1.");
        }

        if (SaveFormat is not null
            && SaveFormat.Trim().ToLowerInvariant() is not ("json" or "jsonl" or "csv"))
        {
            return ValidationResult.Error("--save-format must be json, jsonl, or csv.");
        }

        if (Layout is not null
            && Layout.Trim().ToLowerInvariant() is not
                ("auto" or "combined" or "per-group" or "single"))
        {
            return ValidationResult.Error(
                "--layout must be auto, combined, per-group, or single.");
        }

        if (string.Equals(Layout?.Trim(), "single", StringComparison.OrdinalIgnoreCase)
            && string.Equals(SaveFormat?.Trim(), "csv", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Error(
                "--layout single is only supported with --save-format json/jsonl.");
        }

        if (MaxPrice is { } maxPrice && maxPrice <= 0)
        {
            return ValidationResult.Error("--max-price must be greater than zero.");
        }
        if (DropPercent is { } drop && (drop <= 0 || drop > 100))
        {
            return ValidationResult.Error("--drop-percent must be between 0 and 100.");
        }
        if (RisePercent is { } rise && (rise <= 0 || rise > 100))
        {
            return ValidationResult.Error("--rise-percent must be between 0 and 100.");
        }
        if ((MaxPrice is not null || DropPercent is not null || RisePercent is not null)
            && Asins.Length == 0
            && AsinFile is null
            && libraryTargets == 0)
        {
            return ValidationResult.Error(
                "--max-price/--drop-percent/--rise-percent require a CLI or " +
                "library target; for configuration use max_price/drop_percent/" +
                "rise_percent in the JSON.");
        }
        if (Baseline is not null
            && Baseline.Trim().ToLowerInvariant() is not
                ("original" or "first_seen" or "max_seen" or "last_seen"))
        {
            return ValidationResult.Error(
                "--baseline must be original, first_seen, max_seen, or last_seen.");
        }
        if (Cooldown is < 0)
        {
            return ValidationResult.Error("--cooldown cannot be negative.");
        }
        if (BlockedCooldown < 60)
        {
            return ValidationResult.Error("--blocked-cooldown must be at least 60 seconds.");
        }
        if (MaxConsecutiveDomainFailures < 1)
        {
            return ValidationResult.Error(
                "--max-consecutive-domain-failures must be at least 1.");
        }
        if (SavePath is not null && string.IsNullOrWhiteSpace(SavePath))
        {
            return ValidationResult.Error("--save-path cannot be empty.");
        }
        if (SaveFile is not null)
        {
            var file = SaveFile.Trim();
            if (file.Length == 0
                || file is "." or ".."
                || file.Contains('/')
                || file.Contains('\\')
                || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return ValidationResult.Error(
                    "--save-file must be a simple name, without directories.");
            }
        }
        if (LogPath is not null && string.IsNullOrWhiteSpace(LogPath))
        {
            return ValidationResult.Error("--log-path cannot be empty.");
        }
        if (Config is not null && string.IsNullOrWhiteSpace(Config))
        {
            return ValidationResult.Error("--config cannot be empty.");
        }
        if (AsinFile is not null && string.IsNullOrWhiteSpace(AsinFile))
        {
            return ValidationResult.Error("--asin-file cannot be empty.");
        }

        var hasAdaptiveCli = HasAdaptiveOptions();
        if (Schedule is not null
            && !string.Equals(Schedule.Trim(), "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Error(
                "--schedule only supports the value 'adaptive'.");
        }
        if (hasAdaptiveCli && Timer is not null)
        {
            return ValidationResult.Error(
                "--timer and adaptive scheduling options are not compatible.");
        }
        if (hasAdaptiveCli && Jitter > 0)
        {
            return ValidationResult.Error(
                "--jitter is not compatible with adaptive scheduling.");
        }
        if (Window is { } window && window is < 60 or > 2_592_000)
        {
            return ValidationResult.Error(
                "--window must be between 60 and 2592000 seconds.");
        }
        if (MaxRequests is { } maxRequests && maxRequests is < 1 or > 1_000_000)
        {
            return ValidationResult.Error(
                "--max-requests must be between 1 and 1000000.");
        }
        if (MinGap is { } minGap && minGap is < 1 or > 2_592_000)
        {
            return ValidationResult.Error(
                "--min-gap must be between 1 and 2592000 seconds.");
        }
        if (MaxGap is { } maxGap && maxGap is < 1 or > 2_592_000)
        {
            return ValidationResult.Error(
                "--max-gap must be between 1 and 2592000 seconds.");
        }
        if (MinGap is { } configuredMin
            && MaxGap is { } configuredMax
            && configuredMax < configuredMin)
        {
            return ValidationResult.Error(
                "--max-gap must be greater than or equal to --min-gap.");
        }

        return ValidationResult.Success();
    }

    private bool HasAdaptiveOptions() =>
        Schedule is not null
        || Window is not null
        || MaxRequests is not null
        || MinGap is not null
        || MaxGap is not null;
}
