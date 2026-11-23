using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AmazonScraper.Cli;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.EnvSetup;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Skill;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Cli.Web;
using AmazonScraper.Update;
using Spectre.Console.Cli;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
if (Console.IsOutputRedirected)
{
    // Redirected stdout bypasses the console code page. Rewrap it so JSON and
    // TSV are always UTF-8, including on Windows.
    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8)
    {
        AutoFlush = true,
    });
}
else
{
    try
    {
        Console.OutputEncoding = utf8;
    }
    catch (IOException)
    {
        // Some Windows consoles do not allow changing the encoding.
    }
}

if (TryHandleEnvironmentVar(args, out var envVarExitCode))
{
    return envVarExitCode;
}

if (StartupLauncher.TryPrepare(args, out var startupArgs, out var startupExitCode))
{
    if (startupExitCode != 0)
    {
        return startupExitCode;
    }

    args = startupArgs;
}

TrySyncInstallManifest(args);

if (TryHandleVersion(args, out var versionExitCode))
{
    return versionExitCode;
}

if (args is ["--skill"])
{
    SkillPrinter.Print();
    return 0;
}

// Spectre.Console.Cli options are command-scoped. Accept the Python-style
// leading form too: `amz-cli --domain it search ...` is normalized to
// `amz-cli search ... --domain it`.
args = NormalizeLeadingGlobalOptions(args);

// The auto-update hook is deliberately fire-and-forget. It skips monitor,
// update, version/help and the hidden update helpers.
AutoUpdateLauncher.TryScheduleStartup(args);

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("amz-cli");
    config.SetApplicationVersion(VersionInfo.Informational);

    config.AddCommand<SearchCommand>("search")
        .WithDescription("Search Amazon products and show the results.");
    config.AddCommand<DealsCommand>("deals")
        .WithDescription("Show active deals without a search query.");
    config.AddBranch("compare", compare =>
    {
        compare.SetDescription(
            "Push products into the shared comparison tabs used by the Web UI.");
        compare.AddCommand<CompareShowCommand>("show")
            .WithDescription("Fetch and print the comparison table.");
        compare.AddCommand<CompareAddCommand>("add")
            .WithDescription("Add one or more products to the comparison.");
        compare.AddCommand<CompareListCommand>("list")
            .WithDescription("List the products in the comparison.");
        compare.AddCommand<CompareRemoveCommand>("remove")
            .WithDescription("Remove one or more products from the comparison.");
        compare.AddCommand<CompareClearCommand>("clear")
            .WithDescription("Remove all products from the comparison.");
    });
    config.AddCommand<DetailsCommand>("details")
        .WithDescription("Show details for one or more products (ASINs).");
    config.AddCommand<MonitorCommand>("monitor")
        .WithDescription("Monitor prices and groups with a timer or adaptive budget, saving data.");
    config.AddBranch("history", history =>
    {
        history.SetDescription(
            "Inspect or remove searches and opened products from the shared history.");
        history.AddCommand<HistoryListCommand>("list")
            .WithDescription("List history entries.");
        history.AddCommand<HistoryRemoveCommand>("remove")
            .WithDescription("Remove history entries by id.");
        history.AddCommand<HistoryClearCommand>("clear")
            .WithDescription("Remove all history entries and cached results.");
    });
    config.AddBranch("library", library =>
    {
        library.SetDescription(
            "Manage saved products and groups shared with the web interface.");
        library.AddCommand<LibraryListCommand>("list")
            .WithDescription("List saved products.");
        library.AddCommand<LibraryShowCommand>("show")
            .WithDescription("Show one saved product.");
        library.AddCommand<LibraryAddCommand>("add")
            .WithDescription("Save one or more products.");
        library.AddCommand<LibraryRemoveCommand>("remove")
            .WithDescription("Remove one or more saved products.");
        library.AddCommand<LibraryImportMonitorCommand>("import-monitor")
            .WithDescription(
                "Import legacy monitor groups/items into the saved library.");
        library.AddBranch("group", group =>
        {
            group.SetDescription(
                "Manage saved product groups without starting monitoring.");
            group.AddCommand<LibraryGroupListCommand>("list")
                .WithDescription("List saved groups.");
            group.AddCommand<LibraryGroupShowCommand>("show")
                .WithDescription("Show a saved group and its products.");
            group.AddCommand<LibraryGroupCreateCommand>("create")
                .WithDescription("Create an empty saved group.");
            group.AddCommand<LibraryGroupRenameCommand>("rename")
                .WithDescription("Rename a saved group.");
            group.AddCommand<LibraryGroupEditCommand>("edit")
                .WithDescription("Edit a saved group's name, color, or description.");
            group.AddCommand<LibraryGroupDeleteCommand>("delete")
                .WithDescription("Delete a saved group, keeping its products.");
            group.AddCommand<LibraryGroupAddCommand>("add")
                .WithDescription("Add saved products to a group.");
            group.AddCommand<LibraryGroupRemoveCommand>("remove")
                .WithDescription("Remove products from a group.");
        });
    });
    config.AddCommand<PathsCommand>("paths")
        .WithDescription("Show files, directories, and registrations used by the CLI.");
    config.AddBranch("settings", settings =>
    {
        settings.SetDescription(
            "View or change persistent HTTP settings shared with the Web UI and monitors.");
        settings.AddCommand<SettingsShowCommand>("show")
            .WithDescription("Show saved and effective HTTP settings.");
        settings.AddCommand<SettingsSetCommand>("set")
            .WithDescription("Save HTTP transport, profile, User-Agent, headers and timeout.");
        settings.AddCommand<SettingsResetCommand>("reset")
            .WithDescription("Reset the saved HTTP settings to automatic.");
        settings.AddCommand<SettingsPathCommand>("path")
            .WithDescription("Print the settings file path.");
    });
    config.AddCommand<DomainsCommand>("domains")
        .WithDescription("List available and planned marketplaces.");
    config.AddCommand<SelfTestCommand>("self-test")
        .WithDescription("Check runtime, version, and curl transport.");
    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Diagnose installation, updates, monitors, startup, and skill.");
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Check for and apply signed CLI updates.");
    config.AddCommand<SkillCommand>("skill")
        .WithDescription("Print, install, or check the CLI Markdown skill.");
    config.AddCommand<WebCommand>("web")
        .WithDescription("Start the local web interface for search and details.");
    config.AddCommand<UpdateApplyCommand>("__update-apply")
        .WithDescription("Internal helper: apply an update plan.")
        .IsHidden();
    config.AddCommand<WebResumeCommand>("__web-resume")
        .WithDescription("Internal helper: resume the Web interface after an update.")
        .IsHidden();
    config.AddCommand<UpdateVerifyCommand>("__update-verify")
        .WithDescription("Internal binary verification after an update.")
        .IsHidden();
    config.AddCommand<UpdateRollbackCommand>("__update-rollback")
        .WithDescription("Internal helper: restore a backup.")
        .IsHidden();
    config.AddCommand<UpdateAutoCommand>("__update-auto")
        .WithDescription("Internal auto-update coordinator.")
        .IsHidden();
    config.AddCommand<UpdateNotifyCommand>("__update-notify")
        .WithDescription("Internal helper for update notifications.")
        .IsHidden();
    if (OperatingSystem.IsWindows())
    {
        config.AddCommand<EnvVarCommand>("env-var")
            .WithDescription(
                "Windows only: user/system PATH; equivalent to --install-env-var, " +
                "--uninstall-env-var, and --status-env-var.");
    }
});

