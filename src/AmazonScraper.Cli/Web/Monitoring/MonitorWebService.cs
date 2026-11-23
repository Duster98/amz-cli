using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Monitoring.Notifications;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web.Monitoring;

public sealed class MonitorNotFoundException : Exception
{
    public MonitorNotFoundException(string message) : base(message)
    {
    }
}

public sealed class MonitorWebService
{
    private readonly SavedLibraryStore _library;
    private readonly MonitorBindingStore _bindings;
    private readonly MonitorRegistry _registry;
    private readonly IMonitorStartupInstaller _startup;
    private readonly ILogger<MonitorWebService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public MonitorWebService(
        SavedLibraryStore library,
        MonitorBindingStore bindings,
        MonitorRegistry registry,
        ILogger<MonitorWebService> logger,
        IMonitorStartupInstaller? startup = null)
    {
        _library = library;
        _bindings = bindings;
        _registry = registry;
        _logger = logger;
        _startup = startup ?? new MonitorStartupInstaller();
    }

    public MonitorOverviewResponse GetOverview()
    {
        var library = _library.Read();
        var bindings = _bindings.Read();
        var runs = DiscoverRuns();
        var views = bindings.Bindings
            .Select(binding => BuildView(binding, library, bindings, runs))
            .ToList();
        var known = bindings.Bindings
            .Select(binding => binding.Id)
            .ToHashSet(StringComparer.Ordinal);
        var other = runs
            .Where(run => string.IsNullOrWhiteSpace(run.BindingId)
                || !known.Contains(run.BindingId))
            .Select(run => new MonitorExternalRunView
            {
                Run = ToRunView(run),
            })
            .ToList();
        var startupAvailable = _startup.IsAvailable(out var startupError);
        return new MonitorOverviewResponse
        {
            Bindings = views.Count,
            Running = runs.Count(run => run.Status == "running"),
            Alerts = views.Sum(view => view.Stats.AlertedRules),
            StartupAvailable = startupAvailable,
            StartupBackend = startupAvailable ? _startup.Kind : null,
            StartupError = startupError,
            Monitors = views,
            OtherRuns = other,
        };
    }

    public MonitorBindingView Get(string bindingId)
    {
        var library = _library.Read();
        var bindings = _bindings.Read();
        var binding = FindBinding(bindings, bindingId);
        return BuildView(binding, library, bindings, DiscoverRuns());
    }

    public MonitorBindingView Create(MonitorBindingRequest request)
    {
        var kind = NormalizeKind(request.Kind);
        var id = kind == MonitorBindingKind.Group
            ? MonitorBinding.GroupBindingId(Require(request.GroupId, "group_id"))
            : MonitorBinding.ProductBindingId(Require(request.ProductId, "product_id"));
        var document = _bindings.Read();
        if (document.Bindings.Any(binding =>
                string.Equals(binding.Id, id, StringComparison.Ordinal)))
        {
            throw new MonitorException($"Monitor binding already exists: {id}.");
        }

        var library = _library.Read();
        var binding = BuildBinding(id, kind, request, library, existing: null);
        Validate(binding, library, document);
        _bindings.Upsert(binding);
        if (request.Start)
        {
            return StartAsync(id).GetAwaiter().GetResult();
        }
        return Get(id);
    }

    public MonitorBindingView Update(
        string bindingId,
        MonitorBindingRequest request) =>
        UpdateAsync(bindingId, request).GetAwaiter().GetResult();

