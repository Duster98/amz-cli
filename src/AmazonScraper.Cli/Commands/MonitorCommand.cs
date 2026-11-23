using System.Runtime.InteropServices;
using System.Text.Json;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Update;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class MonitorCommand : AsyncCommand<MonitorSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        MonitorSettings settings,
        CancellationToken cancellationToken)
    {
        MonitorLogger? logger = null;
        try
        {
            if (settings.NotifyTest)
            {
                using var testLogger = new MonitorLogger(settings.Quiet, settings.LogPath);
                var testOptions = NotificationOptionsResolver.ForTest(settings);
                var service = NotificationService.Create(testOptions, testLogger);
                var result = await service.TestAsync(cancellationToken)
                    .ConfigureAwait(false);
                Console.Out.WriteLine($"Backend: {service.BackendName}");
                if (result.Success)
                {
                    Console.Out.WriteLine("Result: notification sent");
                    if (!string.IsNullOrWhiteSpace(result.Detail))
                    {
                        Console.Out.WriteLine($"Detail: {result.Detail}");
                    }
                    return 0;
                }

                Console.Error.WriteLine(
                    $"Result: failed: {result.Error ?? "unknown error"}");
                Console.Error.WriteLine(NotificationBackendFactory.GetInstallHint());
                return 1;
            }

            if (settings.ListBindings)
            {
                return ListBindings(settings);
            }
            if (!string.IsNullOrWhiteSpace(settings.RemoveBinding))
            {
                return RemoveBinding(settings);
            }

            var config = settings.Config is null
                ? null
                : MonitorConfigLoader.Load(settings.Config);
            MonitorBinding? earlyBinding = null;
            if (!string.IsNullOrWhiteSpace(settings.Binding))
            {
                earlyBinding = new MonitorBindingStore()
                    .Find(settings.Binding.Trim())
                    ?? throw new MonitorException(
                        $"Monitor binding not found: {settings.Binding}.");
            }
            var runtime = earlyBinding?.Runtime;
            var savePath = settings.SavePath
                ?? runtime?.SavePath
                ?? config?.Save?.Path
                ?? ".";
            var saveFile = settings.SaveFile
                ?? runtime?.SaveFile
                ?? config?.Save?.File;
            var paths = MonitorPaths.Resolve(savePath, saveFile);

            if (settings.Status)
            {
                var status = MonitorBackground.Status(paths);
                PrintStartupStatus(paths);
                return status;
            }
            if (settings.Stop)
            {
                var current = TryReadPid(paths.PidPath);
                var stopped = await MonitorBackground.StopAsync(paths)
                    .ConfigureAwait(false);
                if (current is not null)
                {
                    RemoveRegistryEntryForPid(current.Pid, paths);
                }
                PrintStartupStopHint(paths);
                return stopped;
            }
            if (settings.List)
            {
                return ListMonitors(settings, paths);
            }
            if (settings.Stats)
            {
                return ShowStats(settings, paths);
            }
            if (settings.Restart)
            {
                return await RestartMonitorsAsync(settings, paths)
                    .ConfigureAwait(false);
            }
            if (settings.StartupRemove)
            {
                return await RemoveStartupAsync(paths).ConfigureAwait(false);
            }

            var defaultDomain = settings.Domain ?? config?.Domain ?? "it";
            var timer = settings.Timer
                ?? runtime?.TimerSeconds
                ?? config?.TimerSeconds
                ?? 3600;
            var requestDelay = settings.RequestDelay
                ?? runtime?.RequestDelaySeconds
                ?? config?.RequestDelaySeconds
                ?? 10;
            if (timer is < 10 or > 2_592_000)
            {
                throw new MonitorException(
                    "The effective timer must be between 10 and 2592000 seconds.");
            }
            if (settings.HtmlFile is null && requestDelay is < 6 or > 3600)
            {
                throw new MonitorException(
                    "The effective request delay must be between 6 and 3600 seconds.");
            }
            if (settings.HtmlFile is not null && requestDelay is < 0 or > 3600)
            {
                throw new MonitorException(
                    "The effective request delay must be between 0 and 3600 seconds.");
            }

            var format = ParseFormat(
                settings.SaveFormat
                ?? runtime?.SaveFormat
                ?? config?.Save?.Format);
            var layout = ParseLayout(
                settings.Layout
                ?? runtime?.Layout
                ?? config?.Save?.Layout);
            var warnings = new List<string>();
            MonitorPlan plan;
            Func<MonitorPlan>? planProvider = null;
            MonitorBinding? libraryBinding = null;
            if (!string.IsNullOrWhiteSpace(settings.Binding)
                || !string.IsNullOrWhiteSpace(settings.LibraryProduct)
                || !string.IsNullOrWhiteSpace(settings.LibraryGroup))
            {
                var library = new SavedLibraryStore().Read();
                var bindingStore = new MonitorBindingStore();
                libraryBinding = MonitorBindingFactory.Resolve(
                    settings,
                    library,
                    bindingStore,
                    persist: false);
                plan = LibraryMonitorPlanBuilder.Build(
                    libraryBinding,
                    library,
                    bindingStore.Read());
                if (!settings.DryRun)
                {
                    if (string.IsNullOrWhiteSpace(settings.Binding)
                        || HasRuntimeOverrides(settings))
                    {
                        libraryBinding.Runtime = BuildRuntime(
                            settings,
                            paths,
                            format,
                            layout,
                            timer,
                            requestDelay);
                        bindingStore.Upsert(libraryBinding);
                    }
                    var bindingId = libraryBinding.Id;
                    planProvider = () =>
                    {
                        var currentLibrary = new SavedLibraryStore().Read();
                        var currentBindings = new MonitorBindingStore().Read();
                        var currentBinding = currentBindings.Bindings
                            .FirstOrDefault(binding => string.Equals(
                                binding.Id,
                                bindingId,
                                StringComparison.Ordinal))
                            ?? throw new MonitorException(
                                $"Monitor binding was removed: {bindingId}.");
                        return LibraryMonitorPlanBuilder.Build(
                            currentBinding,
                            currentLibrary,
                            currentBindings);
                    };
                }
            }
            else
            {
                plan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
                {
                    Config = config,
                    CliAsins = settings.Asins,
                    AsinFile = settings.AsinFile,
                    DefaultDomain = defaultDomain,
                    CliMaxPrice = settings.MaxPrice,
                    CliDropPercent = settings.DropPercent,
                    CliRisePercent = settings.RisePercent,
                    CliBaseline = settings.Baseline,
                    CliCooldownSeconds = settings.Cooldown,
                    ForceInterval = settings.Timer is not null
                        && !HasAdaptiveOptions(settings),
                    CliScheduleMode = settings.Schedule,
                    CliWindowSeconds = settings.Window,
                    CliMaxRequests = settings.MaxRequests,
                    CliMinGapSeconds = settings.MinGap,
                    CliMaxGapSeconds = settings.MaxGap,
                    Warning = warnings.Add,
                });
            }

            if (plan.Schedule is not null
                && (settings.Once
                    || settings.Cycles is not null
                    || settings.HtmlFile is not null))
            {
                warnings.Add(
                    "--once/--cycles/--html use cycle mode: " +
                    "adaptive scheduling ignored.");
            }

            ApplyRuntimeDefaults(
                settings,
                runtime ?? libraryBinding?.Runtime);
            var resolvedLayout = MonitorOutput.ResolveLayout(layout, format, plan);
            var notificationOptions = NotificationOptionsResolver.FromSettings(
                settings,
                config,
                runtime ?? libraryBinding?.Runtime);
            var domainSettings = MonitorDomainSettings.Build(
                plan.Domains.ToArray(),
                settings.Caps,
                settings.Countries,
                settings.DomainCookies,
                settings.Cookie);
            if (plan.Schedule is null)
            {
                var minimumCycleSeconds = plan.AllTargets.Count() * requestDelay;
                if (timer < minimumCycleSeconds)
                {
                    warnings.Add(
                        $"The timer ({timer}s) is shorter than the minimum cycle " +
                        $"({minimumCycleSeconds}s = {plan.AllTargets.Count()} targets x " +
                        $"{requestDelay}s): requests will run continuously.");
                }
            }

            if (settings.DryRun)
            {
                PrintWarnings(warnings);
                PrintDryRun(
                    plan,
                    paths,
                    format,
                    resolvedLayout,
                    domainSettings,
                    timer,
                    requestDelay,
                    notificationOptions);
                return 0;
            }

            if (settings.Startup)
            {
                PrintWarnings(warnings);
                var startupCommandLine = libraryBinding is null
                    ? null
                    : BuildBindingStartupCommandLine(
                        libraryBinding.Id,
                        Environment.GetCommandLineArgs());
                return await InstallStartupAsync(
                        settings,
                        paths,
                        plan,
                        format,
                        resolvedLayout,
                        timer,
                        startupCommandLine)
                    .ConfigureAwait(false);
            }

            if (settings.Detach)
            {
                PrintWarnings(warnings);
                if (notificationOptions.Enabled
                    && NotificationBackendFactory.Create() is null)
                {
                    Console.Error.WriteLine(
                        $"[WARN] {NotificationBackendFactory.GetInstallHint()}");
                }
                var logPath = Path.GetFullPath(settings.LogPath ?? paths.LogPath);
                var runtimeHash = MonitorRuntimeSignature.Compute(
                    timer,
                    requestDelay,
                    settings.Jitter,
                    format,
                    resolvedLayout,
                    settings.Notify,
                    (settings.NotifyOn ?? string.Empty).Split(
                        [',', ';', ' '],
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries),
                    settings.NotifyCooldown,
                    settings.NoAutoUpdate,
                    paths.SavePath,
                    paths.BaseName);
                var info = await MonitorBackground.StartDetachedAsync(
                        paths,
                        logPath,
                        plan,
                        format,
                        resolvedLayout,
                        timer,
                        libraryBinding?.Id,
                        runtimeHash)
                    .ConfigureAwait(false);
                Console.Out.WriteLine("Monitor started in background.");
                Console.Out.WriteLine($"PID: {info.Pid}");
                Console.Out.WriteLine($"Log: {info.LogPath}");
                foreach (var file in info.DataFiles)
                {
                    Console.Out.WriteLine($"Data: {file}");
                }
                return 0;
            }

            logger = new MonitorLogger(settings.Quiet, settings.LogPath);
            foreach (var warning in warnings)
            {
                logger.Warn(warning);
            }

            var notifications = NotificationService.Create(notificationOptions, logger);
            var once = settings.Once || settings.HtmlFile is not null;
            var runner = new MonitorRunner(
                plan,
                settings,
                paths,
                format,
                resolvedLayout,
                timer,
                requestDelay,
                settings.Jitter,
                settings.BlockedCooldown,
                settings.MaxConsecutiveDomainFailures,
                once,
                settings.Cycles,
                settings.ExitOnAlert,
                settings.HtmlFile,
                logger,
                notifications,
                planProvider);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancelHandler = (_, args) =>
            {
                args.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            PosixSignalRegistration? sigterm = null;
            try
            {
                try
                {
                    sigterm = PosixSignalRegistration.Create(
                        PosixSignal.SIGTERM,
                        signal =>
                        {
                            signal.Cancel = true;
                            cts.Cancel();
                        });
                }
                catch (Exception)
                {
                    // Windows and some hosts do not support POSIX signals.
                }

                var result = await runner.RunAsync(cts.Token).ConfigureAwait(false);
                PrintResult(result);
                return result.ExitCode;
            }
            finally
            {
                sigterm?.Dispose();
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (OperationCanceledException)
        {
            CliConsole.Error("Monitor interrupted.");
            return 130;
        }
        catch (MonitorException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
        catch (MarketplaceException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CliConsole.Error($"Monitor error: {ex.Message}");
            return 1;
        }
        finally
        {
            logger?.Dispose();
        }
    }

    private static void ApplyRuntimeDefaults(
        MonitorSettings settings,
        MonitorRuntimeSettings? runtime)
    {
        if (runtime is null)
        {
            return;
        }

        if (settings.Jitter == 0 && runtime.JitterSeconds is { } jitter)
        {
            settings.Jitter = jitter;
        }
        if (!settings.Notify && runtime.Notify == true)
        {
            settings.Notify = true;
        }
        if ((settings.Notify || runtime.Notify == true)
            && string.IsNullOrWhiteSpace(settings.NotifyOn)
            && runtime.NotifyOn.Count > 0)
        {
            settings.NotifyOn = string.Join(',', runtime.NotifyOn);
        }
        if (settings.NotifyCooldown is null
            && runtime.NotifyCooldownSeconds is { } cooldown)
        {
            settings.NotifyCooldown = cooldown;
        }
        if (runtime.NoAutoUpdate == true)
        {
            settings.NoAutoUpdate = true;
        }
    }

    private static bool HasRuntimeOverrides(MonitorSettings settings) =>
        settings.Timer is not null
        || settings.RequestDelay is not null
        || settings.Jitter != 0
        || settings.SavePath is not null
        || settings.SaveFile is not null
        || settings.SaveFormat is not null
        || settings.Layout is not null
        || settings.Notify
        || settings.NotifyOn is not null
        || settings.NotifyCooldown is not null
        || settings.NoAutoUpdate;

    private static MonitorRuntimeSettings BuildRuntime(
        MonitorSettings settings,
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout layout,
        int timer,
        int requestDelay) =>
        new()
        {
            TimerSeconds = timer,
            RequestDelaySeconds = requestDelay,
            JitterSeconds = Math.Max(0, settings.Jitter),
            SavePath = paths.SavePath,
            SaveFile = paths.BaseName,
            SaveFormat = format == MonitorFormat.Csv ? "csv" : "json",
            Layout = layout switch
            {
                MonitorLayout.PerGroup => "per-group",
                MonitorLayout.Combined => "combined",
                MonitorLayout.Single => "single",
                _ => "auto",
            },
            Notify = settings.Notify ? true : null,
            NotifyOn = (settings.NotifyOn ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .ToList(),
            NotifyCooldownSeconds = settings.NotifyCooldown,
            NoAutoUpdate = settings.NoAutoUpdate ? true : null,
        };

    private static IReadOnlyList<string> BuildBindingStartupCommandLine(
        string bindingId,
        IReadOnlyList<string> arguments)
    {
        var withValue = new HashSet<string>(StringComparer.Ordinal)
        {
            "--library-product",
            "--library-group",
            "--binding",
            "--max-price",
            "--drop-percent",
            "--rise-percent",
            "--baseline",
            "--cooldown",
            "--total-max",
            "--currency",
            "--fx",
        };
        var flags = new HashSet<string>(StringComparer.Ordinal)
        {
            "--allow-incomplete",
        };
        var result = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var name = argument;
            var hasInlineValue = false;
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                var equals = argument.IndexOf('=');
                if (equals > 0)
                {
                    name = argument[..equals];
                    hasInlineValue = true;
                }
            }

            if (flags.Contains(name))
            {
                continue;
            }
            if (withValue.Contains(name))
            {
                if (!hasInlineValue
                    && index + 1 < arguments.Count
                    && !arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    index++;
                }
                continue;
            }
            result.Add(argument);
        }
        result.Add("--binding");
        result.Add(bindingId);
        return result;
    }

    private static async Task<int> InstallStartupAsync(
        MonitorSettings settings,
        MonitorPaths paths,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timer,
        IReadOnlyList<string>? commandLine = null)
    {
        try
        {
            var manager = StartupManager.Create();
            var definition = StartupDefinitionFactory.Create(
                paths,
                plan,
                format,
                layout,
                timer,
                settings.Config,
                settings.LogPath,
                commandLine ?? Environment.GetCommandLineArgs());
            var current = TryReadPid(paths.PidPath);
            var running = current is not null
                && MonitorProcessInspector.IsRunning(current);
            var managed = running
                && current is not null
                && (string.Equals(
                        current.Mode,
                        "startup",
                        StringComparison.Ordinal)
                    || string.Equals(
                        current.StartupId,
                        definition.Id,
                        StringComparison.Ordinal));

            manager.Register(definition);

            if (running && !managed)
            {
                Console.Out.WriteLine($"Startup installed: {definition.Kind}");
                Console.Out.WriteLine($"Name: {definition.Name}");
                Console.Out.WriteLine(
                    $"Monitor already running (PID {current!.Pid}) without startup: " +
                    "not starting a duplicate.");
                Console.Out.WriteLine(
                    "Startup will be used at the next login.");
                PrintStartupRemovalHint(paths);
                return 0;
            }

            if (running
                && managed
                && string.Equals(
                    manager.Kind,
                    WindowsTaskStartup.KindName,
                    StringComparison.Ordinal))
            {
                await MonitorBackground.StopAsync(paths).ConfigureAwait(false);
            }

            try
            {
                manager.Start(
                    definition,
                    restart: string.Equals(
                        manager.Kind,
                        SystemdUserStartup.KindName,
                        StringComparison.Ordinal));
            }
            catch (MonitorException ex)
            {
                Console.Error.WriteLine(ex.Message);
                PrintLogTail(paths.LogPath);
                PrintStartupHelperLog();
                PrintStartupRemovalHint(paths);
                return 1;
            }

            var started = await WaitForStartupAsync(
                    paths,
                    TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            if (started is null)
            {
                Console.Error.WriteLine(
                    "Startup installed, but the monitor is not running.");
                PrintLogTail(paths.LogPath);
                PrintStartupHelperLog();
                PrintStartupRemovalHint(paths);
                return 1;
            }

            Console.Out.WriteLine($"Startup installed: {definition.Kind}");
            Console.Out.WriteLine($"Name: {definition.Name}");
            Console.Out.WriteLine($"Monitor started (PID {started.Pid}).");
            Console.Out.WriteLine($"Log: {started.LogPath}");
            foreach (var file in started.DataFiles)
            {
                Console.Out.WriteLine($"Data: {file}");
            }
            PrintStartupRemovalHint(paths);
            return 0;
        }
        catch (MonitorException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RemoveStartupAsync(MonitorPaths paths)
    {
        try
        {
            var manager = StartupManager.Create();
            var id = StartupIdentity.CreateId(paths.SavePath, paths.BaseName);
            StartupDefinition? existing = null;
            try
            {
                existing = manager.Registry.FindById(id);
            }
            catch (MonitorException)
            {
                // A malformed spec must not prevent removing the unit/task.
            }

            var definition = existing
                ?? new StartupDefinition
                {
                    Id = id,
                    WorkingDirectory = StartupIdentity.NormalizePath(
                        Environment.CurrentDirectory),
                    SavePath = StartupIdentity.NormalizePath(paths.SavePath),
                    SaveFile = paths.BaseName,
                };
            var current = TryReadPid(paths.PidPath);
            var running = current is not null
                && MonitorProcessInspector.IsRunning(current);
            var managed = running
                && current is not null
                && (string.Equals(
                        current.Mode,
                        "startup",
                        StringComparison.Ordinal)
                    || string.Equals(
                        current.StartupId,
                        id,
                        StringComparison.Ordinal));
            var hadDefinition = existing is not null;
            var removed = manager.Remove(definition);

            if (managed
                && current is not null
                && MonitorProcessInspector.IsRunning(current))
            {
                await MonitorBackground.StopAsync(paths).ConfigureAwait(false);
            }
            if (managed && current is not null)
            {
                RemoveRegistryEntryForPid(current.Pid, paths);
            }

            if (!hadDefinition && !removed)
            {
                Console.Out.WriteLine("No auto-start configured.");
                return 0;
            }

            Console.Out.WriteLine(
                $"Auto-start removed: {manager.Kind} ({definition.Name}).");
            if (running && !managed && current is not null)
            {
                Console.Out.WriteLine(
                    $"The manual monitor (PID {current.Pid}) remains active. " +
                    "Use --stop to stop it.");
            }
            return 0;
        }
        catch (MonitorException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<MonitorPidInfo?> WaitForStartupAsync(
        MonitorPaths paths,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var info = TryReadPid(paths.PidPath);
            if (info is not null && MonitorProcessInspector.IsRunning(info))
            {
                return info;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        return null;
    }

    private static MonitorPidInfo? TryReadPid(string path)
    {
        try
        {
            return MonitorPidInfo.TryLoad(path);
        }
        catch (MonitorException)
        {
            return null;
        }
    }

    private static void RemoveRegistryEntryForPid(
        int pid,
        MonitorPaths paths)
    {
        try
        {
            var registry = new MonitorRegistry();
            foreach (var entry in registry.ReadAll())
            {
                if (entry.Pid != pid
                    || !StartupIdentity.PathsEqual(entry.SavePath, paths.SavePath)
                    || !StartupIdentity.FilesEqual(entry.SaveFile, paths.BaseName))
                {
                    continue;
                }

                registry.Unregister(entry.Id);
            }
        }
        catch (Exception)
        {
            // Best effort: --list --prune can still clean the entry.
        }
    }

    private static void PrintStartupRemovalHint(MonitorPaths paths)
    {
        var command = $"amz-cli monitor --save-path \"{paths.SavePath}\"" +
            (paths.BaseName == "amz-monitor"
                ? string.Empty
                : $" --save-file \"{paths.BaseName}\"") +
            " --startup-remove";
        Console.Out.WriteLine($"To remove it: {command}");
    }

    private static void PrintStartupStatus(MonitorPaths paths)
    {
        try
        {
            var definition = new StartupRegistry().FindBySchedule(
                paths.SavePath,
                paths.BaseName);
            if (definition is null)
            {
                return;
            }

            Console.Out.WriteLine(
                $"Startup: {definition.Kind} ({definition.Name})");
            Console.Out.WriteLine(
                $"Startup enabled: {(definition.Enabled ? "yes" : "no")}");
        }
        catch (Exception)
        {
            // A malformed startup definition must not break --status.
        }
    }

    private static void PrintStartupStopHint(MonitorPaths paths)
    {
        try
        {
            var definition = new StartupRegistry().FindBySchedule(
                paths.SavePath,
                paths.BaseName);
            if (definition is not null)
            {
                Console.Out.WriteLine(
                    "Startup still active: use --startup-remove to " +
                    "disable it.");
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void PrintLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Log: {path} (not found)");
                return;
            }

            Console.Error.WriteLine($"Log: {path}");
            foreach (var line in File.ReadLines(path).TakeLast(10))
            {
                Console.Error.WriteLine($"  {line}");
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static void PrintStartupHelperLog()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        PrintLogTail(Path.Combine(
            root,
            "AmzCLI",
            "startup-launch.log"));
    }

    private static int ListBindings(MonitorSettings settings)
    {
        var document = new MonitorBindingStore().Read();
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                document,
                MonitorBindingStore.JsonOptions));
            return 0;
        }

        var rows = document.Bindings;
        var runs = new MonitorDiscovery(new MonitorRegistry(), [])
            .Discover(includeStale: true, prune: false)
            .Entries;
        string StatusOf(MonitorBinding binding) =>
            runs.FirstOrDefault(run => string.Equals(
                run.BindingId,
                binding.Id,
                StringComparison.Ordinal))?.Status ?? "stopped";
        if (settings.TkOpt)
        {
            Console.WriteLine(
                "id\tkind\ttarget\tstatus\tmax_price\tdrop_percent\trise_percent\ttotal_max\tcurrency");
            foreach (var binding in rows)
            {
                Console.WriteLine(string.Join('\t',
                    binding.Id,
                    binding.Kind,
                    binding.IsGroup ? binding.GroupId : binding.ProductId,
                    StatusOf(binding),
                    binding.MaxPrice?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    binding.DropPercent?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    binding.RisePercent?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    binding.TotalMax?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    binding.Currency ?? string.Empty));
            }
            return 0;
        }

        if (rows.Count == 0)
        {
            Console.WriteLine("No monitor bindings.");
            return 0;
        }

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = new Style(Color.Cyan),
        };
        table.AddColumn("ID");
        table.AddColumn("Kind");
        table.AddColumn("Target");
        table.AddColumn("Status");
        table.AddColumn("Item max");
        table.AddColumn("Drop %");
        table.AddColumn("Rise %");
        table.AddColumn("Group max");
        foreach (var binding in rows)
        {
            table.AddRow(
                binding.Id,
                binding.Kind,
                binding.IsGroup
                    ? binding.GroupId ?? string.Empty
                    : binding.ProductId ?? string.Empty,
                StatusOf(binding),
                binding.MaxPrice?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                binding.DropPercent?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                binding.RisePercent?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                binding.TotalMax is { } totalMax
                    ? $"{totalMax.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                      (binding.Currency ?? string.Empty)
                    : "-");
        }
        CliConsole.Stdout(settings.Width).Write(table);
        return 0;
    }

    private static int RemoveBinding(MonitorSettings settings)
    {
        var id = settings.RemoveBinding!.Trim();
        var store = new MonitorBindingStore();
        if (store.Find(id) is null)
        {
            CliConsole.Error($"Monitor binding not found: {id}");
            return 1;
        }

        store.Remove(id);
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { id, removed = true },
                MonitorBindingStore.JsonOptions));
        }
        else
        {
            Console.WriteLine($"Monitor binding removed: {id}");
        }
        return 0;
    }

    private static int ListMonitors(MonitorSettings settings, MonitorPaths paths)
    {
        var discovery = new MonitorDiscovery(
            new MonitorRegistry(),
            [paths.SavePath, Environment.CurrentDirectory]);
        var result = discovery.Discover(settings.All, settings.Prune);
        if (settings.Verify)
        {
            VerifyStartupRegistrations(result.Entries);
        }

        if (settings.Prune)
        {
            Console.Error.WriteLine(
                result.PrunedCount == 0
                    ? "No stale entries to remove."
                    : result.PrunedCount == 1
                        ? "Removed 1 stale entry."
                        : $"Removed {result.PrunedCount} stale entries.");
        }

        if (settings.Json)
        {
            Console.Out.WriteLine(MonitorListRenderer.ToJson(result.Entries));
        }
        else if (settings.TkOpt)
        {
            Console.Out.WriteLine(MonitorListRenderer.ToTsv(result.Entries));
        }
        else
        {
            MonitorListRenderer.Render(
                result.Entries,
                settings.Verbose,
                settings.Width);
            if (result.Entries.Count == 0)
            {
                var bindings = new MonitorBindingStore().Read().Bindings;
                if (bindings.Count > 0)
                {
                    Console.WriteLine(
                        bindings.Count == 1
                            ? "1 configured monitor binding is stopped. Use --list-bindings to see it."
                            : $"{bindings.Count} configured monitor bindings are stopped. Use --list-bindings to see them.");
                }
            }
            if (result.StaleCount > 0 && !settings.All && !settings.Prune)
            {
                Console.Error.WriteLine(
                    result.StaleCount == 1
                        ? "1 stale entry. Use --all to see it or --prune to remove it."
                        : $"{result.StaleCount} stale entries. Use --all to see them or " +
                          "--prune to remove them.");
            }
        }

        return 0;
    }

    private static int ShowStats(
        MonitorSettings settings,
        MonitorPaths paths)
    {
        var registryEntry = new MonitorRegistry()
            .ReadAll()
            .FirstOrDefault(entry =>
                StartupIdentity.PathsEqual(entry.SavePath, paths.SavePath)
                && StartupIdentity.FilesEqual(
                    entry.SaveFile,
                    paths.BaseName));
        var report = MonitorStatsService.Build(
            paths,
            registryEntry?.DataFiles);
        if (settings.Json)
        {
            Console.Out.WriteLine(MonitorStatsRenderer.ToJson(report));
        }
        else
        {
            MonitorStatsRenderer.Render(report);
        }

        return 0;
    }

    private static async Task<int> RestartMonitorsAsync(
        MonitorSettings settings,
        MonitorPaths paths)
    {
        var filter = settings.SavePath is not null
            || settings.SaveFile is not null
            || settings.Config is not null;
        var inspection = MonitorRestartCoordinator.Inspect(
            InstallLocation.Resolve(),
            savePath: filter ? paths.SavePath : null,
            saveFile: filter ? paths.BaseName : null);

        if (inspection.HasForeground || inspection.HasUnrestartable)
        {
            var reason = inspection.BlockReason
                ?? "One or more monitors cannot be restarted automatically.";
            if (settings.Json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        stopped = Array.Empty<string>(),
                        restarted = Array.Empty<string>(),
                        errors = new[] { reason },
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.Error.WriteLine($"Cannot restart: {reason}");
            }
            return 1;
        }

        if (inspection.Items.Count == 0)
        {
            if (settings.Json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        stopped = Array.Empty<string>(),
                        restarted = Array.Empty<string>(),
                        errors = Array.Empty<string>(),
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.Out.WriteLine("No monitors to restart.");
            }
            return 0;
        }

        var stopped = await MonitorRestartCoordinator
            .StopAllAsync(inspection)
            .ConfigureAwait(false);
        var restarted = await MonitorRestartCoordinator
            .RestartAsync(inspection, stopped.Stopped)
            .ConfigureAwait(false);
        var errors = stopped.Errors.Concat(restarted.Errors).ToList();

        if (settings.Json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(
                new
                {
                    stopped = stopped.Stopped,
                    restarted = restarted.Restarted,
                    errors,
                },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.Out.WriteLine(
                $"Restarted: {restarted.Restarted.Count}");
            foreach (var id in restarted.Restarted)
            {
                Console.Out.WriteLine($"  {id}");
            }
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"  {error}");
            }
        }

        return errors.Count == 0 ? 0 : 1;
    }

    private static void VerifyStartupRegistrations(
        IReadOnlyList<MonitorListEntry> entries)
    {
        var definitions = new StartupRegistry().ReadAll();
        if (definitions.Count == 0)
        {
            return;
        }

        StartupManager manager;
        try
        {
            manager = StartupManager.Create();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Warning: startup verification unavailable: {ex.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.StartupId))
            {
                continue;
            }

            var definition = definitions.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    entry.StartupId,
                    StringComparison.Ordinal));
            if (definition is null)
            {
                entry.StartupRegistered = false;
                continue;
            }

            try
            {
                entry.StartupRegistered = manager.Exists(definition);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Warning: startup verification {definition.Name}: {ex.Message}");
            }
        }
    }

    private static bool HasAdaptiveOptions(MonitorSettings settings) =>
        settings.Schedule is not null
        || settings.Window is not null
        || settings.MaxRequests is not null
        || settings.MinGap is not null
        || settings.MaxGap is not null;

    private static MonitorFormat ParseFormat(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "json" or "jsonl" => MonitorFormat.JsonLines,
            "csv" => MonitorFormat.Csv,
            _ => throw new MonitorException(
                $"Unsupported format: '{value}'. Use json/jsonl or csv."),
        };

    private static MonitorLayout ParseLayout(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => MonitorLayout.Auto,
            "combined" => MonitorLayout.Combined,
            "per-group" => MonitorLayout.PerGroup,
            "single" => MonitorLayout.Single,
            _ => throw new MonitorException(
                $"Unsupported layout: '{value}'. " +
                "Use auto, combined, per-group, or single."),
        };

    private static void PrintWarnings(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"[WARN] {warning}");
        }
    }

    private static void PrintDryRun(
        MonitorPlan plan,
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout layout,
        MonitorDomainSettings domainSettings,
        int timer,
        int requestDelay,
        NotificationOptions notificationOptions)
    {
        Console.Out.WriteLine("Monitor dry-run");
        Console.Out.WriteLine($"Target: {plan.AllTargets.Count()}");
        Console.Out.WriteLine($"Domains: {string.Join(", ", plan.Domains)}");
        if (plan.Schedule is null)
        {
            Console.Out.WriteLine($"Timer: {timer}s");
        }
        Console.Out.WriteLine($"Request delay: {requestDelay}s");
        Console.Out.WriteLine($"Format: {(format == MonitorFormat.Csv ? "csv" : "jsonl")}");
        Console.Out.WriteLine($"Layout: {LayoutName(layout)}");
        Console.Out.WriteLine(
            $"Notifications: " + (notificationOptions.Enabled
                ? MonitorNotificationEventsParser.Format(
                    MonitorNotificationEventsParser.Parse(notificationOptions.On))
                : "off"));
        Console.Out.WriteLine();

        if (plan.Schedule is not null)
        {
            PrintSchedulePreview(plan, requestDelay);
        }

        foreach (var target in plan.AllTargets.OrderBy(item => item.Target.Key, StringComparer.Ordinal))
        {
            var rules = DescribeRules(target.Rule);
            Console.Out.WriteLine(
                $"  {target.Target.Key}" +
                (target.GroupId is null ? string.Empty : $" group={target.GroupId}") +
                (target.Quantity > 1 ? $" qty={target.Quantity}" : string.Empty) +
                (rules.Length == 0 ? string.Empty : $" rules={rules}"));
        }

        foreach (var group in plan.Groups)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(
                $"Group {group.Id} ({group.Name}): " +
                $"{(group.TotalMax is { } max ? $"total_max={MonitorObservationFactory.FormatAmount(max)} {group.Currency}" : "no total threshold")}");
            foreach (var item in group.Items)
            {
                Console.Out.WriteLine(
                    $"  {item.Target.Key} x{item.Quantity} " +
                    $"({DescribeRules(item.Rule)})");
            }
        }

        Console.Out.WriteLine();
        foreach (var (domain, location) in domainSettings.Locations.OrderBy(item => item.Key))
        {
            Console.Out.WriteLine(
                $"Delivery {domain}: {location.PostalCode} / {location.CountryCode}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine("Expected data files:");
        foreach (var file in MonitorOutput.DescribeFiles(paths, format, layout, plan))
        {
            Console.Out.WriteLine($"  {file}");
        }
        Console.Out.WriteLine($"State: {paths.StatePath}");
        Console.Out.WriteLine($"Lock: {paths.LockPath}");
        Console.Out.WriteLine();

        if (plan.Schedule is null)
        {
            var requestsPerHour = timer > 0
                ? plan.AllTargets.Count() * 3600.0 / timer
                : 0;
            Console.Out.WriteLine(
                $"Budget: {plan.AllTargets.Count()} requests/cycle, " +
                $"~{requestsPerHour:0.0} requests/hour with timer {timer}s.");
        }
        else
        {
            Console.Out.WriteLine(
                $"Adaptive budget: at most {plan.Schedule.MaxRequests} requests " +
                $"every {plan.Schedule.WindowSeconds}s.");
        }
    }

    private static void PrintSchedulePreview(
        MonitorPlan plan,
        int requestDelay)
    {
        var schedule = plan.Schedule!;
        Console.Out.WriteLine("Schedule: adaptive");
        Console.Out.WriteLine($"Window: {schedule.WindowSeconds}s");
        Console.Out.WriteLine($"Budget: {schedule.MaxRequests} requests");
        Console.Out.WriteLine(
            $"Default gap: {schedule.DefaultMinGapSeconds}-" +
            $"{schedule.DefaultMaxGapSeconds}s");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Units:");

        foreach (var unit in schedule.Units
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            Console.Out.WriteLine(
                $"  {unit.Id} cost={unit.Cost} " +
                $"gap={unit.MinGapSeconds}-{unit.MaxGapSeconds}s");
        }

        var start = DateTimeOffset.UtcNow;
        var state = new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state,
            requestDelay,
            plan.ScheduleHash ?? string.Empty,
            random: new Random(12_345));
        scheduler.Initialize(start);
        var now = start;
        var runs = new Dictionary<string, int>(StringComparer.Ordinal);
        var executions = 0;
        for (var guard = 0;
             guard < 100_000 && now < scheduler.WindowEnd;
             guard++)
        {
            var unit = scheduler.SelectNext(now);
            if (unit is null)
            {
                var delay = scheduler.NextDelay(now);
                if (delay <= TimeSpan.Zero)
                {
                    delay = TimeSpan.FromSeconds(1);
                }
                now += delay;
                continue;
            }

            executions++;
            runs[unit.Id] = runs.GetValueOrDefault(unit.Id) + 1;
            var duration = scheduler.EstimateDuration(unit);
            scheduler.RecordRun(
                unit,
                now,
                now + duration,
                unit.Cost);
            now += duration;
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"Window simulation: {executions} runs, " +
            $"{state.SpentRequests}/{schedule.MaxRequests} requests.");
        foreach (var unit in schedule.Units
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            Console.Out.WriteLine(
                $"  {unit.Id}: {runs.GetValueOrDefault(unit.Id)} runs");
        }
        if (state.SpentRequests < schedule.MaxRequests)
        {
            var remaining = schedule.MaxRequests - state.SpentRequests;
            Console.Out.WriteLine(
                $"Simulated remaining budget: {remaining} " +
                (remaining == 1 ? "request." : "requests."));
        }
        else
        {
            Console.Out.WriteLine("The budget is exhausted within the window.");
        }
        Console.Out.WriteLine();
    }

    private static string DescribeRules(MonitorRule rule)
    {
        var parts = new List<string>();
        if (rule.MaxPrice is { } max)
        {
            parts.Add($"max_price={MonitorObservationFactory.FormatAmount(max)}");
        }
        if (rule.DropPercent is { } drop)
        {
            parts.Add(
                $"drop_percent={drop.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} " +
                $"baseline={BaselineName(rule.Baseline)}");
        }
        if (rule.RisePercent is { } rise)
        {
            parts.Add(
                $"rise_percent={rise.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} " +
                $"baseline={BaselineName(rule.Baseline)}");
        }
        return string.Join(", ", parts);
    }

    private static string BaselineName(PriceBaseline baseline) =>
        baseline switch
        {
            PriceBaseline.Original => "original",
            PriceBaseline.FirstSeen => "first_seen",
            PriceBaseline.MaxSeen => "max_seen",
            PriceBaseline.LastSeen => "last_seen",
            PriceBaseline.Fixed => "fixed",
            _ => "unknown",
        };

    private static string LayoutName(MonitorLayout layout) =>
        layout switch
        {
            MonitorLayout.Combined => "combined",
            MonitorLayout.PerGroup => "per-group",
            MonitorLayout.Single => "single",
            _ => "auto",
        };

    private static void PrintResult(MonitorRunResult result)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_DETACHED"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var executions = result.Adaptive ? "units" : "cycles";
        Console.Out.WriteLine(
            $"Monitor finished: {result.Cycles} {executions}, " +
            $"{result.Observations} observations, {result.Errors} errors, " +
            $"{result.Alerts} alerts.");
        foreach (var file in result.DataFiles)
        {
            Console.Out.WriteLine($"Data: {file}");
        }
    }
}
