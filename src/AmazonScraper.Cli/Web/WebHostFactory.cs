using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Cli.Web.Monitoring;
using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web;

public sealed record WebApplicationBundle(
    WebApplication App,
    SearchJobManager Jobs,
    ISuggestionService Suggestions);

public static class WebHostFactory
{
    private static readonly HashSet<string> AllowedSortTokens =
    [
        "relevanceblender",
        "price-asc-rank",
        "price-desc-rank",
        "review-rank",
        "date-desc-rank",
        "exact-aware-popularity-rank",
    ];

    public static WebApplicationBundle Create(
        WebOptions options,
        WebSessionState session,
        WebAssetStore assets,
        ISuggestionService? suggestions = null,
        WebUiStateStore? uiStateStore = null,
        DealOptionsService? dealOptionsService = null,
        SavedLibraryStore? libraryStore = null,
        HistoryStore? historyStoreIn = null,
        HistoryCacheStore? historyCacheStore = null,
        ProductCacheStore? productCacheStore = null,
        MonitorBindingStore? monitorBindingStore = null,
        MonitorRegistry? monitorRegistryStore = null,
        IMonitorStartupInstaller? monitorStartupInstaller = null,
        IUpdateExecutor? updateExecutor = null,
        HttpSettingsStore? httpSettingsStore = null,
        TransportCooldownStore? transportCooldownStore = null,
        CompareStore? compareStore = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(WebHostFactory).Assembly.GetName().Name,
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            if (options.Remote)
            {
                kestrel.ListenAnyIP(options.Port);
            }
            else
            {
                kestrel.Listen(IPAddress.Loopback, options.Port);
            }
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
        if (options.Quiet)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
        }

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            json.SerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            json.SerializerOptions.Encoder =
                System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        });
        builder.Services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy =
                    JsonNamingPolicy.SnakeCaseLower;
            });
        builder.Services.Configure<HostOptions>(options =>
        {
            // SignalR WebSockets are long-lived. Kestrel must not keep the
            // process alive for the default 30 seconds during shutdown.
            options.ShutdownTimeout = TimeSpan.FromSeconds(2);
        });

        var uiState = uiStateStore ?? new WebUiStateStore();
        var httpSettings = httpSettingsStore ?? new HttpSettingsStore();
        var transportCooldowns = transportCooldownStore ?? TransportCooldownStore.Default;
        var compare = compareStore ?? new CompareStore();
        var library = libraryStore
            ?? new SavedLibraryStore(legacyUiStatePath: uiState.FilePath);
        library.EnsureMigrated();
        var historyStore = historyStoreIn ?? new HistoryStore();
        var historyCache = historyCacheStore ?? new HistoryCacheStore();
        var productCache = productCacheStore ?? new ProductCacheStore();
        var monitorBindings = monitorBindingStore ?? new MonitorBindingStore();
        var monitorRegistry = monitorRegistryStore ?? new MonitorRegistry();
        historyCache.Clear();
        productCache.Clear();
        historyStore.MarkStaleRunning(TimeSpan.FromHours(6));

        builder.Services.AddSingleton(uiState);
        builder.Services.AddSingleton(compare);
        builder.Services.AddSingleton<ICompareRealtimeNotifier, CompareRealtimeNotifier>();
        builder.Services.AddSingleton(library);
        builder.Services.AddSingleton(historyStore);
        builder.Services.AddSingleton(historyCache);
        builder.Services.AddSingleton<ILibraryRealtimeNotifier, LibraryRealtimeNotifier>();
        builder.Services.AddSingleton<IHistoryRealtimeNotifier, HistoryRealtimeNotifier>();
        builder.Services.AddSingleton<LibraryHubConnections>();
        builder.Services.AddSingleton<IUpdateRealtimeNotifier, UpdateRealtimeNotifier>();
        builder.Services.AddSingleton(monitorBindings);
        builder.Services.AddSingleton(monitorRegistry);
        builder.Services.AddSingleton<IMonitorRealtimeNotifier, MonitorRealtimeNotifier>();
        if (monitorStartupInstaller is null)
        {
            builder.Services.AddSingleton<IMonitorStartupInstaller, MonitorStartupInstaller>();
        }
        else
        {
            builder.Services.AddSingleton(monitorStartupInstaller);
        }
        builder.Services.AddSingleton<MonitorWebService>();
        builder.Services.AddHostedService<CompareChangeWatcher>();
        builder.Services.AddHostedService<MonitorChangeWatcher>();
        builder.Services.AddHostedService<LibraryChangeWatcher>();
        builder.Services.AddHostedService<HistoryChangeWatcher>();

        var app = builder.Build();
        var hubConnections = app.Services.GetRequiredService<LibraryHubConnections>();
        app.Lifetime.ApplicationStopping.Register(hubConnections.AbortAll);
        var jobs = new SearchJobManager(
            app.Logger,
            app.Lifetime,
            historyStore,
            historyCache);
        var suggestionService = suggestions ?? new SuggestionService();
        var dealOptions = dealOptionsService ?? new DealOptionsService();
        var updateJobs = new UpdateJobManager(
            updateExecutor ?? new UpdateExecutor(),
            app.Lifetime,
            app.Services.GetRequiredService<IUpdateRealtimeNotifier>(),
            jobs,
            options,
            session,
            app.Logger);
        app.Lifetime.ApplicationStopping.Register(jobs.CancelAll);
        ConfigurePipeline(
            app,
            options,
            session,
            assets,
            jobs,
            suggestionService,
            uiState,
            httpSettings,
            transportCooldowns,
            compare,
            app.Services.GetRequiredService<ICompareRealtimeNotifier>(),
            library,
            historyStore,
            historyCache,
            productCache,
            dealOptions,
            updateJobs,
            app.Services.GetRequiredService<MonitorWebService>());
        return new WebApplicationBundle(app, jobs, suggestionService);
    }

    private static void ConfigurePipeline(
        WebApplication app,
        WebOptions options,
        WebSessionState session,
        WebAssetStore assets,
        SearchJobManager jobs,
        ISuggestionService suggestions,
        WebUiStateStore uiState,
        HttpSettingsStore httpSettings,
        TransportCooldownStore transportCooldowns,
        CompareStore compare,
        ICompareRealtimeNotifier compareNotifier,
        SavedLibraryStore library,
        HistoryStore historyStore,
        HistoryCacheStore historyCache,
        ProductCacheStore productCache,
        DealOptionsService dealOptions,
        UpdateJobManager updateJobs,
        MonitorWebService monitorService)
    {
        var allowedHosts = options.Remote
            ? WebNetwork.AllowedHosts()
            : WebSecurity.LoopbackHosts;

        IReadOnlyList<string> ActiveHttpDomains()
        {
            var domains = uiState.Load().Preferences.Domains;
            return domains.Count > 0
                ? domains
                : [MarketplaceRegistry.Default.Id];
        }

        HttpSettingsDiagnostics BuildHttpDiagnostics(HttpSettings? settings = null) =>
            HttpDiagnostics.Build(
                settings ?? httpSettings.Load(),
                ActiveHttpDomains(),
                transportCooldowns);

        ProductSearchRequest WithHttpSettings(ProductSearchRequest request)
        {
            var saved = httpSettings.Load();
            return request with
            {
                Http = saved,
                AllowTransportFallback = request.AllowTransportFallback
                    && saved.FallbackToCurl,
            };
        }

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (OperationCanceledException)
                when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (Exception exception)
            {
                var mapped = WebErrors.Map(exception);
                if (mapped.Status >= 500)
                {
                    app.Logger.LogError(
                        exception,
                        "Web error {Method} {Path} ({Code})",
                        context.Request.Method,
                        context.Request.Path,
                        mapped.Code);
                }
                else
                {
                    app.Logger.LogWarning(
                        "Web request rejected {Method} {Path}: {Message}",
                        context.Request.Method,
                        context.Request.Path,
                        mapped.Message);
                }

                await WriteErrorAsync(
                    context,
                    mapped.Status,
                    mapped.Code,
                    mapped.Message);
            }
        });

        app.Use(async (context, next) =>
        {
            if (!WebSecurity.IsAllowedHost(context.Request.Host, allowedHosts))
            {
                await WriteErrorAsync(
                    context,
                    400,
                    "invalid_host",
                    "Host not allowed for this interface.");
                return;
            }

            WebSecurity.ApplyHeaders(
                context.Response,
                options.Development,
                context.Request.Host.Value);
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api/v1"))
            {
                session.Touch();
                await next();
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            if (path == "/api/v1/session"
                || path.StartsWithSegments(WebSecurity.ControlPath))
            {
                await next();
                return;
            }

            var cookie = context.Request.Cookies[WebSecurity.CookieName];
            if (!WebSecurity.ValuesEqual(cookie, session.InstanceId))
            {
                await WriteErrorAsync(
                    context,
                    401,
                    "unauthenticated",
                    "Web session missing or expired. Reopen the interface with 'amz-cli web'.");
                return;
            }

            session.Touch();
            if (!HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method))
            {
                var csrf = context.Request.Headers[WebSecurity.CsrfHeader].ToString();
                var origin = context.Request.Headers.Origin.ToString();
                if (!string.Equals(csrf, "1", StringComparison.Ordinal)
                    || !WebSecurity.IsAllowedOrigin(
                        origin,
                        context.Connection.LocalPort,
                        options.Development,
                        allowedHosts))
                {
                    await WriteErrorAsync(
                        context,
                        403,
                        "invalid_csrf",
                        "Unauthorized request: missing CSRF token or invalid origin.");
                    return;
                }
            }

            await next();
        });

        app.MapPost(
            "/api/v1/session",
            (HttpContext context, SessionRequest? request) =>
            {
                if (request is null
                    || !WebSecurity.ValuesEqual(request.Token, session.Token))
                {
                    return JsonError(
                        401,
                        "invalid_token",
                        "Invalid session token. Reopen the interface with 'amz-cli web'.");
                }

                context.Response.Cookies.Append(
                    WebSecurity.CookieName,
                    session.InstanceId,
                    WebSecurity.SessionCookie());
                return Results.Json(
                    new
                    {
                        ok = true,
                        instance_id = session.InstanceId,
                        version = VersionInfo.Informational,
                    },
                    WebJson.Options);
            });

        app.MapGet(
            "/api/v1/control/ping",
            (HttpContext context) =>
            {
                if (!IsControlRequest(context, session))
                {
                    return JsonError(401, "invalid_token", "Invalid control token.");
                }

                return Results.Json(
                    new
                    {
                        ok = true,
                        instance_id = session.InstanceId,
                        pid = Environment.ProcessId,
                        port = session.Port,
                        version = VersionInfo.Informational,
                        started_at = session.StartedAt.ToUniversalTime().ToString("O"),
                        uptime_seconds = (long)session.Uptime.TotalSeconds,
                    },
                    WebJson.Options);
            });

        app.MapPost(
            "/api/v1/control/shutdown",
            (HttpContext context) =>
            {
                if (!IsControlRequest(context, session))
                {
                    return JsonError(401, "invalid_token", "Invalid control token.");
                }

                context.Response.OnCompleted(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(150).ConfigureAwait(false);
                        app.Lifetime.StopApplication();
                    });
                    return Task.CompletedTask;
                });
                return Results.Json(
                    new { ok = true },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapGet(
            "/api/v1/meta",
            () =>
            {
                var report = VersionReport.Read();
                var cacheConfig = productCache.LoadConfig();
                return Results.Json(
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
                        source = report.Source,
                        web = new
                        {
                            build = assets.BuildHash,
                            development = options.Development,
                            port = session.Port,
                            started_at = session.StartedAt
                                .ToUniversalTime()
                                .ToString("O"),
                            uptime_seconds = (long)session.Uptime.TotalSeconds,
                        },
                        library = new
                        {
                            auto_refresh_after_minutes =
                                cacheConfig.SavedAutoRefreshAfterMinutes,
                            group_refresh_cooldown_minutes =
                                cacheConfig.SavedGroupRefreshCooldownMinutes,
                            group_refresh_delay_seconds =
                                cacheConfig.SavedGroupRefreshDelaySeconds,
                        },
                    },
                    WebJson.Options);
            });

        app.MapGet(
            "/api/v1/update",
            () => Results.Json(updateJobs.GetStatus(), WebJson.Options));

        app.MapGet(
            "/api/v1/http-settings",
            () => Results.Json(BuildHttpDiagnostics(), WebJson.Options));

        app.MapPut(
            "/api/v1/http-settings",
            (HttpSettings settings) =>
            {
                if (settings is null)
                {
                    return JsonError(
                        400,
                        "invalid_http_settings",
                        "Missing HTTP settings.");
                }

                var saved = httpSettings.Save(settings);
                return Results.Json(
                    BuildHttpDiagnostics(saved),
                    WebJson.Options);
            });

        app.MapDelete(
            "/api/v1/http-settings/cooldowns/{domain}",
            (string domain) =>
            {
                transportCooldowns.Clear(domain);
                return Results.Json(BuildHttpDiagnostics(), WebJson.Options);
            });

        app.MapGet(
            "/api/v1/compare",
            () => Results.Json(compare.Read(), WebJson.Options));

        app.MapPost(
            "/api/v1/compare/items",
            async (CompareAddRequest request) =>
            {
                if (request is null)
                {
                    return JsonError(
                        400,
                        "invalid_compare_item",
                        "Missing comparison request.");
                }

                var inputs = request.Items is { Count: > 0 }
                    ? request.Items
                    : new List<CompareItemInput>
                    {
                        new()
                        {
                            Domain = request.Domain,
                            Asin = request.Asin,
                        },
                    };
                var targets = new List<CompareTarget>();
                try
                {
                    foreach (var input in inputs)
                    {
                        if (input is null
                            || string.IsNullOrWhiteSpace(input.Asin))
                        {
                            continue;
                        }
                        targets.Add(CompareStore.NormalizeTarget(
                            string.IsNullOrWhiteSpace(input.Domain)
                                ? request.Domain
                                : input.Domain,
                            input.Asin));
                    }
                }
                catch (CompareException ex)
                {
                    return JsonError(
                        400,
                        "invalid_compare_item",
                        ex.Message);
                }

                if (targets.Count == 0)
                {
                    return JsonError(
                        400,
                        "invalid_compare_item",
                        "No products were provided.");
                }

                try
                {
                    var document = compare.Add(
                        targets,
                        request.Source ?? "web");
                    await compareNotifier
                        .NotifyCompareChangedAsync(document.Revision)
                        .ConfigureAwait(false);
                    return Results.Json(document, WebJson.Options);
                }
                catch (CompareFullException ex)
                {
                    return JsonError(409, "compare_full", ex.Message);
                }
                catch (CompareException ex)
                {
                    return JsonError(400, "compare_error", ex.Message);
                }
            });

        app.MapDelete(
            "/api/v1/compare/items/{domain}/{asin}",
            async (string domain, string asin) =>
            {
                try
                {
                    var document = compare.Remove(domain, asin);
                    await compareNotifier
                        .NotifyCompareChangedAsync(document.Revision)
                        .ConfigureAwait(false);
                    return Results.Json(document, WebJson.Options);
                }
                catch (CompareException ex)
                {
                    return JsonError(
                        400,
                        "invalid_compare_item",
                        ex.Message);
                }
            });

        app.MapDelete(
            "/api/v1/compare",
            async () =>
            {
                var document = compare.Clear();
                await compareNotifier
                    .NotifyCompareChangedAsync(document.Revision)
                    .ConfigureAwait(false);
                return Results.Json(document, WebJson.Options);
            });

        app.MapPost(
            "/api/v1/update/check",
            () =>
            {
                if (!updateJobs.TryStartCheck(out var error))
                {
                    return JsonError(
                        409,
                        error ?? "update_busy",
                        "Another update operation is already running.");
                }
                return Results.Json(
                    new { ok = true },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapPost(
            "/api/v1/update/apply",
            () =>
            {
                if (!updateJobs.TryStartApply(out var error))
                {
                    var status = error switch
                    {
                        "update_resume_failed" => 500,
                        "search_in_progress" => 409,
                        _ => 409,
                    };
                    var message = error switch
                    {
                        "search_in_progress" =>
                            "A search is running. Wait for it to finish or cancel it before updating.",
                        "update_not_available" =>
                            "No applicable update is available. Check again first.",
                        "update_resume_failed" =>
                            "Could not prepare the Web interface restart.",
                        _ => "Another update operation is already running.",
                    };
                    return JsonError(
                        status,
                        error ?? "update_busy",
                        message);
                }
                return Results.Json(
                    new { ok = true },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapGet(
            "/api/v1/marketplaces",
            () =>
            {
                var marketplaces = MarketplaceRegistry.All
                    .Where(item => item.Available)
                    .Select(item =>
                    {
                        _ = MarketplaceRegistry.TryResolve(item.Id, out var profile);
                        return new
                        {
                            id = item.Id,
                            display_name = item.DisplayName,
                            host = item.Host,
                            language = item.Language,
                            currency = profile.CurrencyCode,
                            country = profile.CountryCode,
                            country_only_delivery = profile.SupportsCountryOnlyDelivery,
                        };
                    })
                    .ToArray();
                return Results.Json(
                    new
                    {
                        default_id = MarketplaceRegistry.Default.Id,
                        marketplaces,
                    },
                    WebJson.Options);
            });

        app.MapGet(
            "/api/v1/suggestions",
            async (
                string? q,
                string? domain,
                HttpContext context) =>
            {
                var marketplace = MarketplaceRegistry.Resolve(domain);
                var query = AmazonSuggestionClient.NormalizePrefix(q);
                if (query.Length < SuggestionService.MinimumLengthFor(marketplace.Id))
                {
                    return Results.Json(
                        new
                        {
                            query,
                            marketplace_id = marketplace.Id,
                            suggestions = Array.Empty<string>(),
                        },
                        WebJson.Options);
                }

                try
                {
                    var values = await suggestions
                        .GetAsync(marketplace, query, context.RequestAborted)
                        .ConfigureAwait(false);
                    return Results.Json(
                        new
                        {
                            query,
                            marketplace_id = marketplace.Id,
                            suggestions = values,
                        },
                        WebJson.Options);
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    app.Logger.LogWarning(
                        "Suggestions unavailable for {Marketplace}: {Message}",
                        marketplace.Id,
                        exception.Message);
                    return Results.Json(
                        new
                        {
                            query,
                            marketplace_id = marketplace.Id,
                            suggestions = Array.Empty<string>(),
                            unavailable = true,
                        },
                        WebJson.Options);
                }
            });

        app.MapGet(
            "/api/v1/ui-state",
            () => Results.Json(uiState.Load(), WebJson.Options));

        app.MapPut(
            "/api/v1/ui-state/preferences",
            (WebUiPreferences preferences) =>
            {
                if (preferences is null)
                {
                    return JsonError(
                        400,
                        "invalid_preferences",
                        "Missing preferences.");
                }

                return Results.Json(
                    uiState.UpdatePreferences(preferences),
                    WebJson.Options);
            });

        app.MapPut(
            "/api/v1/ui-state/recent-searches",
            (WebRecentSearchesRequest request) =>
            {
                if (request is null)
                {
                    return JsonError(
                        400,
                        "invalid_recent_searches",
                        "Missing history.");
                }

                return Results.Json(
                    uiState.UpdateRecentSearches(request.RecentSearches),
                    WebJson.Options);
            });

        app.MapGet(
            "/api/v1/library",
            () => LibraryResult(library.Read));

        app.MapPost(
            "/api/v1/library/products",
            (SavedProduct product) =>
            {
                if (product is null)
                {
                    return JsonError(
                        400,
                        "invalid_library",
                        "Missing saved product.");
                }
                return LibraryResult(() =>
                    library.AddOrUpdateProducts([product]));
            });

        app.MapPatch(
            "/api/v1/library/products/{productId}",
            (string productId, SavedProduct product) =>
            {
                if (product is null)
                {
                    return JsonError(
                        400,
                        "invalid_library",
                        "Missing saved product.");
                }
                return LibraryResult(() =>
                    library.UpdateProduct(productId, product));
            });

        app.MapDelete(
            "/api/v1/library/products/{productId}",
            (string productId) => LibraryResult(() =>
                library.RemoveProduct(productId)));

        app.MapPost(
            "/api/v1/library/groups",
            (SavedGroup group) =>
            {
                if (group is null)
                {
                    return JsonError(
                        400,
                        "invalid_library",
                        "Missing saved group.");
                }
                return LibraryResult(() => library.CreateGroup(group));
            });

        app.MapPatch(
            "/api/v1/library/groups/{groupId}",
            (string groupId, SavedGroupUpdateRequest request) =>
            {
                if (request is null)
                {
                    return JsonError(
                        400,
                        "invalid_library",
                        "Missing saved group.");
                }
                return LibraryResult(() =>
                    library.UpdateGroup(
                        groupId,
                        request.Name,
                        request.Accent,
                        request.Description));
            });

        app.MapDelete(
            "/api/v1/library/groups/{groupId}",
            (string groupId) => LibraryResult(() =>
                library.DeleteGroup(groupId)));

        app.MapPost(
            "/api/v1/library/groups/{groupId}/items",
            (string groupId, SavedGroupItemsRequest request) =>
            {
                if (request?.ProductIds is not { Count: > 0 })
                {
                    return JsonError(
                        400,
                        "invalid_library",
                        "Missing group products.");
                }
                var quantity = Math.Clamp(request.Quantity, 1, 99);
                return LibraryResult(() => library.AddGroupItems(
                    groupId,
                    request.ProductIds
                        .Distinct(StringComparer.Ordinal)
                        .Select(productId => new SavedGroupItem
                        {
                            ProductId = productId,
                            Quantity = quantity,
                        })));
            });

        app.MapDelete(
            "/api/v1/library/groups/{groupId}/items/{productId}",
            (string groupId, string productId) => LibraryResult(() =>
                library.RemoveGroupItem(groupId, productId)));

        app.MapGet(
            "/api/v1/monitors",
            () => Results.Json(
                monitorService.GetOverview(),
                WebJson.Options));

        app.MapGet(
            "/api/v1/monitors/{bindingId}",
            (string bindingId) => Results.Json(
                monitorService.Get(bindingId),
                WebJson.Options));

        app.MapPost(
            "/api/v1/monitors",
            (MonitorBindingRequest request) => Results.Json(
                monitorService.Create(request),
                WebJson.Options));

        app.MapPatch(
            "/api/v1/monitors/{bindingId}",
            async (
                string bindingId,
                MonitorBindingRequest request,
                CancellationToken cancellationToken) => Results.Json(
                await monitorService.UpdateAsync(
                    bindingId,
                    request,
                    cancellationToken),
                WebJson.Options));

        app.MapDelete(
            "/api/v1/monitors/{bindingId}",
            (string bindingId) =>
            {
                monitorService.Delete(bindingId);
                return Results.Json(new { ok = true }, WebJson.Options);
            });

        app.MapPost(
            "/api/v1/monitors/{bindingId}/start",
            async (
                string bindingId,
                MonitorStartRequest? request,
                CancellationToken cancellationToken) =>
                Results.Json(
                    await monitorService.StartAsync(
                        bindingId,
                        request,
                        cancellationToken),
                    WebJson.Options));

        app.MapPost(
            "/api/v1/monitors/{bindingId}/stop",
            async (string bindingId, CancellationToken cancellationToken) =>
                Results.Json(
                    await monitorService.StopAsync(bindingId, cancellationToken),
                    WebJson.Options));

        app.MapPost(
            "/api/v1/monitors/{bindingId}/restart",
            async (string bindingId, CancellationToken cancellationToken) =>
                Results.Json(
                    await monitorService.RestartAsync(bindingId, cancellationToken),
                    WebJson.Options));

        app.MapPost(
            "/api/v1/monitors/{bindingId}/startup",
            async (string bindingId, CancellationToken cancellationToken) =>
                Results.Json(
                    await monitorService.InstallStartupAsync(
                        bindingId,
                        cancellationToken),
                    WebJson.Options));

        app.MapDelete(
            "/api/v1/monitors/{bindingId}/startup",
            async (string bindingId, CancellationToken cancellationToken) =>
                Results.Json(
                    await monitorService.RemoveStartupAsync(
                        bindingId,
                        cancellationToken),
                    WebJson.Options));

        app.MapPost(
            "/api/v1/monitors/{bindingId}/startup/verify",
            (string bindingId) => Results.Json(
                monitorService.VerifyStartup(bindingId),
                WebJson.Options));

        app.MapPost(
            "/api/v1/monitors/{bindingId}/notify-test",
            (string bindingId) => Results.Json(
                monitorService.TestNotification(bindingId),
                WebJson.Options));

        app.MapGet(
            "/api/v1/monitors/{bindingId}/series",
            (string bindingId, string? range, int? max_points) =>
                Results.Json(
                    monitorService.GetSeries(
                        bindingId,
                        range,
                        max_points),
                    WebJson.Options));

        app.MapHub<LibraryHub>("/api/v1/hubs/library");

        app.MapGet(
            "/api/v1/history",
            () =>
            {
                historyStore.MarkStaleRunning(TimeSpan.FromHours(6));
                var document = historyStore.Read();
                var entries = new JsonArray();
                foreach (var entry in document.Entries)
                {
                    if (JsonSerializer.SerializeToNode(
                            entry,
                            HistoryStore.JsonOptions) is not JsonObject node)
                    {
                        continue;
                    }
                    node["cached"] = entry.Kind == HistoryKind.Search
                        ? historyCache.Has(entry.Id)
                        : !string.IsNullOrWhiteSpace(entry.Asin)
                            && productCache.Has(ProductCacheStore.Key(
                                entry.Domain ?? string.Empty,
                                entry.Asin));
                    entries.Add(node);
                }
                return Results.Json(
                    new
                    {
                        schema = document.Schema,
                        revision = document.Revision,
                        updated_at = document.UpdatedAt,
                        entries,
                    },
                    WebJson.Options);
            });

        app.MapGet(
            "/api/v1/history/{id}/cache",
            (string id) =>
            {
                var cache = historyCache.Read(id);
                return cache is null
                    ? JsonError(
                        404,
                        "history_cache_not_found",
                        "The cached result is no longer available. Run it again.")
                    : Results.Json(cache, WebJson.Options);
            });

        app.MapPost(
            "/api/v1/history/product",
            (HistoryProductRequest request) =>
            {
                if (request is null
                    || string.IsNullOrWhiteSpace(request.Asin)
                    || string.IsNullOrWhiteSpace(request.Domain))
                {
                    return JsonError(
                        400,
                        "invalid_history_product",
                        "Missing product ASIN or domain.");
                }
                try
                {
                    var entry = historyStore.AddProduct(
                        new HistoryProductInput(
                            request.Asin,
                            request.Domain,
                            request.Snapshot,
                            HistorySource.Web));
                    return Results.Json(entry, WebJson.Options);
                }
                catch (HistoryException ex)
                {
                    return JsonError(500, "history_error", ex.Message);
                }
            });

        app.MapPost(
            "/api/v1/history/{id}/cache",
            (string id, JsonObject? result) =>
            {
                if (result is null)
                {
                    return JsonError(
                        400,
                        "invalid_history_cache",
                        "Missing cached result.");
                }
                if (!historyStore.Contains(id))
                {
                    return JsonError(
                        404,
                        "history_entry_not_found",
                        "The history entry no longer exists.");
                }
                try
                {
                    historyCache.Save(id, result);
                    return Results.Json(new { ok = true }, WebJson.Options);
                }
                catch (HistoryException ex)
                {
                    return JsonError(500, "history_cache_error", ex.Message);
                }
            });

        app.MapDelete(
            "/api/v1/history/{id}",
            (string id) =>
            {
                var document = historyStore.Delete(id);
                historyCache.Delete(id);
                return Results.Json(document, WebJson.Options);
            });

        app.MapDelete(
            "/api/v1/history",
            () =>
            {
                var document = historyStore.Clear();
                historyCache.Clear();
                return Results.Json(document, WebJson.Options);
            });

        app.MapGet(
            "/api/v1/deals/options",
            async (
                string? domain,
                string? deal_type,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await dealOptions
                        .GetAsync(
                            domain,
                            deal_type,
                            httpSettings.Load(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Json(result, WebJson.Options);
                }
                catch (ArgumentException ex)
                {
                    return JsonError(
                        400,
                        "invalid_deal_options",
                        ex.Message);
                }
            });

        app.MapPost(
            "/api/v1/deals",
            (DealRequest request) =>
            {
                var validation = ValidateDeal(
                    request,
                    httpSettings.Load().TimeoutSeconds,
                    out var serviceRequest);
                if (validation is not null)
                {
                    return validation;
                }

                serviceRequest = WithHttpSettings(serviceRequest!);
                if (!jobs.TryCreate(
                        serviceRequest,
                        history: null,
                        out var jobId,
                        out _,
                        out var error))
                {
                    return JsonError(
                        429,
                        error ?? "search_busy",
                        "A search is already running. Wait for it to finish or cancel it.");
                }

                return Results.Json(
                    new { job_id = jobId },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapPost(
            "/api/v1/search",
            (SearchRequest request) =>
            {
                var validation = ValidateSearch(
                    request,
                    httpSettings.Load().TimeoutSeconds,
                    out var serviceRequest);
                if (validation is not null)
                {
                    return validation;
                }

                serviceRequest = WithHttpSettings(serviceRequest!);
                HistorySearchInput? history = null;
                if (request.History is not null)
                {
                    var historyDomain = serviceRequest.Domain
                        ?? serviceRequest.Domains?.FirstOrDefault()
                        ?? "it";
                    history = new HistorySearchInput(
                        serviceRequest.Query,
                        historyDomain,
                        serviceRequest.Domains ?? [historyDomain],
                        request.History.Filters ?? new HistorySearchFilters(),
                        HistorySource.Web,
                        request.History.EntryId);
                }

                if (!jobs.TryCreate(
                        serviceRequest,
                        history,
                        out var jobId,
                        out var historyId,
                        out var error))
                {
                    return JsonError(
                        429,
                        error ?? "search_busy",
                        "A search is already running. Wait for it to finish or cancel it.");
                }

                return Results.Json(
                    new { job_id = jobId, history_id = historyId },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapGet(
            "/api/v1/jobs/{id}",
            (string id) =>
            {
                if (!jobs.TryGet(id, out var snapshot))
                {
                    return JsonError(
                        404,
                        "job_not_found",
                        "The requested search does not exist or has expired.");
                }

                return Results.Json(JobNode(snapshot), WebJson.Options);
            });

        app.MapDelete(
            "/api/v1/jobs/{id}",
            (string id) =>
            {
                if (!jobs.TryGet(id, out var snapshot))
                {
                    return JsonError(
                        404,
                        "job_not_found",
                        "The requested search does not exist or has expired.");
                }
                if (snapshot.State is "succeeded" or "failed" or "cancelled")
                {
                    return Results.Json(
                        new { ok = true, state = snapshot.State },
                        WebJson.Options);
                }

                jobs.Cancel(id);
                return Results.Json(
                    new { ok = true, state = "cancelled" },
                    WebJson.Options);
            });

        app.MapPost(
            "/api/v1/shutdown",
            (HttpContext context) =>
            {
                context.Response.OnCompleted(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(150).ConfigureAwait(false);
                        app.Lifetime.StopApplication();
                    });
                    return Task.CompletedTask;
                });
                return Results.Json(
                    new { ok = true },
                    WebJson.Options,
                    statusCode: StatusCodes.Status202Accepted);
            });

        app.MapGet(
            "/api/v1/products/{asin}",
            (
                string asin,
                string? domain,
                bool? rating,
                bool? reviews,
                bool? options,
                string? country,
                string? cap,
                HttpContext context) =>
                ProductResultAsync(
                    asin,
                    domain,
                    rating,
                    reviews,
                    options,
                    country,
                    cap,
                    context,
                    forceRefresh: false,
                    httpSettings: httpSettings.Load(),
                    productCache,
                    library));

        app.MapGet(
            "/api/v1/products/{asin}/variants",
            async (string asin, string? domain, HttpContext context) =>
            {
                try
                {
                    var service = new ProductDetailsService();
                    var result = await service.GetAsync(
                            new ProductDetailsRequest(
                                asin,
                                domain,
                                IncludeRating: false,
                                IncludeReviews: false,
                                IncludeBuyingOptions: false,
                                IncludeVariantPrices: true,
                                TimeoutSeconds: httpSettings.Load().TimeoutSeconds,
                                Http: httpSettings.Load()),
                            context.RequestAborted)
                        .ConfigureAwait(false);
                    return Results.Json(
                        new
                        {
                            variants = result.Product.Variants,
                            warnings = result.Warnings,
                        },
                        WebJson.Options);
                }
                catch (AmazonBlockedException ex)
                {
                    return JsonError(502, "blocked", ex.Message);
                }
                catch (AmazonNotFoundException ex)
                {
                    return JsonError(404, "product_not_found", ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return JsonError(400, "invalid_request", ex.Message);
                }
            });

        app.MapPost(
            "/api/v1/products/{asin}/refresh",
            (
                string asin,
                string? domain,
                bool? rating,
                bool? reviews,
                bool? options,
                string? country,
                string? cap,
                HttpContext context) =>
                ProductResultAsync(
                    asin,
                    domain,
                    rating,
                    reviews,
                    options,
                    country,
                    cap,
                    context,
                    forceRefresh: true,
                    httpSettings: httpSettings.Load(),
                    productCache,
                    library));

        app.MapMethods(
            "/{**path}",
            [HttpMethods.Get, HttpMethods.Head],
            (HttpContext context, string? path) =>
                ServeAssetAsync(context, assets, path));
    }

    private static async Task<IResult> ProductResultAsync(
        string asin,
        string? domain,
        bool? rating,
        bool? reviews,
        bool? options,
        string? country,
        string? cap,
        HttpContext context,
        bool forceRefresh,
        HttpSettings httpSettings,
        ProductCacheStore productCache,
        SavedLibraryStore library)
    {
        var requestedMarketplace = MarketplaceRegistry.Resolve(domain);
        DeliveryLocation? deliveryLocation;
        try
        {
            deliveryLocation = DeliveryLocation.Normalize(
                country,
                cap,
                requestedMarketplace);
        }
        catch (AmazonScraper.Core.Http.DeliveryLocationRejectedException ex)
        {
            return JsonError(422, "delivery_location_rejected", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return JsonError(400, "invalid_delivery_location", ex.Message);
        }

        var includeRating = rating ?? true;
        var includeReviews = reviews ?? true;
        var includeOptions = options ?? true;
        var requestedContext = new ProductCacheContext
        {
            Rating = includeRating,
            Reviews = includeReviews,
            Options = includeOptions,
            Country = deliveryLocation?.CountryCode,
            PostalCode = deliveryLocation?.PostalCode,
        };
        var cacheKey = ProductCacheStore.Key(
            requestedMarketplace.Id,
            asin);
        var cacheConfig = productCache.LoadConfig();
        if (!forceRefresh)
        {
            var cached = productCache.ReadFresh(
                cacheKey,
                requestedContext,
                cacheConfig);
            if (cached?.Document["result"] is JsonObject cachedResult)
            {
                var cachedSaved =
                    ProductCacheStore.ReadSavedProduct(cached.Document);
                if (cachedSaved is not null)
                {
                    try
                    {
                        library.TrySyncProductDetails(
                            cachedSaved,
                            cached.CachedAt);
                    }
                    catch (SavedLibraryException)
                    {
                        // Library synchronization is best effort.
                    }
                }
                var payload = (JsonObject)cachedResult.DeepClone();
                payload["cached"] = true;
                payload["stale"] = false;
                payload["cache_age_seconds"] = Math.Max(
                    0,
                    (long)(DateTimeOffset.UtcNow - cached.CachedAt)
                        .TotalSeconds);
                return Results.Json(payload, WebJson.Options);
            }
        }

        var service = new ProductDetailsService();
        ProductDetailsResult result;
        try
        {
            result = await service
                .GetAsync(
                    new ProductDetailsRequest(
                        asin,
                        domain,
                        IncludeRating: includeRating,
                        IncludeReviews: includeReviews,
                        IncludeBuyingOptions: includeOptions,
                        DeliveryCountry: deliveryLocation?.CountryCode,
                        DeliveryPostalCode: deliveryLocation?.PostalCode,
                        TimeoutSeconds: httpSettings.TimeoutSeconds,
                        Http: httpSettings),
                    context.RequestAborted,
                    forceRefresh)
                .ConfigureAwait(false);
        }
        catch (AmazonScraper.Core.Http.DeliveryLocationRejectedException ex)
        {
            return JsonError(422, "delivery_location_rejected", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return JsonError(400, "invalid_delivery_location", ex.Message);
        }
        var response = ProductResultNode(result);
        SavedProduct? savedProduct = null;
        try
        {
            savedProduct = SavedProductResolver.FromDetails(result);
        }
        catch (SavedLibraryException)
        {
            // A partial product page can still be returned without syncing.
        }

        if (!result.Stale)
        {
            if (savedProduct is not null)
            {
                try
                {
                    library.TrySyncProductDetails(
                        savedProduct,
                        result.FetchedAt);
                }
                catch (SavedLibraryException)
                {
                    // Library synchronization is best effort.
                }
            }
            try
            {
                productCache.Save(
                    cacheKey,
                    response,
                    requestedContext,
                    savedProduct);
            }
            catch (ProductCacheException)
            {
                // The session cache is best effort; the fetched detail is still returned.
            }
        }
        return Results.Json(response, WebJson.Options);
    }

    internal static JsonObject ProductResultNode(ProductDetailsResult result)
    {
        var marketplace = MarketplaceRegistry.Resolve(result.MarketplaceId);
        var priceAmount = PriceParser.TryParse(
            result.Product.Price,
            marketplace.Format);
        var primePriceAmount = result.Product.Prime is { } prime
            ? PriceParser.TryParse(prime.Price, marketplace.Format)
            : null;
        var node = new JsonObject
        {
            ["marketplace"] = new JsonObject
            {
                ["id"] = marketplace.Id,
                ["display_name"] = marketplace.DisplayName,
                ["currency"] = marketplace.CurrencyCode,
                ["country"] = marketplace.CountryCode,
            },
            ["product"] = JsonSerializer.SerializeToNode(
                result.Product,
                WebJson.Options),
            ["price_is_from"] = string.IsNullOrWhiteSpace(result.Product.Price)
                && !string.IsNullOrWhiteSpace(
                    result.Product.BuyingOptions?.LowestPrice),
            ["currency"] = result.Currency,
        };
        if (priceAmount is { } amount)
        {
            node["price_amount"] = amount;
        }
        if (primePriceAmount is { } primeAmount)
        {
            node["prime_price_amount"] = primeAmount;
        }
        var warnings = new JsonArray();
        foreach (var warning in result.Warnings)
        {
            warnings.Add(warning);
        }
        node["warnings"] = warnings;
        node["cached"] = result.Cached;
        node["stale"] = result.Stale;
        node["cache_age_seconds"] = (long)Math.Max(
            0,
            result.CacheAge.TotalSeconds);
        node["fetched_at"] = result.FetchedAt
            .ToUniversalTime()
            .ToString("O");
        return node;
    }

    private static IResult? ValidateSearch(
        SearchRequest request,
        double defaultTimeout,
        out ProductSearchRequest? serviceRequest)
    {
        serviceRequest = null;
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return JsonError(400, "invalid_query", "The query cannot be empty.");
        }
        if (query.Length > 200)
        {
            return JsonError(
                400,
                "invalid_query",
                "The query cannot exceed 200 characters.");
        }

        var domains = new List<string>();
        var requestedDomains = request.Domains is { Count: > 0 }
            ? request.Domains
            : [request.Domain ?? string.Empty];
        foreach (var entry in requestedDomains)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var candidate = entry.Trim().ToLowerInvariant();
            if (!MarketplaceRegistry.TryResolve(candidate, out var marketplace))
            {
                return JsonError(
                    400,
                    "invalid_marketplace",
                    $"Unsupported marketplace: '{candidate}'.");
            }

            if (!domains.Contains(marketplace.Id, StringComparer.Ordinal))
            {
                domains.Add(marketplace.Id);
            }
        }
        if (domains.Count == 0)
        {
            domains.Add(MarketplaceRegistry.Default.Id);
        }
        var domain = domains[0];
        var primaryMarketplace = MarketplaceRegistry.Resolve(domain);
        DeliveryLocation? deliveryLocation;
        try
        {
            deliveryLocation = DeliveryLocation.Normalize(
                request.DeliveryCountry,
                request.DeliveryPostalCode,
                primaryMarketplace);
        }
        catch (AmazonScraper.Core.Http.DeliveryLocationRejectedException ex)
        {
            return JsonError(422, "delivery_location_rejected", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return JsonError(400, "invalid_delivery_location", ex.Message);
        }

        string? sort = null;
        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            sort = request.Sort.Trim().ToLowerInvariant();
            if (!AllowedSortTokens.Contains(sort))
            {
                return JsonError(
                    400,
                    "invalid_sort",
                    $"Unsupported sort: '{sort}'.");
            }
        }

        var pages = request.Pages ?? 1;
        if (pages is < 1 or > 5)
        {
            return JsonError(
                400,
                "invalid_pages",
                "The number of pages must be between 1 and 5.");
        }

        if (request.Page is < 1 or > 20)
        {
            return JsonError(
                400,
                "invalid_page",
                "The page must be between 1 and 20.");
        }

        var pageDelay = request.PageDelayMs ?? 2000;
        if (pageDelay is < 0 or > 60_000)
        {
            return JsonError(
                400,
                "invalid_page_delay",
                "The delay between pages must be between 0 and 60000 ms.");
        }

        var enrichDelay = request.EnrichDelayMs ?? 800;
        if (enrichDelay is < 0 or > 60_000)
        {
            return JsonError(
                400,
                "invalid_enrich_delay",
                "The enrichment delay must be between 0 and 60000 ms.");
        }

        var timeout = request.TimeoutSeconds ?? defaultTimeout;
        if (timeout is < 5 or > 120)
        {
            return JsonError(
                400,
                "invalid_timeout",
                "The timeout must be between 5 and 120 seconds.");
        }

        serviceRequest = new ProductSearchRequest(
            query,
            domain,
            pages,
            request.NoAds,
            request.Delivery,
            request.Enrich,
            pageDelay,
            enrichDelay,
            timeout,
            request.FillMissingPrices ?? true,
            request.Page,
            domains,
            sort,
            DeliveryCountry: deliveryLocation?.CountryCode,
            DeliveryPostalCode: deliveryLocation?.PostalCode);
        return null;
    }

    private static IResult? ValidateDeal(
        DealRequest request,
        double defaultTimeout,
        out ProductSearchRequest? serviceRequest)
    {
        serviceRequest = null;
        if (!MarketplaceRegistry.TryResolve(request.Domain, out var marketplace))
        {
            return JsonError(
                400,
                "invalid_marketplace",
                $"Unsupported marketplace: '{request.Domain}'.");
        }

        var available = DealFilterCatalog.Available(marketplace.Id);
        if (available.Count == 0)
        {
            return JsonError(
                400,
                "deals_unavailable",
                $"No deal type available for {marketplace.DisplayName}.");
        }

        DealFilterKind kind;
        if (string.IsNullOrWhiteSpace(request.DealType))
        {
            kind = available.Contains(DealFilterKind.Today)
                ? DealFilterKind.Today
                : available[0];
        }
        else if (!DealFilterCatalog.TryParseKind(request.DealType, out kind)
            || !available.Contains(kind))
        {
            return JsonError(
                400,
                "invalid_deal_type",
                $"Deal type unavailable for {marketplace.DisplayName}.");
        }

        var category = string.IsNullOrWhiteSpace(request.Category)
            ? null
            : request.Category.Trim();
        if (category is not null
            && (category.Length > 20
                || category.Any(character => !char.IsDigit(character))))
        {
            return JsonError(
                400,
                "invalid_deal_category",
                "The category must be a valid numeric node.");
        }

        var page = request.Page ?? 1;
        if (page is < 1 or > 20)
        {
            return JsonError(
                400,
                "invalid_page",
                "The page must be between 1 and 20.");
        }

        var pageDelay = request.PageDelayMs ?? 2000;
        if (pageDelay is < 0 or > 60_000)
        {
            return JsonError(
                400,
                "invalid_page_delay",
                "The delay between pages must be between 0 and 60000 ms.");
        }

        var enrichDelay = request.EnrichDelayMs ?? 800;
        if (enrichDelay is < 0 or > 60_000)
        {
            return JsonError(
                400,
                "invalid_enrich_delay",
                "The enrichment delay must be between 0 and 60000 ms.");
        }

        var timeout = request.TimeoutSeconds ?? defaultTimeout;
        if (timeout is < 5 or > 120)
        {
            return JsonError(
                400,
                "invalid_timeout",
                "The timeout must be between 5 and 120 seconds.");
        }

        DeliveryLocation? deliveryLocation;
        try
        {
            deliveryLocation = DeliveryLocation.Normalize(
                request.DeliveryCountry,
                request.DeliveryPostalCode,
                marketplace);
        }
        catch (AmazonScraper.Core.Http.DeliveryLocationRejectedException ex)
        {
            return JsonError(422, "delivery_location_rejected", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return JsonError(400, "invalid_delivery_location", ex.Message);
        }

        serviceRequest = new ProductSearchRequest(
            Query: "Deals",
            Domain: marketplace.Id,
            Pages: 1,
            NoAds: request.NoAds,
            Delivery: request.Delivery,
            Enrich: request.Enrich,
            PageDelayMs: pageDelay,
            EnrichDelayMs: enrichDelay,
            TimeoutSeconds: timeout,
            FillMissingPrices: false,
            Page: page,
            DealType: DealFilterCatalog.KindName(kind),
            DealCategory: category,
            DeliveryCountry: deliveryLocation?.CountryCode,
            DeliveryPostalCode: deliveryLocation?.PostalCode);
        return null;
    }

    private static JsonObject JobNode(SearchJobSnapshot snapshot)
    {
        var node = new JsonObject
        {
            ["job_id"] = snapshot.Id,
            ["state"] = snapshot.State,
            ["created_at"] = snapshot.CreatedAt.ToUniversalTime().ToString("O"),
        };
        if (snapshot.StartedAt is { } started)
        {
            node["started_at"] = started.ToUniversalTime().ToString("O");
        }
        if (snapshot.CompletedAt is { } completed)
        {
            node["completed_at"] = completed.ToUniversalTime().ToString("O");
        }
        if (snapshot.Progress is { } progress)
        {
            node["progress"] = new JsonObject
            {
                ["stage"] = progress.Stage,
                ["completed_pages"] = progress.CompletedPages,
                ["total_pages"] = progress.TotalPages,
                ["message"] = progress.Message,
            };
        }
        if (snapshot.Result is { } result)
        {
            node["result"] = SearchResultNode(result);
        }
        if (snapshot.ErrorCode is not null)
        {
            node["error"] = new JsonObject
            {
                ["code"] = snapshot.ErrorCode,
                ["message"] = snapshot.ErrorMessage ?? "Unknown error.",
            };
        }

        return node;
    }

    internal static JsonObject SearchResultNode(ProductSearchResult result)
    {
        var node = new JsonObject
        {
            ["query"] = result.Query,
            ["marketplace_id"] = result.MarketplaceId,
            ["marketplace_name"] = result.MarketplaceName,
            ["currency"] = result.Currency,
            ["duplicate_count"] = result.DuplicateCount,
            ["elapsed_ms"] = result.ElapsedMilliseconds,
            ["total_available_pages"] = result.TotalAvailablePages,
        };

        var requested = new JsonArray();
        foreach (var page in result.RequestedPages)
        {
            requested.Add(page);
        }
        node["requested_pages"] = requested;

        var completed = new JsonArray();
        foreach (var page in result.CompletedPages)
        {
            completed.Add(page);
        }
        node["completed_pages"] = completed;

        var warnings = new JsonArray();
        foreach (var warning in result.Warnings)
        {
            warnings.Add(warning);
        }
        node["warnings"] = warnings;

        _ = MarketplaceRegistry.TryResolve(result.MarketplaceId, out var marketplace);
        var marketplaces = new JsonArray();
        foreach (var summary in result.Marketplaces ?? [])
        {
            var summaryNode = new JsonObject
            {
                ["id"] = summary.Id,
                ["name"] = summary.Name,
                ["currency"] = summary.Currency,
                ["result_count"] = summary.ResultCount,
                ["total_available_pages"] = summary.TotalAvailablePages,
            };
            var summaryCompleted = new JsonArray();
            foreach (var page in summary.CompletedPages)
            {
                summaryCompleted.Add(page);
            }
            summaryNode["completed_pages"] = summaryCompleted;
            var summaryWarnings = new JsonArray();
            foreach (var warning in summary.Warnings)
            {
                summaryWarnings.Add(warning);
            }
            summaryNode["warnings"] = summaryWarnings;
            marketplaces.Add(summaryNode);
        }
        node["marketplaces"] = marketplaces;

        var items = new JsonArray();
        foreach (var item in result.Results)
        {
            if (JsonSerializer.SerializeToNode(item, WebJson.Options) is not JsonObject itemNode)
            {
                continue;
            }

            var itemMarketplace = marketplace;
            if (!string.IsNullOrWhiteSpace(item.MarketplaceId)
                && MarketplaceRegistry.TryResolve(
                    item.MarketplaceId,
                    out var resolvedItemMarketplace))
            {
                itemMarketplace = resolvedItemMarketplace;
            }

            itemNode["price_amount"] = PriceParser.TryParse(
                item.Price,
                itemMarketplace.Format);
            itemNode["currency"] = string.IsNullOrWhiteSpace(item.Currency)
                ? result.Currency
                : item.Currency;
            items.Add(itemNode);
        }
        node["results"] = items;
        return node;
    }

    private static bool IsControlRequest(
        HttpContext context,
        WebSessionState session) =>
        WebSecurity.ValuesEqual(
            context.Request.Headers[WebSecurity.ControlHeader].ToString(),
            session.Token);

    private static IResult JsonError(
        int status,
        string code,
        string message) =>
        Results.Json(
            new { error = new { code, message } },
            WebJson.Options,
            statusCode: status);

    private static async Task WriteErrorAsync(
        HttpContext context,
        int status,
        string code,
        string message)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response
            .WriteAsync(
                JsonSerializer.Serialize(
                    new { error = new { code, message } },
                    WebJson.Options),
                context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task ServeAssetAsync(
        HttpContext context,
        WebAssetStore assets,
        string? path)
    {
        var requestPath = path ?? context.Request.Path.Value ?? string.Empty;
        requestPath = requestPath.TrimStart('/');
        if (requestPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(
                context,
                404,
                "not_found",
                "Endpoint not found.")
                .ConfigureAwait(false);
            return;
        }

        if (requestPath.Length == 0)
        {
            requestPath = "index.html";
        }

        if (!assets.TryGet(requestPath, out var asset))
        {
            if (Path.HasExtension(requestPath)
                || !assets.TryGetIndex(out asset))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        context.Response.Headers.CacheControl = asset.Immutable
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        context.Response.Headers.ETag = asset.ETag;
        if (string.Equals(
                context.Request.Headers.IfNoneMatch.ToString(),
                asset.ETag,
                StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType = asset.ContentType;
        context.Response.ContentLength = asset.Content.Length;
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.Body
            .WriteAsync(asset.Content, context.RequestAborted)
            .ConfigureAwait(false);
    }
    private static IResult LibraryResult(
        Func<SavedLibraryDocument> operation)
    {
        try
        {
            return Results.Json(operation(), WebJson.Options);
        }
        catch (SavedLibraryNotFoundException ex)
        {
            return JsonError(404, "library_not_found", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return JsonError(400, "invalid_library", ex.Message);
        }
        catch (SavedLibraryException ex)
        {
            return JsonError(409, "library_error", ex.Message);
        }
    }

}
