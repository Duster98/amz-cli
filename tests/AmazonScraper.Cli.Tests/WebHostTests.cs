using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Cli.Web;
using AmazonScraper.Update;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonScraper.Cli.Tests;

internal sealed class StubUpdateExecutor : IUpdateExecutor
{
    public Task<UpdateCheckResult> CheckAsync(
        string channel,
        CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateCheckResult(
            UpdateStatus.UpToDate,
            VersionInfo.Informational,
            "linux-x64",
            channel,
            null,
            null,
            $"Version {VersionInfo.Informational} is already up to date."));

    public Task<UpdateApplyOutcome> ApplyAsync(
        UpdateCheckResult check,
        bool requireCurl,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken,
        AutoUpdateContext auto,
        WebUpdateContext web) =>
        throw new NotSupportedException();
}

public class WebHostTests
{
    [Fact]
    public async Task Session_exchange_protects_the_api_and_rejects_bad_hosts()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(directory.Root, "index.html"),
            "<!doctype html><title>web test</title>");
        var assets = WebAssetStore.Load(directory.Root);
        var session = new WebSessionState();
        var suggestions = new StubSuggestionService();
        var uiState = new WebUiStateStore(
            Path.Combine(directory.Root, "web", "ui-state.json"));
        var library = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"),
            legacyUiStatePath: uiState.FilePath);
        var history = new HistoryStore(
            Path.Combine(directory.Root, "history.json"));
        var historyCache = new HistoryCacheStore(
            Path.Combine(directory.Root, "history-cache"));
        var productCache = new ProductCacheStore(
            Path.Combine(directory.Root, "product-cache"),
            Path.Combine(directory.Root, "cache-settings.json"));
        var monitorBindings = new AmazonScraper.Cli.Monitoring.MonitorBindingStore(
            Path.Combine(directory.Root, "monitor-bindings.json"));
        var monitorRegistry = new AmazonScraper.Cli.Monitoring.MonitorRegistry(
            Path.Combine(directory.Root, "monitors"));
        var monitorData = Path.Combine(directory.Root, "monitor-data");
        var httpSettings = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        var cooldowns = new TransportCooldownStore(
            Path.Combine(directory.Root, "transport"));
        var compare = new CompareStore(
            Path.Combine(directory.Root, "compare.json"));
        var bundle = WebHostFactory.Create(
            new WebOptions
            {
                Port = 0,
                Quiet = true,
                IdleMinutes = 0,
            },
            session,
            assets,
            suggestions,
            uiState,
            libraryStore: library,
            historyStoreIn: history,
            historyCacheStore: historyCache,
            productCacheStore: productCache,
            monitorBindingStore: monitorBindings,
            monitorRegistryStore: monitorRegistry,
            monitorStartupInstaller: new FakeMonitorStartupInstaller(),
            updateExecutor: new StubUpdateExecutor(),
            httpSettingsStore: httpSettings,
            transportCooldownStore: cooldowns,
            compareStore: compare);
        var app = bundle.App;
        productCache.Save(
            ProductCacheStore.Key("it", "B0H25NG1JK"),
            System.Text.Json.Nodes.JsonNode.Parse(
                """{"marketplace":{"id":"it"},"product":{"asin":"B0H25NG1JK","title":"ASUS Vivobook"}}""")!);
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookies,
            UseProxy = false,
        };
        using var client = new HttpClient(handler);
        try
        {
            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .First(item => item.StartsWith("http://", StringComparison.Ordinal));
            Assert.NotNull(address);
            client.BaseAddress = new Uri(address!);

            var index = await client.GetStringAsync("/");
            Assert.Contains("web test", index);

            var unauthorized = await client.GetAsync("/api/v1/meta");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            var exchange = await client.PostAsJsonAsync(
                "/api/v1/session",
                new { token = session.Token });
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

            using var negotiate = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/hubs/library/negotiate?negotiateVersion=1");
            negotiate.Headers.Add("X-Amz-Cli-Web", "1");
            var negotiateResponse = await client.SendAsync(negotiate);
            Assert.Equal(HttpStatusCode.OK, negotiateResponse.StatusCode);
            using var negotiateJson = JsonDocument.Parse(
                await negotiateResponse.Content.ReadAsStringAsync());
            Assert.False(string.IsNullOrWhiteSpace(
                negotiateJson.RootElement.GetProperty("connectionToken")
                    .GetString()));

            var meta = await client.GetFromJsonAsync<MetaResponse>("/api/v1/meta");
            Assert.NotNull(meta);
            Assert.Equal(VersionInfo.Informational, meta!.Version);
            Assert.Equal(assets.BuildHash, meta.Web.Build);

            var initialHttp =
                await client.GetFromJsonAsync<HttpSettingsEnvelope>(
                    "/api/v1/http-settings");
            Assert.NotNull(initialHttp);
            Assert.Equal("auto", initialHttp!.Settings.Transport);
            Assert.Contains("Firefox147", initialHttp.Profiles);
            Assert.Contains(
                initialHttp.Effective,
                item => item.Domain == "it");

            using var httpPut = new HttpRequestMessage(
                HttpMethod.Put,
                "/api/v1/http-settings")
            {
                Content = JsonContent.Create(new
                {
                    transport = "curl",
                    browser = "Firefox147",
                    user_agent = "Test UA",
                    browser_headers = true,
                    timeout_seconds = 45,
                    fallback_to_curl = false,
                }),
            };
            httpPut.Headers.Add("X-Amz-Cli-Web", "1");
            var httpPutResponse = await client.SendAsync(httpPut);
            Assert.Equal(HttpStatusCode.OK, httpPutResponse.StatusCode);
            var savedHttp = await httpPutResponse.Content
                .ReadFromJsonAsync<HttpSettingsEnvelope>();
            Assert.NotNull(savedHttp);
            Assert.Equal("curl", savedHttp!.Settings.Transport);
            Assert.Equal("Firefox147", savedHttp.Settings.Browser);
            Assert.Equal("Test UA", savedHttp.Settings.UserAgent);
            Assert.True(savedHttp.Settings.BrowserHeaders);
            Assert.Equal(45, savedHttp.Settings.TimeoutSeconds);
            Assert.False(savedHttp.Settings.FallbackToCurl);
            Assert.True(File.Exists(httpSettings.FilePath));

            Assert.True(cooldowns.RememberHttpFailure(
                "it",
                "HTTP 503",
                TimeSpan.FromHours(2)));
            var withCooldown =
                await client.GetFromJsonAsync<HttpSettingsEnvelope>(
                    "/api/v1/http-settings");
            Assert.NotNull(withCooldown);
            Assert.Single(
                withCooldown!.Cooldowns,
                item => item.Domain == "it");

            using var clearCooldown = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/http-settings/cooldowns/it");
            clearCooldown.Headers.Add("X-Amz-Cli-Web", "1");
            var clearResponse = await client.SendAsync(clearCooldown);
            Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
            var cleared = await clearResponse.Content
                .ReadFromJsonAsync<HttpSettingsEnvelope>();
            Assert.NotNull(cleared);
            Assert.DoesNotContain(
                cleared!.Cooldowns,
                item => item.Domain == "it");

            var initialCompare =
                await client.GetFromJsonAsync<CompareEnvelope>(
                    "/api/v1/compare");
            Assert.NotNull(initialCompare);
            Assert.Empty(initialCompare!.Items);

            using var compareAdd = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/compare/items")
            {
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new { domain = "it", asin = "B0H25NG1JK" },
                        new { domain = "de", asin = "B0FL85P2QS" },
                    },
                    source = "agent",
                }),
            };
            compareAdd.Headers.Add("X-Amz-Cli-Web", "1");
            var compareAddResponse = await client.SendAsync(compareAdd);
            Assert.Equal(HttpStatusCode.OK, compareAddResponse.StatusCode);
            var savedCompare = await compareAddResponse.Content
                .ReadFromJsonAsync<CompareEnvelope>();
            Assert.NotNull(savedCompare);
            Assert.Equal(2, savedCompare!.Items.Count);
            Assert.Equal(1, savedCompare.Revision);
            Assert.Equal("agent", savedCompare.Items[0].Source);

            using var invalidCompare = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/compare/items")
            {
                Content = JsonContent.Create(new
                {
                    domain = "it",
                    asin = "bad",
                }),
            };
            invalidCompare.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidCompareResponse =
                await client.SendAsync(invalidCompare);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidCompareResponse.StatusCode);

            using var fillCompare = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/compare/items")
            {
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new { domain = "it", asin = "B000000001" },
                        new { domain = "it", asin = "B000000002" },
                        new { domain = "it", asin = "B000000003" },
                        new { domain = "it", asin = "B000000004" },
                    },
                }),
            };
            fillCompare.Headers.Add("X-Amz-Cli-Web", "1");
            var fillCompareResponse = await client.SendAsync(fillCompare);
            Assert.Equal(HttpStatusCode.OK, fillCompareResponse.StatusCode);

            using var fullCompare = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/compare/items")
            {
                Content = JsonContent.Create(new
                {
                    domain = "it",
                    asin = "B000000005",
                }),
            };
            fullCompare.Headers.Add("X-Amz-Cli-Web", "1");
            var fullCompareResponse = await client.SendAsync(fullCompare);
            Assert.Equal(
                HttpStatusCode.Conflict,
                fullCompareResponse.StatusCode);

            using var removeCompare = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/compare/items/it/B0H25NG1JK");
            removeCompare.Headers.Add("X-Amz-Cli-Web", "1");
            var removeCompareResponse =
                await client.SendAsync(removeCompare);
            Assert.Equal(HttpStatusCode.OK, removeCompareResponse.StatusCode);
            var afterRemove = await removeCompareResponse.Content
                .ReadFromJsonAsync<CompareEnvelope>();
            Assert.NotNull(afterRemove);
            Assert.Equal(5, afterRemove!.Items.Count);

            using var clearCompare = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/compare");
            clearCompare.Headers.Add("X-Amz-Cli-Web", "1");
            var clearCompareResponse = await client.SendAsync(clearCompare);
            Assert.Equal(HttpStatusCode.OK, clearCompareResponse.StatusCode);
            var afterClear = await clearCompareResponse.Content
                .ReadFromJsonAsync<CompareEnvelope>();
            Assert.NotNull(afterClear);
            Assert.Empty(afterClear!.Items);

            var marketplaces = await client.GetFromJsonAsync<MarketplacesResponse>(
                "/api/v1/marketplaces");
            Assert.NotNull(marketplaces);
            Assert.Equal(
                "it",
                marketplaces!.Marketplaces.Single(item => item.Id == "it").Language);
            Assert.Equal(
                "ja",
                marketplaces.Marketplaces.Single(item => item.Id == "jp").Language);
            Assert.Equal(
                "en",
                marketplaces.Marketplaces.Single(item => item.Id == "uk").Language);

            using var invalidLocation = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/search")
            {
                Content = JsonContent.Create(new
                {
                    query = "test",
                    domain = "it",
                    delivery_country = "IT",
                    delivery_postal_code = "bad",
                }),
            };
            invalidLocation.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidLocationResponse = await client.SendAsync(invalidLocation);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidLocationResponse.StatusCode);

            var invalidProduct = await client.GetAsync(
                "/api/v1/products/B000000001?domain=it&country=IT&cap=bad");
            Assert.Equal(HttpStatusCode.BadRequest, invalidProduct.StatusCode);

            var shortSuggestions = await client.GetFromJsonAsync<SuggestionsResponse>(
                "/api/v1/suggestions?q=z&domain=it");
            Assert.NotNull(shortSuggestions);
            Assert.Empty(shortSuggestions!.Suggestions);
            Assert.Equal(0, suggestions.Calls);

            var suggestionResponse = await client.GetFromJsonAsync<SuggestionsResponse>(
                "/api/v1/suggestions?q=zain&domain=it");
            Assert.NotNull(suggestionResponse);
            Assert.Equal(["zaino donna"], suggestionResponse!.Suggestions);
            Assert.Equal(1, suggestions.Calls);

            var initialState = await client.GetFromJsonAsync<UiStateResponse>(
                "/api/v1/ui-state");
            Assert.NotNull(initialState);
            Assert.Equal("system", initialState!.Preferences.Theme);
            Assert.Equal("en", initialState.Preferences.Language);
            Assert.Equal(string.Empty, initialState.Preferences.DeliveryCountry);
            Assert.Equal(string.Empty, initialState.Preferences.DeliveryPostalCode);
            Assert.Equal("it", initialState.Preferences.DealsDomain);
            Assert.Equal("list", initialState.Preferences.ResultsLayout);
            Assert.Equal("grid", initialState.Preferences.MonitoringLayout);
            Assert.True(File.Exists(uiState.FilePath));

            using var preferencesRequest = new HttpRequestMessage(
                HttpMethod.Put,
                "/api/v1/ui-state/preferences")
            {
                Content = JsonContent.Create(new
                {
                    theme = "dark",
                    language = "de",
                    delivery_country = "DE",
                    delivery_postal_code = "10115",
                    domain = "de",
                    domains = new[] { "de", "fr", "es" },
                    deals_domain = "fr",
                    rail_pinned = true,
                    results_layout = "grid",
                    monitoring_layout = "list",
                    filters = new
                    {
                        no_ads = false,
                        delivery = true,
                        enrich = true,
                        min_rating = 4.5,
                        price_min = 50,
                        price_max = 200,
                        only_deals = true,
                        free_shipping = true,
                        amazon_choice = true,
                        sort = "price-asc",
                    },
                }),
            };
            preferencesRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var preferencesResponse = await client.SendAsync(preferencesRequest);
            Assert.Equal(HttpStatusCode.OK, preferencesResponse.StatusCode);
            var savedPreferences = await preferencesResponse.Content
                .ReadFromJsonAsync<UiStateResponse>();
            Assert.NotNull(savedPreferences);
            Assert.Equal("dark", savedPreferences!.Preferences.Theme);
            Assert.Equal("de", savedPreferences.Preferences.Language);
            Assert.Equal("DE", savedPreferences.Preferences.DeliveryCountry);
            Assert.Equal("10115", savedPreferences.Preferences.DeliveryPostalCode);
            Assert.Equal("de", savedPreferences.Preferences.Domain);
            Assert.Equal(
                new[] { "de", "fr", "es" },
                savedPreferences.Preferences.Domains);
            Assert.Equal("fr", savedPreferences.Preferences.DealsDomain);
            Assert.Equal("grid", savedPreferences.Preferences.ResultsLayout);
            Assert.Equal("list", savedPreferences.Preferences.MonitoringLayout);
            Assert.True(savedPreferences.Preferences.RailPinned);
            Assert.False(savedPreferences.Preferences.Filters.NoAds);
            Assert.Equal(4.5, savedPreferences.Preferences.Filters.MinRating);
            Assert.Equal(50, savedPreferences.Preferences.Filters.PriceMin);
            Assert.Equal(200, savedPreferences.Preferences.Filters.PriceMax);
            Assert.True(savedPreferences.Preferences.Filters.OnlyDeals);
            Assert.True(savedPreferences.Preferences.Filters.FreeShipping);
            Assert.True(savedPreferences.Preferences.Filters.AmazonChoice);
            Assert.Equal("price-asc", savedPreferences.Preferences.Filters.Sort);

            using var recentRequest = new HttpRequestMessage(
                HttpMethod.Put,
                "/api/v1/ui-state/recent-searches")
            {
                Content = JsonContent.Create(new
                {
                    recent_searches = new[]
                    {
                        new
                        {
                            query = "zaino",
                            domain = "it",
                            marketplace = "Amazon.it",
                            at = DateTimeOffset.UtcNow.ToString("O"),
                        },
                    },
                }),
            };
            recentRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var recentResponse = await client.SendAsync(recentRequest);
            Assert.Equal(HttpStatusCode.OK, recentResponse.StatusCode);
            var savedRecent = await recentResponse.Content
                .ReadFromJsonAsync<UiStateResponse>();
            Assert.NotNull(savedRecent);
            Assert.Single(savedRecent!.RecentSearches);

            using var productRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/library/products")
            {
                Content = JsonContent.Create(new
                {
                    asin = "b0h25ng1jk",
                    domain = "it",
                    price_at_save = 699,
                    price_last = 679,
                    snapshot = new
                    {
                        title = "ASUS Vivobook",
                        price = "679,00 €",
                        price_amount = 679,
                        currency = "EUR",
                    },
                }),
            };
            productRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var productResponse = await client.SendAsync(productRequest);
            Assert.Equal(HttpStatusCode.OK, productResponse.StatusCode);
            using var productJson = JsonDocument.Parse(
                await productResponse.Content.ReadAsStringAsync());
            var productRoot = productJson.RootElement;
            var savedProduct = Assert.Single(
                productRoot.GetProperty("products").EnumerateArray());
            Assert.Equal("it:B0H25NG1JK", savedProduct.GetProperty("id").GetString());
            Assert.Equal(1, productRoot.GetProperty("revision").GetInt64());

            using var groupRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/library/groups")
            {
                Content = JsonContent.Create(new
                {
                    id = "g_laptops",
                    name = "Laptop ufficio",
                    accent = "#A1B2C3",
                    description = "Portatili per il lavoro",
                    items = new[]
                    {
                        new
                        {
                            product_id = "it:B0H25NG1JK",
                            quantity = 1,
                        },
                    },
                }),
            };
            groupRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var groupResponse = await client.SendAsync(groupRequest);
            Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
            using var groupJson = JsonDocument.Parse(
                await groupResponse.Content.ReadAsStringAsync());
            var savedGroup = Assert.Single(
                groupJson.RootElement.GetProperty("groups").EnumerateArray());
            Assert.Equal("Laptop ufficio", savedGroup.GetProperty("name").GetString());
            Assert.Equal("#a1b2c3", savedGroup.GetProperty("accent").GetString());
            Assert.Equal(
                "Portatili per il lavoro",
                savedGroup.GetProperty("description").GetString());

            var libraryGet = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/library");
            Assert.Single(libraryGet.GetProperty("products").EnumerateArray());
            Assert.Single(libraryGet.GetProperty("groups").EnumerateArray());
            Assert.Equal(2, libraryGet.GetProperty("revision").GetInt64());

            using var monitorRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/monitors")
            {
                Content = JsonContent.Create(new
                {
                    kind = "group",
                    group_id = "g_laptops",
                    total_max = 900,
                    drop_percent = 5,
                    rise_percent = 10,
                    baseline = "first_seen",
                    runtime = new
                    {
                        timer_seconds = 1800,
                        request_delay_seconds = 12,
                        save_path = monitorData,
                        save_file = "amz-monitor",
                        save_format = "json",
                        layout = "combined",
                    },
                }),
            };
            monitorRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var monitorResponse = await client.SendAsync(monitorRequest);
            Assert.Equal(HttpStatusCode.OK, monitorResponse.StatusCode);
            using var monitorJson = JsonDocument.Parse(
                await monitorResponse.Content.ReadAsStringAsync());
            var monitor = monitorJson.RootElement;
            Assert.Equal("group:g_laptops", monitor.GetProperty("id").GetString());
            Assert.Equal(
                "Laptop ufficio",
                monitor.GetProperty("target").GetProperty("title").GetString());
            Assert.Equal(5,
                monitor.GetProperty("drop_percent").GetInt32());
            Assert.Equal(10,
                monitor.GetProperty("rise_percent").GetInt32());
            Assert.Equal(1800,
                monitor.GetProperty("runtime").GetProperty("timer_seconds").GetInt32());

            var monitors = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/monitors");
            Assert.Single(monitors.GetProperty("monitors").EnumerateArray());
            var series = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/monitors/group%3Ag_laptops/series?range=30d");
            Assert.Equal("group", series.GetProperty("kind").GetString());
            Assert.Equal(0, series.GetProperty("observations").GetInt32());

            using var startupRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/monitors/group%3Ag_laptops/startup");
            startupRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var startupResponse = await client.SendAsync(startupRequest);
            Assert.Equal(HttpStatusCode.OK, startupResponse.StatusCode);
            using var startupJson = JsonDocument.Parse(
                await startupResponse.Content.ReadAsStringAsync());
            Assert.True(startupJson.RootElement
                .GetProperty("startup")
                .GetProperty("configured")
                .GetBoolean());
            Assert.True(startupJson.RootElement
                .GetProperty("startup")
                .GetProperty("registered")
                .GetBoolean());

            using var verifyRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/monitors/group%3Ag_laptops/startup/verify");
            verifyRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var verifyResponse = await client.SendAsync(verifyRequest);
            Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
            using var verifyJson = JsonDocument.Parse(
                await verifyResponse.Content.ReadAsStringAsync());
            Assert.True(verifyJson.RootElement
                .GetProperty("startup")
                .GetProperty("registered")
                .GetBoolean());

            using var deleteMonitorRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/monitors/group%3Ag_laptops");
            deleteMonitorRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var deleteMonitorResponse = await client.SendAsync(deleteMonitorRequest);
            Assert.Equal(HttpStatusCode.OK, deleteMonitorResponse.StatusCode);

            using var patchRequest = new HttpRequestMessage(
                HttpMethod.Patch,
                "/api/v1/library/products/it%3AB0H25NG1JK")
            {
                Content = JsonContent.Create(new
                {
                    price_last = 649,
                    currency = "EUR",
                    snapshot = new
                    {
                        title = "ASUS Vivobook",
                        price = "649,00 €",
                        price_amount = 649,
                        currency = "EUR",
                    },
                }),
            };
            patchRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var patchResponse = await client.SendAsync(patchRequest);
            Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
            using var patchedJson = JsonDocument.Parse(
                await patchResponse.Content.ReadAsStringAsync());
            Assert.Equal(3, patchedJson.RootElement.GetProperty("revision").GetInt64());
            Assert.Equal(
                649,
                Assert.Single(patchedJson.RootElement
                    .GetProperty("products")
                    .EnumerateArray())
                    .GetProperty("price_last")
                    .GetDecimal());

            using var groupPatchRequest = new HttpRequestMessage(
                HttpMethod.Patch,
                "/api/v1/library/groups/g_laptops")
            {
                Content = JsonContent.Create(new
                {
                    name = "Laptop casa",
                    accent = "violet",
                    description = "Descrizione aggiornata",
                }),
            };
            groupPatchRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var groupPatchResponse = await client.SendAsync(groupPatchRequest);
            Assert.Equal(HttpStatusCode.OK, groupPatchResponse.StatusCode);
            using var patchedGroupJson = JsonDocument.Parse(
                await groupPatchResponse.Content.ReadAsStringAsync());
            Assert.Equal(4, patchedGroupJson.RootElement.GetProperty("revision").GetInt64());
            var patchedGroup = Assert.Single(patchedGroupJson.RootElement
                .GetProperty("groups")
                .EnumerateArray());
            Assert.Equal("Laptop casa", patchedGroup.GetProperty("name").GetString());
            Assert.Equal("violet", patchedGroup.GetProperty("accent").GetString());
            Assert.Equal(
                "Descrizione aggiornata",
                patchedGroup.GetProperty("description").GetString());

            using var deleteGroupRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/library/groups/g_laptops");
            deleteGroupRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var deleteGroupResponse = await client.SendAsync(deleteGroupRequest);
            Assert.Equal(HttpStatusCode.OK, deleteGroupResponse.StatusCode);
            using var afterDeleteJson = JsonDocument.Parse(
                await deleteGroupResponse.Content.ReadAsStringAsync());
            Assert.Empty(afterDeleteJson.RootElement
                .GetProperty("groups")
                .EnumerateArray());
            Assert.Single(afterDeleteJson.RootElement
                .GetProperty("products")
                .EnumerateArray());

            using var deleteProductRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/library/products/it%3AB0H25NG1JK");
            deleteProductRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var deleteProductResponse = await client.SendAsync(
                deleteProductRequest);
            Assert.Equal(HttpStatusCode.OK, deleteProductResponse.StatusCode);
            using var afterProductDeleteJson = JsonDocument.Parse(
                await deleteProductResponse.Content.ReadAsStringAsync());
            Assert.Empty(afterProductDeleteJson.RootElement
                .GetProperty("products")
                .EnumerateArray());

            using var invalidProductRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/library/products")
            {
                Content = JsonContent.Create(new
                {
                    asin = "bad",
                    domain = "it",
                }),
            };
            invalidProductRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidProductResponse = await client.SendAsync(
                invalidProductRequest);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidProductResponse.StatusCode);

            var emptyHistory = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/history");
            Assert.Empty(emptyHistory.GetProperty("entries").EnumerateArray());

            var missingCache = await client.GetAsync(
                "/api/v1/history/h_missing/cache");
            Assert.Equal(HttpStatusCode.NotFound, missingCache.StatusCode);

            using var historyProductRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/history/product")
            {
                Content = JsonContent.Create(new
                {
                    asin = "B0H25NG1JK",
                    domain = "it",
                    snapshot = new
                    {
                        title = "ASUS Vivobook",
                        price = "679,00 €",
                        price_amount = 679,
                        currency = "EUR",
                    },
                }),
            };
            historyProductRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var historyProductResponse = await client.SendAsync(
                historyProductRequest);
            Assert.Equal(HttpStatusCode.OK, historyProductResponse.StatusCode);
            var historyAfterProduct = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/history");
            var historyEntry = Assert.Single(
                historyAfterProduct.GetProperty("entries").EnumerateArray());
            Assert.Equal("product", historyEntry.GetProperty("kind").GetString());
            Assert.True(historyEntry.GetProperty("cached").GetBoolean());

            var historyId = historyEntry.GetProperty("id").GetString();
            Assert.True(productCache.Has(
                ProductCacheStore.Key("it", "B0H25NG1JK")));

            using var deleteHistoryRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/v1/history/{historyId}");
            deleteHistoryRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var deleteHistoryResponse = await client.SendAsync(
                deleteHistoryRequest);
            Assert.Equal(HttpStatusCode.OK, deleteHistoryResponse.StatusCode);
            Assert.True(productCache.Has(
                ProductCacheStore.Key("it", "B0H25NG1JK")));

            using var clearHistoryRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/v1/history");
            clearHistoryRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var clearHistoryResponse = await client.SendAsync(
                clearHistoryRequest);
            Assert.Equal(HttpStatusCode.OK, clearHistoryResponse.StatusCode);
            Assert.True(productCache.Has(
                ProductCacheStore.Key("it", "B0H25NG1JK")));

            var updateStatus = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/update");
            Assert.Equal(
                VersionInfo.Informational,
                updateStatus.GetProperty("current_version").GetString());
            Assert.Equal(
                "idle",
                updateStatus.GetProperty("job").GetProperty("phase").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                updateStatus.GetProperty("curl").GetProperty("mode").GetString()));

            using var checkUpdateRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/update/check");
            checkUpdateRequest.Headers.Add("X-Amz-Cli-Web", "1");
            var checkUpdateResponse = await client.SendAsync(
                checkUpdateRequest);
            Assert.Equal(
                HttpStatusCode.Accepted,
                checkUpdateResponse.StatusCode);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(50);
                var checkedStatus = await client.GetFromJsonAsync<JsonElement>(
                    "/api/v1/update");
                if (checkedStatus
                        .GetProperty("job")
                        .GetProperty("phase")
                        .GetString() == "up_to_date")
                {
                    break;
                }
            }
            var checkedUpdate = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/update");
            Assert.Equal(
                "up_to_date",
                checkedUpdate.GetProperty("job").GetProperty("phase").GetString());
            Assert.Equal(
                "up_to_date",
                checkedUpdate.GetProperty("check").GetProperty("status").GetString());

            var withoutCsrf = await client.PostAsJsonAsync(
                "/api/v1/search",
                new { query = "test" });
            Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);

            using var invalidSort = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/search")
            {
                Content = JsonContent.Create(new
                {
                    query = "test",
                    sort = "not-a-sort",
                }),
            };
            invalidSort.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidSortResponse = await client.SendAsync(invalidSort);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidSortResponse.StatusCode);

            using var invalidDomains = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/search")
            {
                Content = JsonContent.Create(new
                {
                    query = "test",
                    domains = new[] { "it", "not-a-marketplace" },
                }),
            };
            invalidDomains.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidDomainsResponse = await client.SendAsync(invalidDomains);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidDomainsResponse.StatusCode);

            using var invalidDealType = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/deals")
            {
                Content = JsonContent.Create(new
                {
                    domain = "it",
                    deal_type = "lightning",
                }),
            };
            invalidDealType.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidDealTypeResponse = await client.SendAsync(invalidDealType);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidDealTypeResponse.StatusCode);

            using var invalidDealCategory = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/deals")
            {
                Content = JsonContent.Create(new
                {
                    domain = "it",
                    deal_type = "today",
                    category = "not-a-node",
                }),
            };
            invalidDealCategory.Headers.Add("X-Amz-Cli-Web", "1");
            var invalidDealCategoryResponse =
                await client.SendAsync(invalidDealCategory);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidDealCategoryResponse.StatusCode);

            using var evil = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/v1/meta");
            evil.Headers.Host = "evil.example";
            var badHost = await client.SendAsync(evil);
            Assert.Equal(HttpStatusCode.BadRequest, badHost.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            bundle.Jobs.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Remote_mode_binds_all_interfaces_and_accepts_a_lan_host()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(directory.Root, "index.html"),
            "<!doctype html><title>remote web test</title>");
        var assets = WebAssetStore.Load(directory.Root);
        var session = new WebSessionState();
        var bundle = WebHostFactory.Create(
            new WebOptions
            {
                Port = 0,
                Remote = true,
                Quiet = true,
                IdleMinutes = 0,
            },
            session,
            assets,
            new StubSuggestionService(),
            new WebUiStateStore(
                Path.Combine(directory.Root, "web", "ui-state.json")),
            libraryStore: new SavedLibraryStore(
                Path.Combine(directory.Root, "library.json")),
            historyStoreIn: new HistoryStore(
                Path.Combine(directory.Root, "history.json")),
            historyCacheStore: new HistoryCacheStore(
                Path.Combine(directory.Root, "history-cache")),
            productCacheStore: new ProductCacheStore(
                Path.Combine(directory.Root, "product-cache"),
                Path.Combine(directory.Root, "cache-settings.json")),
            monitorBindingStore:
                new AmazonScraper.Cli.Monitoring.MonitorBindingStore(
                    Path.Combine(directory.Root, "monitor-bindings.json")),
            monitorRegistryStore:
                new AmazonScraper.Cli.Monitoring.MonitorRegistry(
                    Path.Combine(directory.Root, "monitors")),
            monitorStartupInstaller: new FakeMonitorStartupInstaller(),
            updateExecutor: new StubUpdateExecutor(),
            compareStore: new CompareStore(
                Path.Combine(directory.Root, "compare.json")));
        var app = bundle.App;
        try
        {
            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses;
            Assert.NotNull(addresses);
            Assert.Contains(addresses!, address =>
                address.Contains("0.0.0.0", StringComparison.Ordinal)
                || address.Contains("[::]", StringComparison.Ordinal));

            var address = addresses!.First(item =>
                item.StartsWith("http://", StringComparison.Ordinal));
            var port = new Uri(address).Port;
            var lanAddress = WebNetwork.FindLanAddress();
            if (string.IsNullOrWhiteSpace(lanAddress))
            {
                return;
            }

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            };

            using var allowed = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/session")
            {
                Content = JsonContent.Create(new { token = session.Token }),
            };
            allowed.Headers.Host =
                $"{WebNetwork.FormatHostForUrl(lanAddress)}:{port}";
            var allowedResponse = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

            using var rejected = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/session")
            {
                Content = JsonContent.Create(new { token = session.Token }),
            };
            rejected.Headers.Host = $"192.0.2.1:{port}";
            var rejectedResponse = await client.SendAsync(rejected);
            Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            bundle.Jobs.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class MetaResponse
    {
        public string Version { get; set; } = string.Empty;

        public WebMeta Web { get; set; } = new();
    }

    private sealed class WebMeta
    {
        public string Build { get; set; } = string.Empty;
    }

    private sealed class UiStateResponse
    {
        public UiPreferences Preferences { get; set; } = new();

        [JsonPropertyName("recent_searches")]
        public List<UiRecentSearch> RecentSearches { get; set; } = [];
    }

    private sealed class UiPreferences
    {
        public string Theme { get; set; } = string.Empty;

        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("delivery_country")]
        public string DeliveryCountry { get; set; } = string.Empty;

        [JsonPropertyName("delivery_postal_code")]
        public string DeliveryPostalCode { get; set; } = string.Empty;

        public string Domain { get; set; } = string.Empty;

        public List<string> Domains { get; set; } = [];

        [JsonPropertyName("deals_domain")]
        public string DealsDomain { get; set; } = string.Empty;

        [JsonPropertyName("rail_pinned")]
        public bool RailPinned { get; set; }

        [JsonPropertyName("results_layout")]
        public string ResultsLayout { get; set; } = string.Empty;

        [JsonPropertyName("monitoring_layout")]
        public string MonitoringLayout { get; set; } = string.Empty;

        public UiFilters Filters { get; set; } = new();
    }

    private sealed class UiFilters
    {
        [JsonPropertyName("no_ads")]
        public bool NoAds { get; set; }

        public bool Delivery { get; set; }

        public bool Enrich { get; set; }

        [JsonPropertyName("min_rating")]
        public double MinRating { get; set; }

        [JsonPropertyName("price_min")]
        public double? PriceMin { get; set; }

        [JsonPropertyName("price_max")]
        public double? PriceMax { get; set; }

        [JsonPropertyName("only_deals")]
        public bool OnlyDeals { get; set; }

        [JsonPropertyName("free_shipping")]
        public bool FreeShipping { get; set; }

        [JsonPropertyName("amazon_choice")]
        public bool AmazonChoice { get; set; }

        public string Sort { get; set; } = string.Empty;
    }

    private sealed class UiRecentSearch
    {
        public string Query { get; set; } = string.Empty;
    }

    private sealed class MarketplacesResponse
    {
        public List<MarketplaceResponse> Marketplaces { get; set; } = [];
    }

    private sealed class MarketplaceResponse
    {
        public string Id { get; set; } = string.Empty;

        public string Language { get; set; } = string.Empty;
    }

    private sealed class SuggestionsResponse
    {
        public List<string> Suggestions { get; set; } = [];
    }

    private sealed class HttpSettingsEnvelope
    {
        public HttpSettingsInfo Settings { get; set; } = new();

        public List<string> Profiles { get; set; } = [];

        public List<HttpCooldownInfo> Cooldowns { get; set; } = [];

        public List<EffectiveHttpInfo> Effective { get; set; } = [];
    }

    private sealed class HttpSettingsInfo
    {
        public string Transport { get; set; } = string.Empty;

        public string Browser { get; set; } = string.Empty;

        [JsonPropertyName("user_agent")]
        public string UserAgent { get; set; } = string.Empty;

        [JsonPropertyName("browser_headers")]
        public bool BrowserHeaders { get; set; }

        [JsonPropertyName("timeout_seconds")]
        public double TimeoutSeconds { get; set; }

        [JsonPropertyName("fallback_to_curl")]
        public bool FallbackToCurl { get; set; }
    }

    private sealed class HttpCooldownInfo
    {
        public string Domain { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    private sealed class EffectiveHttpInfo
    {
        public string Domain { get; set; } = string.Empty;

        public string Transport { get; set; } = string.Empty;

        public string Browser { get; set; } = string.Empty;
    }

    private sealed class CompareEnvelope
    {
        public List<CompareItemInfo> Items { get; set; } = [];

        public long Revision { get; set; }
    }

    private sealed class CompareItemInfo
    {
        public string Id { get; set; } = string.Empty;

        public string Domain { get; set; } = string.Empty;

        public string Asin { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;
    }

    private sealed class StubSuggestionService : ISuggestionService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetAsync(
            AmazonScraper.Core.Marketplaces.IMarketplaceProfile marketplace,
            string prefix,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            IReadOnlyList<string> values = ["zaino donna"];
            return Task.FromResult(values);
        }
    }
}