return await app.RunAsync(args);

static string[] NormalizeLeadingGlobalOptions(string[] input)
{
    var remaining = input.ToList();
    var globals = new List<string>();
    string[] namesWithValue = ["--domain", "--timeout", "--width", "--transport"];

    while (remaining.Count > 0)
    {
        var current = remaining[0];
        if (namesWithValue.Contains(current, StringComparer.Ordinal))
        {
            if (remaining.Count < 2)
            {
                break;
            }
            globals.Add(current);
            globals.Add(remaining[1]);
            remaining.RemoveRange(0, 2);
            continue;
        }

        if (namesWithValue.Any(name => current.StartsWith(name + "=", StringComparison.Ordinal)))
        {
            globals.Add(current);
            remaining.RemoveAt(0);
            continue;
        }

        break;
    }

    if (remaining.Count == 0 || globals.Count == 0)
    {
        return input;
    }

    remaining.AddRange(globals);
    return remaining.ToArray();
}

static bool TryHandleEnvironmentVar(string[] args, out int exitCode)
{
    exitCode = 0;
    if (args.Length == 0)
    {
        return false;
    }

    var flag = args[0];
    var isInstall = string.Equals(flag, "--install-env-var", StringComparison.Ordinal);
    var isUninstall = string.Equals(flag, "--uninstall-env-var", StringComparison.Ordinal);
    var isStatus = string.Equals(flag, "--status-env-var", StringComparison.Ordinal);
    if (!isInstall && !isUninstall && !isStatus)
    {
        return false;
    }

    var useSystem = false;
    if (args.Length > 2)
    {
        PrintEnvironmentVarUsage();
        exitCode = 1;
        return true;
    }
    if (args.Length == 2)
    {
        if (string.Equals(args[1], "system", StringComparison.Ordinal)
            || string.Equals(args[1], "--system", StringComparison.Ordinal))
        {
            useSystem = true;
        }
        else
        {
            PrintEnvironmentVarUsage();
            exitCode = 1;
            return true;
        }
    }

    if (isStatus && useSystem)
    {
        PrintEnvironmentVarUsage();
        exitCode = 1;
        return true;
    }

    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine(
            "PATH management is only available on Windows.");
        exitCode = 1;
        return true;
    }

    var result = isInstall
        ? WindowsPathInstaller.Install(useSystem)
        : isUninstall
            ? WindowsPathInstaller.Uninstall(useSystem)
            : WindowsPathInstaller.Status();

    if (result.Success)
    {
        Console.Out.WriteLine(result.Message);
    }
    else
    {
        Console.Error.WriteLine(result.Message);
    }

    exitCode = result.Success ? 0 : 1;
    return true;
}

