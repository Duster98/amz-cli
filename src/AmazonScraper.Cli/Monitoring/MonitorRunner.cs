using System.Diagnostics;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Monitoring;

public sealed record MonitorRunResult(
    int ExitCode,
    int Cycles,
    int Observations,
    int Errors,
    int Alerts,
    IReadOnlyList<string> DataFiles,
    bool Adaptive = false);

public sealed class MonitorRunner
{
    private MonitorPlan _plan;
    private readonly MonitorSettings _settings;
    private readonly MonitorPaths _paths;
    private readonly MonitorFormat _format;
    private readonly MonitorLayout _layout;
    private readonly int _timerSeconds;
    private readonly int _requestDelaySeconds;
    private readonly int _jitterSeconds;
    private readonly int _blockedCooldownSeconds;
    private readonly int _maxConsecutiveDomainFailures;
    private readonly bool _once;
    private readonly int? _cycles;
    private readonly bool _exitOnAlert;
    private readonly string? _htmlFile;
    private readonly bool _autoUpdateEnabled;
    private readonly MonitorLogger _logger;
    private readonly NotificationService _notifications;
    private readonly MonitorDomainSettings _domainSettings;
    private readonly Dictionary<string, DomainRuntime> _domains;
    private readonly Func<MonitorPlan>? _planProvider;
    private readonly HttpSettings _httpSettings;

    public MonitorRunner(
        MonitorPlan plan,
        MonitorSettings settings,
        MonitorPaths paths,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        int requestDelaySeconds,
        int jitterSeconds,
        int blockedCooldownSeconds,
        int maxConsecutiveDomainFailures,
        bool once,
        int? cycles,
        bool exitOnAlert,
        string? htmlFile,
        MonitorLogger logger,
        NotificationService notifications,
        Func<MonitorPlan>? planProvider = null,
        HttpSettings? httpSettings = null)
    {
        _plan = plan;
        _settings = settings;
        _paths = paths;
        _format = format;
        _layout = layout;
        _httpSettings = httpSettings ?? HttpSettingsStore.Default.Load();
        _timerSeconds = timerSeconds;
        _requestDelaySeconds = requestDelaySeconds;
        _jitterSeconds = jitterSeconds;
        _blockedCooldownSeconds = blockedCooldownSeconds;
        _maxConsecutiveDomainFailures = maxConsecutiveDomainFailures;
        _once = once;
        _cycles = cycles;
        _exitOnAlert = exitOnAlert;
        _htmlFile = htmlFile;
        _autoUpdateEnabled = !settings.NoAutoUpdate;
        _logger = logger;
        _notifications = notifications;
        _planProvider = planProvider;
        _domainSettings = MonitorDomainSettings.Build(
            plan.Domains.ToArray(),
            settings.Caps,
            settings.Countries,
            settings.DomainCookies,
            settings.Cookie);
        _domains = plan.Domains.ToDictionary(
            domain => domain,
            domain => new DomainRuntime
            {
                Profile = MarketplaceRegistry.Resolve(domain),
                Location = _domainSettings.Locations.GetValueOrDefault(domain),
                Cookie = _domainSettings.Cookies.GetValueOrDefault(domain),
            },
            StringComparer.Ordinal);
    }

    public async Task<MonitorRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var effectiveCycles = _once ? 1 : _cycles;
        var runId = BuildRunId();
        var html = _htmlFile is null
            ? null
            : await ReadHtmlAsync(_htmlFile, cancellationToken).ConfigureAwait(false);
        using var monitorLock = MonitorLock.Acquire(_paths.LockPath);
        var state = MonitorStateStore.Load(_paths.StatePath, _logger.Warn);
        state.Targets ??= [];
        state.Groups ??= [];
        state.Notifications ??= new MonitorNotificationState();
        state.Notifications.Domains ??= [];
        if (!string.Equals(state.ConfigHash, _plan.ConfigHash, StringComparison.Ordinal))
        {
            if (state.ConfigHash is not null)
            {
                _logger.Warn(
                    "Configuration changed: alert conditions and cooldowns reset.");
            }

            foreach (var target in state.Targets.Values)
            {
                target.Rules.Clear();
            }
            foreach (var group in state.Groups.Values)
            {
                group.Rules.Clear();
            }

            state.ConfigHash = _plan.ConfigHash;
        }