    public async Task<MonitorBindingView> UpdateAsync(
        string bindingId,
        MonitorBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = _bindings.Read();
            var existing = FindBinding(document, bindingId);
            var kind = NormalizeKind(request.Kind);
            if (!string.Equals(existing.Kind, kind, StringComparison.Ordinal))
            {
                throw new MonitorException("A monitor binding cannot change kind.");
            }
            var oldRuntime = EffectiveRuntime(existing, request: null);
            var library = _library.Read();
            var binding = BuildBinding(existing.Id, kind, request, library, existing);
            Validate(binding, library, document);
            _bindings.Upsert(binding);
            var newRuntime = EffectiveRuntime(binding, request: null);
            var oldStartup = _startup.Describe(oldRuntime.Paths);
            var pathsChanged =
                !StartupIdentity.PathsEqual(
                    oldRuntime.Paths.SavePath,
                    newRuntime.Paths.SavePath)
                || !StartupIdentity.FilesEqual(
                    oldRuntime.Paths.BaseName,
                    newRuntime.Paths.BaseName);
            if (pathsChanged)
            {
                var oldRun = FindRun(DiscoverRuns(), existing);
                if (oldRun is { Status: "running" })
                {
                    await StopRun(existing, oldRun).ConfigureAwait(false);
                }
            }
            if (pathsChanged && oldStartup.Configured)
            {
                await _startup.RemoveAsync(oldRuntime.Paths, cancellationToken)
                    .ConfigureAwait(false);
                var plan = LibraryMonitorPlanBuilder.Build(
                    binding,
                    library,
                    _bindings.Read());
                await _startup.InstallAsync(
                        binding,
                        newRuntime.Paths,
                        plan,
                        newRuntime.Format,
                        newRuntime.Layout,
                        newRuntime.TimerSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Get(binding.Id);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Delete(string bindingId)
    {
        _operationLock.Wait();
        try
        {
            var binding = FindBinding(_bindings.Read(), bindingId);
            var runtime = EffectiveRuntime(binding, request: null);
            var run = FindRun(DiscoverRuns(), binding);
            var paths = new MonitorPathsAccessor(run, runtime).Paths;
            var startup = _startup.Describe(paths);
            if (startup.Configured)
            {
                _startup.RemoveAsync(paths).GetAwaiter().GetResult();
            }
            if (run is { Status: "running" })
            {
                StopRun(binding, run).GetAwaiter().GetResult();
            }
            _bindings.Remove(binding.Id);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MonitorBindingView> StartAsync(
        string bindingId,
        MonitorStartRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bindings = _bindings.Read();
            var binding = FindBinding(bindings, bindingId);
            var library = _library.Read();
            Validate(binding, library, bindings);
            var runtime = EffectiveRuntime(binding, request);
            var plan = LibraryMonitorPlanBuilder.Build(binding, library, bindings);
            var running = FindRun(DiscoverRuns(), binding);
            if (running is { Status: "running" })
            {
                throw new MonitorException(
                    $"Monitor already running (PID {running.Pid}).");
            }

            var startup = _startup.Describe(runtime.Paths);
            if (startup.Configured)
            {
                var started = await _startup.StartAsync(
                        runtime.Paths,
                        cancellationToken)
                    .ConfigureAwait(false);
                var view = Get(binding.Id);
                view.Startup = started;
                return view;
            }

            var effectiveLayout = MonitorOutput.ResolveLayout(
                runtime.Layout,
                runtime.Format,
                plan);
            var runtimeHash = MonitorRuntimeSignature.Compute(
                runtime.TimerSeconds,
                runtime.RequestDelaySeconds,
                runtime.JitterSeconds,
                runtime.Format,
                effectiveLayout,
                runtime.Notify,
                runtime.NotifyOn,
                runtime.NotifyCooldownSeconds,
                runtime.NoAutoUpdate,
                runtime.Paths.SavePath,
                runtime.Paths.BaseName);
            await MonitorProcessLauncher.StartDetachedAsync(
                    binding.Id,
                    runtime.Paths,
                    runtime.Paths.LogPath,
                    plan,
                    runtime.Format,
                    runtime.Layout,
                    runtime.TimerSeconds,
                    runtime.RequestDelaySeconds,
                    runtime.JitterSeconds,
                    runtime.Notify,
                    runtime.NotifyOn,
                    runtime.NotifyCooldownSeconds,
                    runtime.NoAutoUpdate,
                    runtimeHash)
                .ConfigureAwait(false);
            return Get(binding.Id);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MonitorBindingView> StopAsync(
        string bindingId,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var binding = FindBinding(_bindings.Read(), bindingId);
            var runtime = EffectiveRuntime(binding, request: null);
            var run = FindRun(DiscoverRuns(), binding);
            var paths = new MonitorPathsAccessor(run, runtime).Paths;
            var startup = _startup.Describe(paths);
            if (startup.Configured)
            {
                // A startup-managed monitor is restarted by systemd/Task
                // Scheduler when its process is killed. Remove the unit/task
                // first so "Stop" really stops it.
                var removed = await _startup.RemoveAsync(paths, cancellationToken)
                    .ConfigureAwait(false);
                var view = Get(binding.Id);
                view.Startup = removed;
                return view;
            }
            if (run is not null)
            {
                await StopRun(binding, run).ConfigureAwait(false);
            }
            return Get(binding.Id);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MonitorBindingView> RestartAsync(
        string bindingId,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var binding = FindBinding(_bindings.Read(), bindingId);
            var runtime = EffectiveRuntime(binding, request: null);
            var run = FindRun(DiscoverRuns(), binding);
            var startup = _startup.Describe(runtime.Paths);
            if (startup.Configured)
            {
                if (run is { Status: "running" }
                    && !string.Equals(run.Mode, "startup", StringComparison.Ordinal))
                {
                    await StopRun(binding, run).ConfigureAwait(false);
                }
                var restarted = await _startup.RestartAsync(
                        runtime.Paths,
                        cancellationToken)
                    .ConfigureAwait(false);
                var view = Get(binding.Id);
                view.Startup = restarted;
                return view;
            }
            if (run is not null)
            {
                await StopRun(binding, run).ConfigureAwait(false);
            }
        }
        finally
        {
            _operationLock.Release();
        }
        return await StartAsync(bindingId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MonitorBindingView> InstallStartupAsync(
        string bindingId,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bindings = _bindings.Read();
            var binding = FindBinding(bindings, bindingId);
            var library = _library.Read();
            Validate(binding, library, bindings);
            var runtime = EffectiveRuntime(binding, request: null);
            var plan = LibraryMonitorPlanBuilder.Build(binding, library, bindings);
            var result = await _startup.InstallAsync(
                    binding,
                    runtime.Paths,
                    plan,
                    runtime.Format,
                    runtime.Layout,
                    runtime.TimerSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            var view = Get(binding.Id);
            view.Startup = result;
            return view;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MonitorBindingView> RemoveStartupAsync(
        string bindingId,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var binding = FindBinding(_bindings.Read(), bindingId);
            var runtime = EffectiveRuntime(binding, request: null);
            var run = FindRun(DiscoverRuns(), binding);
            var paths = new MonitorPathsAccessor(run, runtime).Paths;
            var result = await _startup.RemoveAsync(paths, cancellationToken)
                .ConfigureAwait(false);
            var view = Get(binding.Id);
            view.Startup = result;
            return view;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public MonitorBindingView VerifyStartup(string bindingId)
    {
        var binding = FindBinding(_bindings.Read(), bindingId);
        var runtime = EffectiveRuntime(binding, request: null);
        var run = FindRun(DiscoverRuns(), binding);
        var paths = new MonitorPathsAccessor(run, runtime).Paths;
        var view = Get(binding.Id);
        view.Startup = _startup.Verify(paths);
        return view;
    }

    public MonitorNotificationTestResult TestNotification(string bindingId)
    {
        var binding = FindBinding(_bindings.Read(), bindingId);
        var runtime = EffectiveRuntime(binding, request: null);
        var on = runtime.NotifyOn.Count > 0
            ? string.Join(',', runtime.NotifyOn)
            : "alerts";
        using var logger = new MonitorLogger(quiet: true, logPath: null);
        var service = NotificationService.Create(
            new NotificationOptions(true, on, runtime.NotifyCooldownSeconds ?? 3600),
            logger);
        var result = service.TestAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return new MonitorNotificationTestResult
        {
            Success = result.Success,
            Backend = service.BackendName,
            Detail = result.Detail,
            Error = result.Error,
        };
    }

    public MonitorSeriesResponse GetSeries(
        string bindingId,
        string? range,
        int? maxPoints)
    {
        var library = _library.Read();
        var bindings = _bindings.Read();
        var binding = FindBinding(bindings, bindingId);
        var runtime = EffectiveRuntime(binding, request: null);
        var plan = LibraryMonitorPlanBuilder.Build(binding, library, bindings);
        var run = FindRun(DiscoverRuns(), binding);
        var files = run?.DataFiles.Count > 0
            ? run.DataFiles
            : MonitorOutput.DescribeFiles(
                    runtime.Paths,
                    runtime.Format,
                    runtime.Layout,
                    plan)
                .ToList();
        var (from, to) = ResolveRange(range);
        var seriesRange = new MonitorSeriesRange(from, to, maxPoints ?? 500);
        var target = ResolveTarget(binding, library);
        if (!target.Available)
        {
            throw new MonitorException(
                target.Error ?? "The monitor target is no longer available.");
        }
        var data = binding.IsGroup
            ? MonitorSeriesReader.ReadGroup(
                files,
                binding.GroupId ?? string.Empty,
                seriesRange)
            : MonitorSeriesReader.ReadProduct(
                files,
                target.Domain ?? string.Empty,
                target.Asin ?? string.Empty,
                seriesRange);
        var thresholds = new List<MonitorThresholdView>();
        if (binding.IsGroup && binding.TotalMax is { } totalMax)
        {
            thresholds.Add(new MonitorThresholdView
            {
                Label = "total_max",
                Value = totalMax,
                Currency = binding.Currency ?? target.Currency,
            });
        }
        if (!binding.IsGroup && binding.MaxPrice is { } maxPrice)
        {
            thresholds.Add(new MonitorThresholdView
            {
                Label = "max_price",
                Value = maxPrice,
                Currency = target.Currency,
            });
        }
        if (!binding.IsGroup)
        {
            var baseline = ResolveSeriesBaseline(binding, data.Points);
            if (baseline is { } basePrice && basePrice > 0)
            {
                if (binding.DropPercent is { } dropPercent)
                {
                    thresholds.Add(new MonitorThresholdView
                    {
                        Label = "drop_percent",
                        Value = basePrice * (1 - dropPercent / 100m),
                        Currency = target.Currency,
                    });
                }
                if (binding.RisePercent is { } risePercent)
                {
                    thresholds.Add(new MonitorThresholdView
                    {
                        Label = "rise_percent",
                        Value = basePrice * (1 + risePercent / 100m),
                        Currency = target.Currency,
                    });
                }
            }
        }
        return new MonitorSeriesResponse
        {
            BindingId = binding.Id,
            Kind = binding.Kind,
            Title = target.Title,
            Currency = data.Currency ?? binding.Currency ?? target.Currency,
            From = from,
            To = to,
            Points = data.Points,
            Alerts = data.Alerts,
            Thresholds = thresholds,
            Observations = data.Observations,
            Truncated = data.Truncated,
            Warnings = data.Warnings,
        };
    }

    private MonitorBindingView BuildView(
        MonitorBinding binding,
        SavedLibraryDocument library,
        MonitorBindingDocument bindings,
        IReadOnlyList<MonitorListEntry> runs)
    {
        var runtime = EffectiveRuntime(binding, request: null);
        var target = ResolveTarget(binding, library);
        var run = FindRun(runs, binding);
        var runtimePaths = new MonitorPathsAccessor(run, runtime).Paths;
        MonitorPlan? plan = null;
        MonitorException? planError = null;
        try
        {
            plan = LibraryMonitorPlanBuilder.Build(binding, library, bindings);
        }
        catch (MonitorException ex)
        {
            planError = ex;
        }
        var effectiveLayout = plan is null
            ? runtime.Layout
            : MonitorOutput.ResolveLayout(
                runtime.Layout,
                runtime.Format,
                plan);
        var view = new MonitorBindingView
        {
            Id = binding.Id,
            Kind = binding.Kind,
            Name = binding.Name,
            ProductId = binding.ProductId,
            GroupId = binding.GroupId,
            MaxPrice = binding.MaxPrice,
            DropPercent = binding.DropPercent,
            RisePercent = binding.RisePercent,
            Baseline = binding.Baseline,
            BaselinePrice = binding.BaselinePrice,
            CooldownSeconds = binding.CooldownSeconds,
            TotalMax = binding.TotalMax,
            AllowIncomplete = binding.AllowIncomplete,
            Currency = binding.Currency,
            Fx = binding.Fx,
            ItemRules = binding.ItemRules,
            CreatedAt = binding.CreatedAt,
            UpdatedAt = binding.UpdatedAt,
            Target = target,
            Runtime = ToRuntimeView(runtime),
            Startup = _startup.Describe(runtimePaths),
            Run = run is null ? null : ToRunView(run),
            Status = !target.Available
                ? "invalid"
                : run?.Status ?? "stopped",
            RestartRequired = RunNeedsRestart(run, runtime, effectiveLayout),
        };
        if (planError is not null)
        {
            view.Error = planError.Message;
            if (view.Status == "stopped")
            {
                view.Status = "invalid";
            }
        }
        var stats = MonitorStatsService.Build(
            runtimePaths,
            run?.DataFiles);
        if (binding.IsGroup)
        {
            var row = stats.GroupRows.FirstOrDefault(item =>
                string.Equals(item.Id, binding.GroupId, StringComparison.Ordinal));
            view.Stats = new MonitorStatsView
            {
                LastTotal = row?.LastTotal,
                MinTotal = row?.MinTotal,
                MaxTotal = row?.MaxTotal,
                Currency = row?.Currency ?? binding.Currency ?? target.Currency,
                Observations = row?.Observations ?? 0,
                AlertedRules = row?.AlertedRules ?? 0,
                LastSeenAt = row?.LastSeenAt ?? run?.LastSeenAt,
            };
        }
        else
        {
            var row = stats.TargetRows.FirstOrDefault(item =>
                string.Equals(item.Domain, target.Domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Asin, target.Asin, StringComparison.OrdinalIgnoreCase));
            view.Stats = new MonitorStatsView
            {
                FirstPrice = row?.FirstPrice,
                MinPrice = row?.MinPrice,
                MaxPrice = row?.MaxPrice,
                LastPrice = row?.LastPrice,
                Currency = row?.Currency ?? target.Currency,
                Observations = row?.Observations ?? 0,
                AlertedRules = row?.AlertedRules ?? 0,
                LastSeenAt = row?.LastSeenAt ?? run?.LastSeenAt,
            };
        }
        view.CanStart = target.Available
            && run?.Status != "running"
            && view.Error is null;
        view.CanStop = run?.Status == "running";
        return view;
    }

    private sealed record MonitorPathsAccessor(
        MonitorListEntry? Run,
        EffectiveMonitorRuntime Runtime)
    {
        public MonitorPaths Paths
        {
            get
            {
                if (Run is not null
                    && !string.IsNullOrWhiteSpace(Run.SavePath)
                    && !string.IsNullOrWhiteSpace(Run.SaveFile))
                {
                    return MonitorPaths.Resolve(Run.SavePath, Run.SaveFile);
                }
                return Runtime.Paths;
            }
        }
    }

    private static MonitorTargetView ResolveTarget(
        MonitorBinding binding,
        SavedLibraryDocument library)
    {
        if (binding.IsGroup)
        {
            var group = library.Groups.FirstOrDefault(item =>
                string.Equals(item.Id, binding.GroupId, StringComparison.Ordinal));
            if (group is null)
            {
                return new MonitorTargetView
                {
                    Kind = MonitorBindingKind.Group,
                    Id = binding.GroupId,
                    Available = false,
                    Error = "Saved group not found.",
                };
            }
            var products = group.Items
                .Select(item => library.Products.FirstOrDefault(product =>
                    string.Equals(product.Id, item.ProductId, StringComparison.Ordinal)))
                .OfType<SavedProduct>()
                .ToList();
            var first = products.FirstOrDefault();
            var groupCurrency = first?.Currency;
            var domains = products
                .Select(product => product.Domain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain!.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal)
                .ToList();
            var marketplaces = domains
                .Select(domain => MarketplaceRegistry.TryResolve(
                        domain,
                        out var profile)
                    ? profile.DisplayName
                    : domain)
                .ToList();
            decimal? groupPrice = null;
            if (products.Count > 0
                && products.All(product => string.Equals(
                    product.Currency,
                    groupCurrency,
                    StringComparison.OrdinalIgnoreCase)))
            {
                decimal total = 0;
                var complete = true;
                foreach (var item in group.Items)
                {
                    var itemProduct = products.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, item.ProductId, StringComparison.Ordinal));
                    var price = itemProduct?.PriceLast ?? itemProduct?.Snapshot.PriceAmount;
                    if (price is null)
                    {
                        complete = false;
                        break;
                    }
                    total += price.Value * Math.Max(1, item.Quantity);
                }
                groupPrice = complete ? total : null;
            }
            return new MonitorTargetView
            {
                Kind = MonitorBindingKind.Group,
                Id = group.Id,
                Title = group.Name,
                Description = group.Description,
                Image = first?.Snapshot.Image,
                Domain = domains.Count == 1 ? domains[0] : null,
                Marketplace = marketplaces.Count > 0
                    ? string.Join(", ", marketplaces)
                    : null,
                Domains = domains,
                Currency = groupCurrency,
                PriceLast = groupPrice,
                Products = group.Items.Count,
            };
        }

        var product = library.Products.FirstOrDefault(item =>
            string.Equals(item.Id, binding.ProductId, StringComparison.Ordinal));
        if (product is null)
        {
            return new MonitorTargetView
            {
                Kind = MonitorBindingKind.Product,
                Id = binding.ProductId,
                Available = false,
                Error = "Saved product not found.",
            };
        }
        var hasMarketplace = MarketplaceRegistry.TryResolve(
            product.Domain,
            out var marketplace);
        var domain = (product.Domain ?? string.Empty).Trim().ToLowerInvariant();
        return new MonitorTargetView
        {
            Kind = MonitorBindingKind.Product,
            Id = product.Id,
            Title = product.Snapshot.Title,
            Image = product.Snapshot.Image,
            Asin = product.Asin,
            Domain = domain.Length == 0 ? product.Domain : domain,
            Marketplace = hasMarketplace
                ? marketplace.DisplayName
                : product.Domain,
            Url = hasMarketplace
                ? marketplace.DetailUrl(product.Asin)
                : null,
            Domains = domain.Length == 0 ? [] : [domain],
            Currency = product.Currency,
            PriceLast = product.PriceLast ?? product.Snapshot.PriceAmount,
            Products = 1,
        };
    }

    private static MonitorRunView ToRunView(MonitorListEntry run) =>
        new()
        {
            BindingId = run.BindingId,
            Status = run.Status,
            Pid = run.Pid,
            StartedAt = run.StartedAt,
            LastSeenAt = run.LastSeenAt,
            Mode = run.Mode,
            Startup = run.Startup,
            StartupEnabled = run.StartupEnabled,
            StartupRegistered = run.StartupRegistered,
            StartupName = run.StartupName,
            StartupId = run.StartupId,
            StartupConfigPath = run.StartupConfigPath,
            StartupCommandPreview = run.StartupCommandPreview,
            Targets = run.Targets,
            TimerSeconds = run.TimerSeconds,
            ScheduleMode = run.ScheduleMode,
            RuntimeHash = run.RuntimeHash,
            WindowSeconds = run.WindowSeconds,
            MaxRequests = run.MaxRequests,
            MinGapSeconds = run.MinGapSeconds,
            MaxGapSeconds = run.MaxGapSeconds,
            LogPath = run.LogPath,
            PidFile = run.PidFile,
            SavePath = run.SavePath,
            SaveFile = run.SaveFile,
            DataFiles = run.DataFiles,
        };

    private static MonitorRuntimeView ToRuntimeView(EffectiveMonitorRuntime runtime) =>
        new()
        {
            TimerSeconds = runtime.TimerSeconds,
            RequestDelaySeconds = runtime.RequestDelaySeconds,
            JitterSeconds = runtime.JitterSeconds,
            SavePath = runtime.Paths.SavePath,
            SaveFile = runtime.Paths.BaseName,
            SaveFormat = runtime.Format == MonitorFormat.Csv ? "csv" : "json",
            Layout = runtime.LayoutName,
            Notify = runtime.Notify,
            NotifyOn = runtime.NotifyOn.ToList(),
            NotifyCooldownSeconds = runtime.NotifyCooldownSeconds,
            NoAutoUpdate = runtime.NoAutoUpdate,
        };

    private static MonitorBinding BuildBinding(
        string id,
        string kind,
        MonitorBindingRequest request,
        SavedLibraryDocument library,
        MonitorBinding? existing)
    {
        var target = kind == MonitorBindingKind.Group
            ? request.GroupId?.Trim()
            : request.ProductId?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new MonitorException(
                kind == MonitorBindingKind.Group
                    ? "group_id is required."
                    : "product_id is required.");
        }
        var binding = new MonitorBinding
        {
            Id = id,
            Kind = kind,
            ProductId = kind == MonitorBindingKind.Product ? target : null,
            GroupId = kind == MonitorBindingKind.Group ? target : null,
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? existing?.Name
                : request.Name.Trim(),
            MaxPrice = request.MaxPrice,
            DropPercent = request.DropPercent,
            RisePercent = request.RisePercent,
            Baseline = request.Baseline,
            BaselinePrice = request.BaselinePrice,
            CooldownSeconds = request.CooldownSeconds,
            TotalMax = request.TotalMax,
            AllowIncomplete = request.AllowIncomplete,
            Currency = request.Currency,
            Fx = request.Fx is { Count: > 0 }
                ? request.Fx
                : existing?.Fx ?? [],
            ItemRules = request.ItemRules ?? existing?.ItemRules ?? [],
            Runtime = request.Runtime ?? existing?.Runtime,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        binding.Runtime = WithRuntimeDefaults(binding.Runtime, binding.Id);
        return binding;
    }

    private static MonitorRuntimeSettings WithRuntimeDefaults(
        MonitorRuntimeSettings? runtime,
        string bindingId)
    {
        runtime ??= new MonitorRuntimeSettings();
        return new MonitorRuntimeSettings
        {
            TimerSeconds = runtime.TimerSeconds ?? 3600,
            RequestDelaySeconds = runtime.RequestDelaySeconds ?? 10,
            JitterSeconds = runtime.JitterSeconds ?? 0,
            SavePath = string.IsNullOrWhiteSpace(runtime.SavePath)
                ? UserStatePaths.MonitorDataPath(bindingId)
                : runtime.SavePath,
            SaveFile = string.IsNullOrWhiteSpace(runtime.SaveFile)
                ? "amz-monitor"
                : runtime.SaveFile,
            SaveFormat = string.IsNullOrWhiteSpace(runtime.SaveFormat)
                ? "json"
                : runtime.SaveFormat,
            Layout = string.IsNullOrWhiteSpace(runtime.Layout)
                ? "auto"
                : runtime.Layout,
            Notify = runtime.Notify ?? false,
            NotifyOn = runtime.NotifyOn.Count == 0
                ? ["alerts"]
                : runtime.NotifyOn,
            NotifyCooldownSeconds = runtime.NotifyCooldownSeconds,
            NoAutoUpdate = runtime.NoAutoUpdate ?? false,
        };
    }

    private static EffectiveMonitorRuntime EffectiveRuntime(
        MonitorBinding binding,
        MonitorStartRequest? request)
    {
        var runtime = WithRuntimeDefaults(binding.Runtime, binding.Id);
        var timer = request?.TimerSeconds ?? runtime.TimerSeconds ?? 3600;
        var delay = request?.RequestDelaySeconds
            ?? runtime.RequestDelaySeconds
            ?? 10;
        var notify = request?.Notify ?? runtime.Notify ?? false;
        var format = string.Equals(
            runtime.SaveFormat,
            "csv",
            StringComparison.OrdinalIgnoreCase)
            ? MonitorFormat.Csv
            : MonitorFormat.JsonLines;
        var layout = (runtime.Layout ?? "auto").ToLowerInvariant() switch
        {
            "combined" => MonitorLayout.Combined,
            "per-group" => MonitorLayout.PerGroup,
            "single" => MonitorLayout.Single,
            _ => MonitorLayout.Auto,
        };
        return new EffectiveMonitorRuntime(
            timer,
            delay,
            runtime.JitterSeconds ?? 0,
            MonitorPaths.Resolve(
                runtime.SavePath ?? UserStatePaths.MonitorDataPath(binding.Id),
                runtime.SaveFile),
            format,
            layout,
            (runtime.Layout ?? "auto").ToLowerInvariant(),
            notify,
            runtime.NotifyOn.Count == 0 ? ["alerts"] : runtime.NotifyOn,
            runtime.NotifyCooldownSeconds,
            runtime.NoAutoUpdate ?? false);
    }

    private static void Validate(
        MonitorBinding binding,
        SavedLibraryDocument library,
        MonitorBindingDocument bindings)
    {
        _ = LibraryMonitorPlanBuilder.Build(binding, library, bindings);
    }

    private MonitorListEntry? FindRun(
        IReadOnlyList<MonitorListEntry> runs,
        MonitorBinding binding)
    {
        var byId = runs
            .Where(run => string.Equals(
                run.BindingId,
                binding.Id,
                StringComparison.Ordinal))
            .OrderBy(run => StatusRank(run.Status))
            .FirstOrDefault();
        if (byId is not null)
        {
            return byId;
        }
        var runtime = EffectiveRuntime(binding, request: null);
        return runs
            .Where(run => string.IsNullOrWhiteSpace(run.BindingId))
            .Where(run => PathsEqual(run.SavePath, runtime.Paths.SavePath)
                && FilesEqual(run.SaveFile, runtime.Paths.BaseName))
            .OrderBy(run => StatusRank(run.Status))
            .FirstOrDefault();
    }

    private static int StatusRank(string status) => status switch
    {
        "running" => 0,
        "stale" => 1,
        "configured" => 2,
        _ => 3,
    };

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool FilesEqual(string? left, string right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? "amz-monitor" : left,
            right,
            StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<MonitorListEntry> DiscoverRuns() =>
        new MonitorDiscovery(
                _registry,
                [UserStatePaths.MonitorDataRoot()])
            .Discover(includeStale: true, prune: false)
            .Entries;

    private async Task StopRun(MonitorBinding binding, MonitorListEntry run)
    {
        var runtime = EffectiveRuntime(binding, request: null);
        var paths = !string.IsNullOrWhiteSpace(run.SavePath)
            && !string.IsNullOrWhiteSpace(run.SaveFile)
            ? MonitorPaths.Resolve(run.SavePath, run.SaveFile)
            : runtime.Paths;
        await MonitorBackground.StopAsync(paths, quiet: true).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(run.Id))
        {
            _registry.Unregister(run.Id);
        }
        _logger.LogInformation(
            "Stopped monitor binding {BindingId} (PID {Pid}).",
            binding.Id,
            run.Pid);
    }

    private static MonitorBinding FindBinding(
        MonitorBindingDocument document,
        string id) =>
        document.Bindings.FirstOrDefault(binding =>
            string.Equals(binding.Id, id, StringComparison.Ordinal))
        ?? throw new MonitorNotFoundException(
            $"Monitor binding not found: {id}.");

    private static string NormalizeKind(string kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is not (MonitorBindingKind.Product or MonitorBindingKind.Group))
        {
            throw new MonitorException($"Invalid monitor kind: {kind}.");
        }
        return normalized;
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new MonitorException($"{name} is required.");
        }
        return value.Trim();
    }

    private static bool RunNeedsRestart(
        MonitorListEntry? run,
        EffectiveMonitorRuntime runtime,
        MonitorLayout effectiveLayout)
    {
        if (run is null || !string.Equals(run.Status, "running", StringComparison.Ordinal))
        {
            return false;
        }

        var hash = MonitorRuntimeSignature.Compute(
            runtime.TimerSeconds,
            runtime.RequestDelaySeconds,
            runtime.JitterSeconds,
            runtime.Format,
            effectiveLayout,
            runtime.Notify,
            runtime.NotifyOn,
            runtime.NotifyCooldownSeconds,
            runtime.NoAutoUpdate,
            runtime.Paths.SavePath,
            runtime.Paths.BaseName);
        if (!string.IsNullOrWhiteSpace(run.RuntimeHash))
        {
            return !string.Equals(run.RuntimeHash, hash, StringComparison.Ordinal);
        }

        return run.TimerSeconds != runtime.TimerSeconds
            || !StartupIdentity.PathsEqual(run.SavePath, runtime.Paths.SavePath)
            || !StartupIdentity.FilesEqual(run.SaveFile, runtime.Paths.BaseName);
    }

    private static decimal? ResolveSeriesBaseline(
        MonitorBinding binding,
        IReadOnlyList<MonitorSeriesPoint> points)
    {
        var baseline = (binding.Baseline ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return baseline switch
        {
            "first_seen" => points.Count > 0 ? points[0].Price : null,
            "max_seen" => points.Count > 0 ? points.Max(point => point.Price) : null,
            "last_seen" => points.Count > 0 ? points[^1].Price : null,
            "fixed" => binding.BaselinePrice,
            _ => null,
        };
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) ResolveRange(
        string? range)
    {
        var now = DateTimeOffset.UtcNow;
        var value = (range ?? "30d").Trim().ToLowerInvariant();
        if (value == "all")
        {
            return (null, now);
        }
        var duration = value switch
        {
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            "30d" => TimeSpan.FromDays(30),
            "90d" => TimeSpan.FromDays(90),
            _ => throw new MonitorException(
                "range must be 24h, 7d, 30d, 90d, or all."),
        };
        return (now - duration, now);
    }

    private sealed record EffectiveMonitorRuntime(
        int TimerSeconds,
        int RequestDelaySeconds,
        int JitterSeconds,
        MonitorPaths Paths,
        MonitorFormat Format,
        MonitorLayout Layout,
        string LayoutName,
        bool Notify,
        IReadOnlyList<string> NotifyOn,
        int? NotifyCooldownSeconds,
        bool NoAutoUpdate);
}