static bool TryHandleVersion(string[] input, out int exitCode)
{
    exitCode = 0;
    if (input.Length == 0
        || input[0] is not ("--version" or "-v"))
    {
        return false;
    }

    var shortOutput = false;
    var json = false;
    var verbose = false;
    for (var index = 1; index < input.Length; index++)
    {
        switch (input[index])
        {
            case "--short":
                shortOutput = true;
                break;
            case "--json":
                json = true;
                break;
            case "--verbose":
                verbose = true;
                break;
            default:
                Console.Error.WriteLine(
                    $"Unrecognized option for --version: '{input[index]}'.");
                exitCode = 1;
                return true;
        }
    }

    if ((shortOutput && json) || (shortOutput && verbose) || (json && verbose))
    {
        Console.Error.WriteLine(
            "Use only one of --short, --json, and --verbose.");
        exitCode = 1;
        return true;
    }

    var report = VersionReport.Read();
    if (json)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(
            new
            {
                version = report.Version,
                channel = report.Channel,
                released_at = report.ReleasedDate,
                released_at_iso = report.ReleasedAt?
                    .ToUniversalTime()
                    .ToString("O"),
                platform = report.Platform,
                install_dir = report.InstallDir,
                installed_at = report.InstalledAt?
                    .ToUniversalTime()
                    .ToString("O"),
                updated_at = report.UpdatedAt?
                    .ToUniversalTime()
                    .ToString("O"),
                source = report.Source,
                display = report.Display,
            },
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        return true;
    }

    if (shortOutput)
    {
        Console.Out.WriteLine(report.ShortDisplay);
        return true;
    }

    if (verbose)
    {
        Console.Out.WriteLine($"Version: {report.Version}");
        Console.Out.WriteLine($"Channel: {report.Channel}");
        Console.Out.WriteLine(
            $"Release date: {FormatVersionDate(report.ReleasedAt)}");
        Console.Out.WriteLine($"Platform: {report.Platform}");
        Console.Out.WriteLine($"Installation: {report.InstallDir}");
        Console.Out.WriteLine(
            $"Installed: {FormatVersionDate(report.InstalledAt)}");
        Console.Out.WriteLine(
            $"Updated: {FormatVersionDate(report.UpdatedAt)}");
        Console.Out.WriteLine($"Source: {report.Source}");
        return true;
    }

    Console.Out.WriteLine(report.Display);
    return true;
}

static string FormatVersionDate(DateTimeOffset? value) =>
    value is null
        ? "not available"
        : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");

static void TrySyncInstallManifest(string[] args)
{
    if (args.Length > 0
        && args[0] is "__update-apply"
            or "__update-verify"
            or "__update-rollback")
    {
        return;
    }

    try
    {
        _ = InstallManifestUpdater.SyncCurrent(
            InstallLocation.Resolve(),
            VersionInfo.Informational,
            PlatformInfo.Detect());
    }
    catch (Exception)
    {
        // Metadata repair is best effort and must never block a command.
    }
}

static void PrintEnvironmentVarUsage()
{
    Console.Error.WriteLine(
        "Usage: amz-cli --install-env-var [system] | " +
        "--uninstall-env-var [system] | --status-env-var");
}