        await using var output = MonitorOutput.Open(
            _paths,
            _format,
            _layout,
            _plan);
        var parsers = BuildParsers(_plan.Domains);
        var pacer = new RequestPacer(_htmlFile is null ? _requestDelaySeconds : 0);
        var registry = new MonitorRegistry();
        var registryId = runId;
        var startupKind = Environment.GetEnvironmentVariable(
            "AMZ_MONITOR_STARTUP");
        var isStartup = !string.IsNullOrWhiteSpace(startupKind);
        var registryEntry = CreateRegistryEntry(runId);
        try
        {
            var removed = registry.Reconcile(registryEntry);
            if (removed > 0)
            {
                _logger.Info(
                    removed == 1
                        ? "Removed 1 stale entry from the previous monitor."
                        : $"Removed {removed} stale entries from the previous monitor.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"Could not clean up stale monitor entries: {ex.Message}");
        }

        try
        {
            registry.Register(registryEntry);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not register the monitor: {ex.Message}");
        }

        if (isStartup)
        {
            try
            {
                MonitorPidInfo.Save(_paths.PidPath, registryEntry);
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    $"Could not write the startup pidfile: {ex.Message}");
            }
        }

        var cycleCount = 0;
        var observations = 0;
        var errors = 0;
        var alerts = 0;
        var hadErrors = false;
        var hadAlerts = false;
        int exitCode;

        try
        {
            if (_plan.Schedule is not null
                && !_once
                && _cycles is null
                && _htmlFile is null)
            {
                return await RunAdaptiveAsync(
                        state,
                        output,
                        parsers,
                        pacer,
                        runId,
                        html,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryScheduleAutoUpdate();
                if (!TryRefreshPlan(
                        out var refreshError,
                        out var configChanged))
                {
                    hadErrors = true;
                    _logger.Error($"Monitor paused: {refreshError}");
                    if (_once)
                    {
                        return new MonitorRunResult(
                            1,
                            cycleCount,
                            observations,
                            errors,
                            alerts,
                            output.CreatedFiles);
                    }
                    await Task.Delay(
                            TimeSpan.FromSeconds(Math.Max(10, _timerSeconds)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                if (configChanged)
                {
                    ResetRuleState(state);
                    state.ConfigHash = _plan.ConfigHash;
                    _logger.Warn(
                        "Monitor binding changed: alert conditions and " +
                        "cooldowns reset.");
                }
                parsers = BuildParsers(_plan.Domains);
                cycleCount++;
                var cycleStart = DateTimeOffset.UtcNow;
                var cycleAlerts = 0;
                var cycleObservations = 0;
                var cycleErrors = 0;
                var observationsByKey = new Dictionary<string, MonitorItemObservation>(
                    StringComparer.Ordinal);
                var skippedDomains = new HashSet<string>(StringComparer.Ordinal);
                var cycleErrorEvents = new List<MonitorErrorEvent>();

                _logger.Info(
                    $"Cycle {cycleCount} started (run {runId}, " +
                    $"{_plan.AllTargets.Count()} targets, timer {_timerSeconds}s).");

                foreach (var target in _plan.AllTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var runtime = _domains[target.Target.Domain];
                    var now = DateTimeOffset.UtcNow;

                    if (runtime.Disabled)
                    {
                        MarkSkipped(
                            skippedDomains,
                            target.Target.Domain,
                            "domain disabled after consecutive errors");
                        hadErrors = true;
                        continue;
                    }
                    if (now < runtime.RetryAfter)
                    {
                        MarkSkipped(
                            skippedDomains,
                            target.Target.Domain,
                            $"domain in cooldown until {runtime.RetryAfter:yyyy-MM-ddTHH:mm:ssZ}");
                        hadErrors = true;
                        continue;
                    }

                    try
                    {
                        string page;
                        if (html is not null)
                        {
                            page = html;
                        }
                        else
                        {
                            var client = await EnsureClientAsync(runtime, cancellationToken)
                                .ConfigureAwait(false);
                            await pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
                            try
                            {
                                page = await client.DetailAsync(
                                        target.Target.Asin,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                pacer.MarkCompleted();
                            }
                        }

                        var detail = parsers[target.Target.Domain].Parse(
                            page,
                            target.Target.Asin,
                            runtime.Location?.CountryCode,
                            runtime.Location?.PostalCode);
                        var observation = MonitorObservationFactory.FromDetail(
                            detail,
                            target,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        var evaluation = MonitorRuleEvaluator.EvaluateItem(
                            target,
                            observation,
                            state,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        observation.TriggeredRules.AddRange(evaluation.TriggeredRules);
                        observationsByKey[target.Target.Key] = observation;
                        await output.WriteItemAsync(observation, cancellationToken)
                            .ConfigureAwait(false);

                        cycleObservations++;
                        observations++;
                        ResetDomainFailures(runtime);
                        foreach (var alert in evaluation.Alerts)
                        {
                            await WriteAlertAsync(output, alert, cancellationToken)
                                .ConfigureAwait(false);
                            _logger.Alert(alert.Message);
                            var payload = MonitorNotificationPayloadFactory.ItemAlert(
                                alert,
                                observation,
                                DateTimeOffset.UtcNow);
                            await _notifications.NotifyAlertAsync(
                                    alert,
                                    payload,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            cycleAlerts++;
                            alerts++;
                            hadAlerts = true;
                        }

                        _logger.Info(
                            $"OK [{target.Target.Domain}] {target.Target.Asin} " +
                            $"{observation.Price} {observation.Discount}" +
                            (string.IsNullOrWhiteSpace(observation.Deal)
                                ? string.Empty
                                : $" deal={observation.Deal}") +
                            (string.IsNullOrWhiteSpace(observation.DealEndsOn)
                                ? string.Empty
                                : $" deal_ends={observation.DealEndsOn}") +
                            (observation.TriggeredRules.Count == 0
                                ? string.Empty
                                : $" rules={string.Join(',', observation.TriggeredRules)}"));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (AmazonBlockedException ex)
                    {
                        var observation = MonitorObservationFactory.FromError(
                            target,
                            ex,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        observationsByKey[target.Target.Key] = observation;
                        await output.WriteItemAsync(observation, cancellationToken)
                            .ConfigureAwait(false);
                        cycleObservations++;
                        observations++;
                        cycleErrors++;
                        errors++;
                        hadErrors = true;
                        var reason = $"block: {ex.Message}";
                        var disabled = RecordDomainFailure(runtime, reason);
                        if (disabled)
                        {
                            await _notifications.NotifyDomainDisabledAsync(
                                    target.Target.Domain,
                                    reason,
                                    state,
                                    DateTimeOffset.UtcNow,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await _notifications.NotifyBlockAsync(
                                    target.Target.Domain,
                                    reason,
                                    state,
                                    DateTimeOffset.UtcNow,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        _logger.Warn(
                            $"BLOCKED [{target.Target.Domain}] {target.Target.Asin}: " +
                            $"{ex.Message}");
                    }
                    catch (DomainSetupException ex)
                    {
                        var observation = MonitorObservationFactory.FromError(
                            target,
                            ex,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        observationsByKey[target.Target.Key] = observation;
                        await output.WriteItemAsync(observation, cancellationToken)
                            .ConfigureAwait(false);
                        cycleObservations++;
                        observations++;
                        cycleErrors++;
                        errors++;
                        hadErrors = true;
                        var reason = ex.Message;
                        var disabled = RecordDomainFailure(runtime, reason);
                        if (disabled)
                        {
                            await _notifications.NotifyDomainDisabledAsync(
                                    target.Target.Domain,
                                    reason,
                                    state,
                                    DateTimeOffset.UtcNow,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await _notifications.NotifyBlockAsync(
                                    target.Target.Domain,
                                    reason,
                                    state,
                                    DateTimeOffset.UtcNow,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        _logger.Warn(
                            $"SETUP [{target.Target.Domain}] {target.Target.Asin}: " +
                            $"{ex.Message}");
                    }
                    catch (AmazonNotFoundException ex)
                    {
                        var observation = MonitorObservationFactory.FromError(
                            target,
                            ex,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        observationsByKey[target.Target.Key] = observation;
                        await output.WriteItemAsync(observation, cancellationToken)
                            .ConfigureAwait(false);
                        cycleObservations++;
                        observations++;
                        cycleErrors++;
                        errors++;
                        hadErrors = true;
                        cycleErrorEvents.Add(new MonitorErrorEvent(
                            target.Target.Domain,
                            target.Target.Asin,
                            "not_found",
                            ex.Message));
                        ResetDomainFailures(runtime);
                        _logger.Warn(
                            $"NOT FOUND [{target.Target.Domain}] {target.Target.Asin}: " +
                            $"{ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        var observation = MonitorObservationFactory.FromError(
                            target,
                            ex,
                            runId,
                            cycleCount,
                            DateTimeOffset.UtcNow);
                        observationsByKey[target.Target.Key] = observation;
                        await output.WriteItemAsync(observation, cancellationToken)
                            .ConfigureAwait(false);
                        cycleObservations++;
                        observations++;
                        cycleErrors++;
                        errors++;
                        hadErrors = true;
                        cycleErrorEvents.Add(new MonitorErrorEvent(
                            target.Target.Domain,
                            target.Target.Asin,
                            "error",
                            ex.Message));
                        _logger.Warn(
                            $"ERROR [{target.Target.Domain}] {target.Target.Asin}: " +
                            $"{ex.Message}");
                    }
                }

                await _notifications.NotifyErrorDigestAsync(
                        cycleErrorEvents,
                        state,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var group in _plan.Groups)
                {
                    var observation = MonitorObservationFactory.FromGroup(
                        group,
                        observationsByKey,
                        runId,
                        cycleCount,
                        DateTimeOffset.UtcNow);
                    var evaluation = MonitorRuleEvaluator.EvaluateGroup(
                        group,
                        observation,
                        state,
                        runId,
                        cycleCount,
                        DateTimeOffset.UtcNow);
                    observation.TriggeredRules.AddRange(evaluation.TriggeredRules);
                    await output.WriteGroupAsync(observation, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var alert in evaluation.Alerts)
                    {
                        await WriteAlertAsync(output, alert, cancellationToken)
                            .ConfigureAwait(false);
                        _logger.Alert(alert.Message);
                        var payload = MonitorNotificationPayloadFactory.GroupAlert(
                            alert,
                            observation,
                            observationsByKey,
                            DateTimeOffset.UtcNow);
                        await _notifications.NotifyAlertAsync(
                                alert,
                                payload,
                                cancellationToken)
                            .ConfigureAwait(false);
                        cycleAlerts++;
                        alerts++;
                        hadAlerts = true;
                    }

                    _logger.Info(
                        $"GRUPPO [{group.Id}] " +
                        (observation.Total is { } total
                            ? $"{MonitorObservationFactory.FormatAmount(total)} {group.Currency}"
                            : "total unavailable") +
                        (observation.TotalMax is { } max
                            ? $" / {MonitorObservationFactory.FormatAmount(max)} {group.Currency}"
                            : string.Empty) +
                        (observation.Complete ? string.Empty : " (incomplete)") +
                        (observation.TriggeredRules.Count == 0
                            ? string.Empty
                            : $" rules={string.Join(',', observation.TriggeredRules)}"));
                }

                try
                {
                    await MonitorStateStore.SaveAsync(
                            _paths.StatePath,
                            state,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Could not save state '{_paths.StatePath}': {ex.Message}");
                    await _notifications.NotifyFatalAsync(
                            $"Could not save state: {ex.Message}",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    return new MonitorRunResult(
                        1,
                        cycleCount,
                        observations,
                        errors,
                        alerts,
                        output.CreatedFiles);
                }

                var elapsed = DateTimeOffset.UtcNow - cycleStart;
                _logger.Info(
                    $"Cycle {cycleCount} completed in {elapsed.TotalSeconds:0.0}s: " +
                    $"{cycleObservations} observations, {cycleErrors} errors, " +
                    $"{cycleAlerts} alerts.");

                if (effectiveCycles is { } maxCycles && cycleCount >= maxCycles)
                {
                    break;
                }
                if (_domains.Values.All(domain => domain.Disabled))
                {
                    _logger.Error("All domains have been disabled: exiting.");
                    await _notifications.NotifyFatalAsync(
                            "All domains have been disabled.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    return new MonitorRunResult(
                        1,
                        cycleCount,
                        observations,
                        errors,
                        alerts,
                        output.CreatedFiles);
                }

                var wait = ComputeCycleWait(cycleStart, _timerSeconds, _jitterSeconds);
                var nextRun = DateTimeOffset.UtcNow + wait;
                var activeDomains = _domains.Values
                    .Where(domain => !domain.Disabled)
                    .ToArray();
                if (activeDomains.Length > 0
                    && activeDomains.All(domain => domain.RetryAfter > nextRun))
                {
                    nextRun = activeDomains.Min(domain => domain.RetryAfter);
                    wait = nextRun - DateTimeOffset.UtcNow;
                }

                if (wait > TimeSpan.Zero)
                {
                    _logger.Info($"Next cycle at {nextRun:yyyy-MM-ddTHH:mm:ssZ}.");
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
            }

            exitCode = _exitOnAlert && hadAlerts
                ? 3
                : hadErrors
                    ? 2
                    : 0;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Stop requested: shutting down the monitor.");
            try
            {
                await MonitorStateStore.SaveAsync(
                        _paths.StatePath,
                        state,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not save state during shutdown: {ex.Message}");
            }

            exitCode = 130;
        }
        catch (Exception ex)
        {
            await _notifications.NotifyFatalAsync(
                    $"Monitor error: {ex.Message}",
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            registry.Unregister(registryId);
            if (isStartup)
            {
                TryDeleteOwnPidFile();
            }

            foreach (var runtime in _domains.Values)
            {
                runtime.Client?.Dispose();
                runtime.Client = null;
            }
        }

        _logger.Summary(
            $"Monitor finished: {cycleCount} cycles, {observations} observations, " +
            $"{errors} errors, {alerts} alerts.");
        return new MonitorRunResult(
            exitCode,
            cycleCount,
            observations,
            errors,
            alerts,
            output.CreatedFiles);
    }

    private sealed record AdaptiveTargetResult(
        MonitorItemObservation? Observation,
        int Alerts,
        int Errors,
        int RequestCost,
        bool Skipped,
        IReadOnlyList<MonitorErrorEvent> ErrorEvents);

    private async Task<MonitorRunResult> RunAdaptiveAsync(
        MonitorState state,
        MonitorOutput output,
        IReadOnlyDictionary<string, DetailParser> parsers,
        RequestPacer pacer,
        string runId,
        string? html,
        CancellationToken cancellationToken)
    {
        var schedule = _plan.Schedule
            ?? throw new MonitorException("Adaptive plan missing.");
        state.Schedule ??= new MonitorScheduleState();
        var scheduler = new MonitorScheduler(
            schedule,
            state.Schedule,
            _requestDelaySeconds,
            _plan.ScheduleHash ?? string.Empty,
            _logger.Warn);
        scheduler.Initialize(DateTimeOffset.UtcNow);

        _logger.Info(
            $"Adaptive schedule: window {schedule.WindowSeconds}s, " +
            $"budget {schedule.MaxRequests} requests, " +
            $"gap {schedule.DefaultMinGapSeconds}-{schedule.DefaultMaxGapSeconds}s, " +
            $"{schedule.Units.Count} units.");

        var executions = 0;
        var observations = 0;
        var errors = 0;
        var alerts = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryScheduleAutoUpdate();

                var now = DateTimeOffset.UtcNow;
                var unit = scheduler.SelectNext(now);
                if (unit is null)
                {
                    if (_domains.Values.All(domain => domain.Disabled))
                    {
                        _logger.Error(
                            "All domains have been disabled: exiting.");
                        await _notifications.NotifyFatalAsync(
                                "All domains have been disabled.",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await SaveAdaptiveStateAsync(state, CancellationToken.None)
                            .ConfigureAwait(false);
                        return AdaptiveResult(
                            1,
                            executions,
                            observations,
                            errors,
                            alerts,
                            output);
                    }

                    var delay = scheduler.NextDelay(now);
                    if (delay > TimeSpan.FromSeconds(60))
                    {
                        delay = TimeSpan.FromSeconds(60);
                    }
                    if (delay <= TimeSpan.Zero)
                    {
                        delay = TimeSpan.FromMilliseconds(500);
                    }
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var unavailable = AdaptiveUnitDelay(unit, now);
                if (unavailable is { } postpone)
                {
                    scheduler.Postpone(unit, now, postpone);
                    _logger.Warn(
                        $"Unit {unit.Describe()} postponed by " +
                        $"{postpone.TotalSeconds:0}s: domain unavailable.");
                    continue;
                }

                var started = DateTimeOffset.UtcNow;
                var cycle = scheduler.GetRuns(unit.Id) + 1;
                var requestCost = 0;
                var unitAlerts = 0;
                var unitErrors = 0;
                var errorEvents = new List<MonitorErrorEvent>();

                if (unit.Kind == MonitorUnitKind.Item)
                {
                    var result = await ExecuteAdaptiveTargetAsync(
                            unit.Item!,
                            cycle,
                            html,
                            output,
                            parsers,
                            state,
                            pacer,
                            runId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    requestCost += result.RequestCost;
                    if (result.RequestCost > 0)
                    {
                        scheduler.ChargeRequests(result.RequestCost);
                    }
                    unitAlerts += result.Alerts;
                    unitErrors += result.Errors;
                    errorEvents.AddRange(result.ErrorEvents);
                    if (result.Observation is not null)
                    {
                        observations++;
                    }
                    if (result.Skipped)
                    {
                        scheduler.Postpone(
                            unit,
                            DateTimeOffset.UtcNow,
                            TimeSpan.FromSeconds(30));
                        continue;
                    }
                }
                else
                {
                    var group = unit.Group
                        ?? throw new MonitorException(
                            "Group unit without a definition.");
                    var observationsByKey = new Dictionary<string, MonitorItemObservation>(
                        StringComparer.Ordinal);
                    foreach (var target in group.Items)
                    {
                        var result = await ExecuteAdaptiveTargetAsync(
                                target,
                                cycle,
                                html,
                                output,
                                parsers,
                                state,
                                pacer,
                                runId,
                                cancellationToken)
                            .ConfigureAwait(false);
                        requestCost += result.RequestCost;
                        if (result.RequestCost > 0)
                        {
                            scheduler.ChargeRequests(result.RequestCost);
                        }
                        unitAlerts += result.Alerts;
                        unitErrors += result.Errors;
                        errorEvents.AddRange(result.ErrorEvents);
                        if (result.Observation is not null)
                        {
                            observationsByKey[target.Target.Key] = result.Observation;
                            observations++;
                        }
                    }

                    var groupResult = await ExecuteAdaptiveGroupAsync(
                            group,
                            observationsByKey,
                            cycle,
                            output,
                            state,
                            runId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    unitAlerts += groupResult.Alerts;
                }

                var ended = DateTimeOffset.UtcNow;
                scheduler.RecordRun(
                    unit,
                    started,
                    ended,
                    requestCost,
                    chargeRequests: false);
                executions++;
                alerts += unitAlerts;
                errors += unitErrors;

                if (errorEvents.Count > 0)
                {
                    await _notifications.NotifyErrorDigestAsync(
                            errorEvents,
                            state,
                            DateTimeOffset.UtcNow,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!await SaveAdaptiveStateAsync(state, cancellationToken)
                        .ConfigureAwait(false))
                {
                    await _notifications.NotifyFatalAsync(
                            "Could not save the adaptive state.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    return AdaptiveResult(
                        1,
                        executions,
                        observations,
                        errors,
                        alerts,
                        output);
                }

                _logger.Info(
                    $"UNIT {unit.Describe()} completed: " +
                    $"{requestCost} requests, " +
                    $"{state.Schedule.SpentRequests}/{schedule.MaxRequests} in budget, " +
                    $"{unitAlerts} alerts.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Stop requested: shutting down the monitor.");
            await SaveAdaptiveStateAsync(state, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.Summary(
                $"Monitor finished: {executions} units, {observations} observations, " +
                $"{errors} errors, {alerts} alerts.");
            return AdaptiveResult(
                130,
                executions,
                observations,
                errors,
                alerts,
                output);
        }
    }

    private MonitorRunResult AdaptiveResult(
        int exitCode,
        int executions,
        int observations,
        int errors,
        int alerts,
        MonitorOutput output)
    {
        _logger.Summary(
            $"Monitor finished: {executions} units, {observations} observations, " +
            $"{errors} errors, {alerts} alerts.");
        return new MonitorRunResult(
            exitCode,
            executions,
            observations,
            errors,
            alerts,
            output.CreatedFiles,
            Adaptive: true);
    }

    private async Task<bool> SaveAdaptiveStateAsync(
        MonitorState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await MonitorStateStore.SaveAsync(
                    _paths.StatePath,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Could not save state '{_paths.StatePath}': {ex.Message}");
            return false;
        }
    }

    private TimeSpan? AdaptiveUnitDelay(
        MonitorUnitPlan unit,
        DateTimeOffset now)
    {
        if (unit.Kind == MonitorUnitKind.Item)
        {
            return DomainDelay(unit.Item!.Target.Domain, now);
        }

        var group = unit.Group
            ?? throw new MonitorException("Group unit without a definition.");
        TimeSpan? minDelay = null;
        foreach (var item in group.Items)
        {
            var delay = DomainDelay(item.Target.Domain, now);
            if (delay is null)
            {
                return null;
            }
            if (minDelay is null || delay < minDelay)
            {
                minDelay = delay;
            }
        }

        return minDelay ?? TimeSpan.FromSeconds(60);
    }

    private TimeSpan? DomainDelay(string domain, DateTimeOffset now)
    {
        var runtime = _domains[domain];
        if (runtime.Disabled)
        {
            return TimeSpan.FromSeconds(Math.Max(60, _blockedCooldownSeconds));
        }
        if (now < runtime.RetryAfter)
        {
            var delay = runtime.RetryAfter - now;
            return delay > TimeSpan.Zero
                ? delay
                : TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private async Task<AdaptiveTargetResult> ExecuteAdaptiveTargetAsync(
        MonitorTargetPlan target,
        int cycle,
        string? html,
        MonitorOutput output,
        IReadOnlyDictionary<string, DetailParser> parsers,
        MonitorState state,
        RequestPacer pacer,
        string runId,
        CancellationToken cancellationToken)
    {
        var runtime = _domains[target.Target.Domain];
        var now = DateTimeOffset.UtcNow;
        if (runtime.Disabled || now < runtime.RetryAfter)
        {
            return new AdaptiveTargetResult(null, 0, 0, 0, true, []);
        }

        try
        {
            string page;
            if (html is not null)
            {
                page = html;
            }
            else
            {
                var client = await EnsureClientAsync(runtime, cancellationToken)
                    .ConfigureAwait(false);
                await pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    page = await client.DetailAsync(
                            target.Target.Asin,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    pacer.MarkCompleted();
                }
            }

            var detail = parsers[target.Target.Domain].Parse(
                page,
                target.Target.Asin,
                runtime.Location?.CountryCode,
                runtime.Location?.PostalCode);
            var observation = MonitorObservationFactory.FromDetail(
                detail,
                target,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            var evaluation = MonitorRuleEvaluator.EvaluateItem(
                target,
                observation,
                state,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            observation.TriggeredRules.AddRange(evaluation.TriggeredRules);
            await output.WriteItemAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            ResetDomainFailures(runtime);

            var alerts = 0;
            foreach (var alert in evaluation.Alerts)
            {
                await WriteAlertAsync(output, alert, cancellationToken)
                    .ConfigureAwait(false);
                _logger.Alert(alert.Message);
                var payload = MonitorNotificationPayloadFactory.ItemAlert(
                    alert,
                    observation,
                    DateTimeOffset.UtcNow);
                await _notifications.NotifyAlertAsync(
                        alert,
                        payload,
                        cancellationToken)
                    .ConfigureAwait(false);
                alerts++;
            }

            _logger.Info(
                $"OK [{target.Target.Domain}] {target.Target.Asin} " +
                $"{observation.Price} {observation.Discount}" +
                (string.IsNullOrWhiteSpace(observation.Deal)
                    ? string.Empty
                    : $" deal={observation.Deal}") +
                (observation.TriggeredRules.Count == 0
                    ? string.Empty
                    : $" rules={string.Join(',', observation.TriggeredRules)}"));
            return new AdaptiveTargetResult(
                observation,
                alerts,
                0,
                1,
                false,
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonBlockedException ex)
        {
            var observation = MonitorObservationFactory.FromError(
                target,
                ex,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            await output.WriteItemAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            var reason = $"block: {ex.Message}";
            var disabled = RecordDomainFailure(runtime, reason);
            if (disabled)
            {
                await _notifications.NotifyDomainDisabledAsync(
                        target.Target.Domain,
                        reason,
                        state,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _notifications.NotifyBlockAsync(
                        target.Target.Domain,
                        reason,
                        state,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            _logger.Warn(
                $"BLOCKED [{target.Target.Domain}] {target.Target.Asin}: " +
                $"{ex.Message}");
            return new AdaptiveTargetResult(
                observation,
                0,
                1,
                1,
                false,
                []);
        }
        catch (DomainSetupException ex)
        {
            var observation = MonitorObservationFactory.FromError(
                target,
                ex,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            await output.WriteItemAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            var reason = ex.Message;
            var disabled = RecordDomainFailure(runtime, reason);
            if (disabled)
            {
                await _notifications.NotifyDomainDisabledAsync(
                        target.Target.Domain,
                        reason,
                        state,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _notifications.NotifyBlockAsync(
                        target.Target.Domain,
                        reason,
                        state,
                        DateTimeOffset.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            _logger.Warn(
                $"SETUP [{target.Target.Domain}] {target.Target.Asin}: " +
                $"{ex.Message}");
            return new AdaptiveTargetResult(
                observation,
                0,
                1,
                1,
                false,
                []);
        }
        catch (AmazonNotFoundException ex)
        {
            var observation = MonitorObservationFactory.FromError(
                target,
                ex,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            await output.WriteItemAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            ResetDomainFailures(runtime);
            var errorEvent = new MonitorErrorEvent(
                target.Target.Domain,
                target.Target.Asin,
                "not_found",
                ex.Message);
            _logger.Warn(
                $"NOT FOUND [{target.Target.Domain}] {target.Target.Asin}: " +
                $"{ex.Message}");
            return new AdaptiveTargetResult(
                observation,
                0,
                1,
                1,
                false,
                [errorEvent]);
        }
        catch (Exception ex)
        {
            var observation = MonitorObservationFactory.FromError(
                target,
                ex,
                runId,
                cycle,
                DateTimeOffset.UtcNow);
            await output.WriteItemAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            var errorEvent = new MonitorErrorEvent(
                target.Target.Domain,
                target.Target.Asin,
                "error",
                ex.Message);
            _logger.Warn(
                $"ERROR [{target.Target.Domain}] {target.Target.Asin}: " +
                $"{ex.Message}");
            return new AdaptiveTargetResult(
                observation,
                0,
                1,
                1,
                false,
                [errorEvent]);
        }
    }

    private async Task<(MonitorGroupObservation Observation, int Alerts)>
        ExecuteAdaptiveGroupAsync(
            MonitorGroupPlan group,
            IReadOnlyDictionary<string, MonitorItemObservation> observations,
            int cycle,
            MonitorOutput output,
            MonitorState state,
            string runId,
            CancellationToken cancellationToken)
    {
        var observation = MonitorObservationFactory.FromGroup(
            group,
            observations,
            runId,
            cycle,
            DateTimeOffset.UtcNow);
        var evaluation = MonitorRuleEvaluator.EvaluateGroup(
            group,
            observation,
            state,
            runId,
            cycle,
            DateTimeOffset.UtcNow);
        observation.TriggeredRules.AddRange(evaluation.TriggeredRules);
        await output.WriteGroupAsync(observation, cancellationToken)
            .ConfigureAwait(false);

        var alerts = 0;
        foreach (var alert in evaluation.Alerts)
        {
            await WriteAlertAsync(output, alert, cancellationToken)
                .ConfigureAwait(false);
            _logger.Alert(alert.Message);
            var payload = MonitorNotificationPayloadFactory.GroupAlert(
                alert,
                observation,
                observations,
                DateTimeOffset.UtcNow);
            await _notifications.NotifyAlertAsync(
                    alert,
                    payload,
                    cancellationToken)
                .ConfigureAwait(false);
            alerts++;
        }

        _logger.Info(
            $"GRUPPO [{group.Id}] " +
            (observation.Total is { } total
                ? $"{MonitorObservationFactory.FormatAmount(total)} {group.Currency}"
                : "total unavailable") +
            (observation.TotalMax is { } max
                ? $" / {MonitorObservationFactory.FormatAmount(max)} {group.Currency}"
                : string.Empty) +
            (observation.Complete ? string.Empty : " (incomplete)") +
            (observation.TriggeredRules.Count == 0
                ? string.Empty
                : $" rules={string.Join(',', observation.TriggeredRules)}"));

        return (observation, alerts);
    }

    private void TryScheduleAutoUpdate()
    {
        if (!_autoUpdateEnabled
            || _once
            || _cycles is not null
            || _htmlFile is not null)
        {
            return;
        }

        var startup = Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP");
        var detached = Environment.GetEnvironmentVariable("AMZ_MONITOR_DETACHED");
        if (string.IsNullOrWhiteSpace(startup)
            && !string.Equals(detached, "1", StringComparison.Ordinal))
        {
            // A foreground monitor cannot be restarted automatically.
            return;
        }

        AutoUpdateLauncher.TryScheduleMonitor();
    }

    private bool TryRefreshPlan(
        out string? error,
        out bool configChanged)
    {
        error = null;
        configChanged = false;
        if (_planProvider is null)
        {
            return true;
        }

        try
        {
            var previousHash = _plan.ConfigHash;
            var next = _planProvider();
            _plan = next;
            configChanged = !string.Equals(
                previousHash,
                next.ConfigHash,
                StringComparison.Ordinal);
            foreach (var domain in next.Domains)
            {
                EnsureDomainRuntime(domain);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ResetRuleState(MonitorState state)
    {
        foreach (var target in state.Targets.Values)
        {
            target.Rules.Clear();
        }
        foreach (var group in state.Groups.Values)
        {
            group.Rules.Clear();
        }
    }

    private Dictionary<string, DetailParser> BuildParsers(
        IEnumerable<string> domains) =>
        domains.ToDictionary(
            domain => domain,
            domain => new DetailParser(MarketplaceRegistry.Resolve(domain)),
            StringComparer.Ordinal);

    private void EnsureDomainRuntime(string domain)
    {
        if (_domains.ContainsKey(domain))
        {
            return;
        }

        _domains[domain] = new DomainRuntime
        {
            Profile = MarketplaceRegistry.Resolve(domain),
            Location = _domainSettings.Locations.GetValueOrDefault(domain),
            Cookie = _domainSettings.Cookies.GetValueOrDefault(domain),
        };
    }

    private MonitorPidInfo CreateRegistryEntry(string runId)
    {
        DateTimeOffset? processStarted = null;
        try
        {
            processStarted = new DateTimeOffset(
                Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                TimeSpan.Zero);
        }
        catch (Exception)
        {
            // Not available on every platform.
        }

        return new MonitorPidInfo
        {
            Id = runId,
            RunId = runId,
            BindingId = EmptyToNull(_settings.Binding)
                ?? EmptyToNull(Environment.GetEnvironmentVariable(
                    "AMZ_MONITOR_BINDING_ID")),
            Pid = Environment.ProcessId,
            ProcessStartedAt = processStarted,
            StartedAt = MonitorObservationFactory.FormatTimestamp(DateTimeOffset.UtcNow),
            Mode = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP"))
                ? "startup"
                : string.Equals(
                    Environment.GetEnvironmentVariable("AMZ_MONITOR_DETACHED"),
                    "1",
                    StringComparison.Ordinal)
                    ? "detach"
                    : "foreground",
            Startup = EmptyToNull(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP")),
            StartupId = EmptyToNull(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP_ID")),
            StartupName = EmptyToNull(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP_NAME")),
            Executable = Environment.ProcessPath,
            PrefixArguments = ResolvePrefixArguments(),
            Arguments = Environment.GetCommandLineArgs().Skip(1).ToList(),
            WorkingDirectory = Environment.CurrentDirectory,
            Domains = _plan.Domains.ToList(),
            Targets = _plan.AllTargets.Count(),
            TimerSeconds = _timerSeconds,
            RuntimeHash = MonitorRuntimeSignature.Compute(
                _timerSeconds,
                _requestDelaySeconds,
                _jitterSeconds,
                _format,
                _layout,
                _settings.Notify,
                (_settings.NotifyOn ?? string.Empty).Split(
                    [',', ';', ' '],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries),
                _settings.NotifyCooldown,
                _settings.NoAutoUpdate,
                _paths.SavePath,
                _paths.BaseName),
            ScheduleMode = _plan.Schedule is null ? null : "adaptive",
            WindowSeconds = _plan.Schedule?.WindowSeconds,
            MaxRequests = _plan.Schedule?.MaxRequests,
            MinGapSeconds = _plan.Schedule?.DefaultMinGapSeconds,
            MaxGapSeconds = _plan.Schedule?.DefaultMaxGapSeconds,
            SavePath = _paths.SavePath,
            SaveFile = _paths.BaseName,
            PidFile = _paths.PidPath,
            LogPath = _settings.LogPath is null
                ? _paths.LogPath
                : Path.GetFullPath(_settings.LogPath),
            DataFiles = MonitorOutput.DescribeFiles(
                    _paths,
                    _format,
                    _layout,
                    _plan)
                .ToList(),
        };
    }

    private static List<string> ResolvePrefixArguments()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)
            || !string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var assembly = typeof(MonitorRunner).Assembly.Location;
        return string.IsNullOrWhiteSpace(assembly)
            ? []
            : ["exec", Path.GetFullPath(assembly)];
    }

    private void TryDeleteOwnPidFile()
    {
        try
        {
            var info = MonitorPidInfo.TryLoad(_paths.PidPath);
            if (info is not null && info.Pid == Environment.ProcessId)
            {
                File.Delete(_paths.PidPath);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<AmazonClient> EnsureClientAsync(
        DomainRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (runtime.Client is not null)
        {
            return runtime.Client;
        }

        var options = AmazonOptionsFactory.Create(
            _settings,
            runtime.Profile,
            saved: _httpSettings);
        if (!string.IsNullOrWhiteSpace(runtime.Cookie))
        {
            options = options.WithCookieHeader(runtime.Cookie);
        }

        var client = new AmazonClient(runtime.Profile, options);
        if (runtime.Location is { } location)
        {
            var first = _plan.FirstTargetFor(runtime.Profile.Id)
                ?? throw new MonitorException(
                    $"No target for domain '{runtime.Profile.Id}'.");
            try
            {
                var ok = await client.SetDeliveryLocationAsync(
                        runtime.Profile.DetailUrl(first.Target.Asin),
                        location.PostalCode,
                        location.CountryCode,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    client.Dispose();
                    throw new DomainSetupException(
                        $"delivery setup failed for {runtime.Profile.Id} " +
                        $"({location.PostalCode} / {location.CountryCode})");
                }
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw;
            }
            catch (DomainSetupException)
            {
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new DomainSetupException(
                    $"delivery setup failed for {runtime.Profile.Id}: {ex.Message}");
            }
        }

        runtime.Client = client;
        return client;
    }

    private bool RecordDomainFailure(DomainRuntime runtime, string reason)
    {
        runtime.Client?.Dispose();
        runtime.Client = null;
        runtime.ConsecutiveFailures++;
        if (runtime.ConsecutiveFailures >= _maxConsecutiveDomainFailures)
        {
            runtime.Disabled = true;
            _logger.Error(
                $"Domain {runtime.Profile.Id} disabled after " +
                $"{runtime.ConsecutiveFailures} consecutive errors ({reason}).");
            return true;
        }

        runtime.RetryAfter = DateTimeOffset.UtcNow.AddSeconds(_blockedCooldownSeconds);
        _logger.Warn(
            $"Domain {runtime.Profile.Id} in cooldown for {_blockedCooldownSeconds}s " +
            $"({reason}).");
        return false;
    }

    private void ResetDomainFailures(DomainRuntime runtime)
    {
        runtime.ConsecutiveFailures = 0;
        runtime.RetryAfter = DateTimeOffset.MinValue;
    }

    private void MarkSkipped(
        HashSet<string> skipped,
        string domain,
        string reason)
    {
        if (skipped.Add(domain))
        {
            _logger.Warn($"Skipping domain {domain}: {reason}.");
        }
    }

    private static async Task WriteAlertAsync(
        MonitorOutput output,
        MonitorAlert alert,
        CancellationToken cancellationToken)
    {
        await output.WriteAlertAsync(alert, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadHtmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MonitorException($"Could not read --html '{path}': {ex.Message}");
        }
    }

    private static TimeSpan ComputeCycleWait(
        DateTimeOffset cycleStart,
        int timerSeconds,
        int jitterSeconds)
    {
        var next = cycleStart.AddSeconds(timerSeconds);
        var wait = next - DateTimeOffset.UtcNow;
        if (wait < TimeSpan.Zero)
        {
            wait = TimeSpan.Zero;
        }

        if (jitterSeconds > 0)
        {
            wait += TimeSpan.FromSeconds(Random.Shared.Next(0, jitterSeconds + 1));
        }

        return wait;
    }

    private static string BuildRunId() =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}";

    private sealed class DomainRuntime
    {
        public required IMarketplaceProfile Profile { get; init; }
        public required MonitorLocation? Location { get; init; }
        public required string? Cookie { get; init; }
        public AmazonClient? Client { get; set; }
        public DateTimeOffset RetryAfter { get; set; } = DateTimeOffset.MinValue;
        public int ConsecutiveFailures { get; set; }
        public bool Disabled { get; set; }
    }

    private sealed class DomainSetupException(string message) : Exception(message);
}
