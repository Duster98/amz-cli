using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Web.Monitoring;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmazonScraper.Cli.Tests;

public class MonitorWebServiceTests
{
    [Fact]
    public void Creates_list_reads_and_deletes_a_product_monitor()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var service = CreateService(directory, data, out var library);
        library.AddOrUpdateProducts([Product()]);

        var created = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            MaxPrice = 600,
            DropPercent = 5,
            RisePercent = 10,
            Baseline = "first_seen",
            Runtime = new MonitorRuntimeSettings
            {
                TimerSeconds = 1800,
                RequestDelaySeconds = 12,
                SavePath = data,
                SaveFile = "amz-monitor",
                SaveFormat = "json",
                Layout = "combined",
            },
        });

        Assert.Equal("it:B0H25NG1JK", created.ProductId);
        Assert.Equal("product:it:B0H25NG1JK", created.Id);
        Assert.Equal("stopped", created.Status);
        Assert.Equal(1800, created.Runtime.TimerSeconds);
        Assert.Equal(12, created.Runtime.RequestDelaySeconds);
        Assert.Equal(5, created.DropPercent);
        Assert.Equal(10, created.RisePercent);
        Assert.Equal("first_seen", created.Baseline);
        Assert.Equal(data, created.Runtime.SavePath);
        Assert.Contains("alerts", created.Runtime.NotifyOn);
        var stored = new MonitorBindingStore(
            Path.Combine(directory.Root, "monitor-bindings.json"))
            .Find(created.Id);
        Assert.NotNull(stored?.Runtime);
        Assert.Contains("alerts", stored!.Runtime!.NotifyOn);
        Assert.Equal("ASUS Vivobook", created.Target.Title);
        Assert.True(created.CanStart);

        var overview = service.GetOverview();
        Assert.Single(overview.Monitors);
        Assert.Equal(1, overview.Bindings);

        service.Delete(created.Id);
        Assert.Empty(service.GetOverview().Monitors);
    }

    [Fact]
    public void Reads_a_product_series_from_the_binding_data_path()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var service = CreateService(directory, data, out var library);
        library.AddOrUpdateProducts([Product()]);
        var view = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            MaxPrice = 600,
            Runtime = new MonitorRuntimeSettings
            {
                SavePath = data,
                SaveFormat = "json",
                Layout = "combined",
            },
        });
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        Directory.CreateDirectory(data);
        File.WriteAllLines(Path.Combine(data, "amz-monitor.items.jsonl"),
        [
            MonitorJson.Item(new MonitorItemObservation
            {
                ObservedAt = start.ToString("O"),
                Domain = "it",
                Asin = "B0H25NG1JK",
                PriceAmount = 599,
                Currency = "EUR",
            }),
            MonitorJson.Item(new MonitorItemObservation
            {
                ObservedAt = start.AddHours(1).ToString("O"),
                Domain = "it",
                Asin = "B0H25NG1JK",
                PriceAmount = 579,
                Currency = "EUR",
            }),
        ]);

        var series = service.GetSeries(view.Id, "24h", 500);

        Assert.Equal(2, series.Observations);
        Assert.Equal(2, series.Points.Count);
        Assert.Equal(579, series.Points[^1].Price);
        Assert.Single(series.Thresholds);
        Assert.Equal(600, series.Thresholds[0].Value);
    }

    [Fact]
    public void Rejects_a_product_that_belongs_to_a_group()
    {
        using var directory = new TempDirectory();
        var service = CreateService(
            directory,
            Path.Combine(directory.Root, "data"),
            out var library);
        library.AddOrUpdateProducts([Product()]);
        library.CreateGroup(new SavedGroup
        {
            Id = "g_test",
            Name = "Test",
            Items = [new SavedGroupItem { ProductId = "it:B0H25NG1JK" }],
        });

        var error = Assert.Throws<MonitorException>(() => service.Create(
            new MonitorBindingRequest
            {
                Kind = MonitorBindingKind.Product,
                ProductId = "it:B0H25NG1JK",
                MaxPrice = 600,
            }));

        Assert.Contains("belongs to group", error.Message);
    }

    [Fact]
    public void Creates_and_reads_a_group_monitor()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var service = CreateService(directory, data, out var library);
        library.AddOrUpdateProducts([Product()]);
        library.CreateGroup(new SavedGroup
        {
            Id = "g_test",
            Name = "Gaming PC",
            Items = [new SavedGroupItem { ProductId = "it:B0H25NG1JK" }],
        });

        var view = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Group,
            GroupId = "g_test",
            TotalMax = 650,
            Runtime = new MonitorRuntimeSettings
            {
                SavePath = data,
                SaveFormat = "json",
                Layout = "combined",
            },
        });

        Assert.Equal("group:g_test", view.Id);
        Assert.Equal("Gaming PC", view.Target.Title);
        Assert.Equal(1, view.Target.Products);

        var start = DateTimeOffset.UtcNow.AddHours(-1);
        Directory.CreateDirectory(data);
        File.WriteAllLines(Path.Combine(data, "amz-monitor.groups.jsonl"),
        [
            MonitorJson.Group(new MonitorGroupObservation
            {
                ObservedAt = start.ToString("O"),
                GroupId = "g_test",
                Name = "Gaming PC",
                Currency = "EUR",
                Total = 620,
                Complete = true,
            }),
        ]);

        var series = service.GetSeries(view.Id, "24h", 500);
        Assert.Single(series.Points);
        Assert.Equal(620, series.Points[0].Price);
        Assert.Equal(650, series.Thresholds[0].Value);
    }

    [Fact]
    public void Registry_entries_keep_the_binding_id()
    {
        using var directory = new TempDirectory();
        var registry = new MonitorRegistry(
            Path.Combine(directory.Root, "monitors"));
        var savePath = Path.Combine(directory.Root, "data");
        Directory.CreateDirectory(savePath);
        var info = new MonitorPidInfo
        {
            Id = "run_test",
            BindingId = "group:g_test",
            Pid = Environment.ProcessId,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            SavePath = savePath,
            SaveFile = "amz-monitor",
            PidFile = Path.Combine(savePath, "amz-monitor.pid"),
        };
        registry.Register(info);

        var found = new MonitorDiscovery(
                registry,
                [savePath])
            .Discover(includeStale: true, prune: false)
            .Entries
            .Single(entry => entry.Id == "run_test");

        Assert.Equal("group:g_test", found.BindingId);
    }

    [Fact]
    public void Exposes_item_rules_and_percentage_thresholds()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var service = CreateService(directory, data, out var library);
        library.AddOrUpdateProducts([Product()]);

        var product = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            DropPercent = 5,
            RisePercent = 10,
            Baseline = "first_seen",
            Runtime = new MonitorRuntimeSettings
            {
                SavePath = data,
                SaveFile = "amz-monitor",
                SaveFormat = "json",
                Layout = "combined",
            },
        });

        Directory.CreateDirectory(data);
        File.WriteAllLines(Path.Combine(data, "amz-monitor.items.jsonl"),
        [
            MonitorJson.Item(new MonitorItemObservation
            {
                ObservedAt = DateTimeOffset.UtcNow
                    .AddDays(-1)
                    .ToString("O"),
                Domain = "it",
                Asin = "B0H25NG1JK",
                PriceAmount = 100,
                Currency = "EUR",
            }),
        ]);

        var series = service.GetSeries(product.Id, "30d", 500);

        Assert.Contains(series.Thresholds, threshold =>
            threshold.Label == "drop_percent" && threshold.Value == 95m);
        Assert.Contains(series.Thresholds, threshold =>
            threshold.Label == "rise_percent" && threshold.Value == 110m);

        service.Delete(product.Id);
        library.CreateGroup(new SavedGroup
        {
            Id = "g_test",
            Name = "Gruppo",
            Items = [new SavedGroupItem { ProductId = "it:B0H25NG1JK" }],
        });
        var group = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Group,
            GroupId = "g_test",
            TotalMax = 500,
            ItemRules = new Dictionary<string, MonitorBindingItemRule>
            {
                ["it:B0H25NG1JK"] = new()
                {
                    MaxPrice = 90,
                    RisePercent = 20,
                },
            },
        });

        Assert.Equal(20, group.ItemRules["it:B0H25NG1JK"].RisePercent);
    }

    [Fact]
    public void Product_target_exposes_marketplace_and_product_url()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var service = CreateService(directory, data, out var library);
        var product = Product();
        product.Domain = "de";
        library.AddOrUpdateProducts([product]);

        var view = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "de:B0H25NG1JK",
            MaxPrice = 600,
        });

        Assert.Equal("de", view.Target.Domain);
        Assert.Equal("Amazon.de", view.Target.Marketplace);
        Assert.Equal(
            "https://www.amazon.de/dp/B0H25NG1JK",
            view.Target.Url);
        Assert.Equal(["de"], view.Target.Domains);
    }

    [Fact]
    public async Task Startup_endpoints_delegate_to_the_shared_installer()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var startup = new FakeMonitorStartupInstaller();
        var service = CreateService(directory, data, out var library, startup);
        library.AddOrUpdateProducts([Product()]);
        var binding = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            MaxPrice = 600,
        });

        var installed = await service.InstallStartupAsync(binding.Id);
        Assert.True(installed.Startup.Configured);
        Assert.Equal(1, startup.InstallCalls);

        var verified = service.VerifyStartup(binding.Id);
        Assert.True(verified.Startup.Registered);
        Assert.Equal(1, startup.VerifyCalls);

        var removed = await service.RemoveStartupAsync(binding.Id);
        Assert.False(removed.Startup.Configured);
        Assert.Equal(1, startup.RemoveCalls);
    }

    [Fact]
    public async Task Stopping_a_startup_managed_monitor_removes_startup()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var startup = new FakeMonitorStartupInstaller();
        var service = CreateService(directory, data, out var library, startup);
        library.AddOrUpdateProducts([Product()]);
        var binding = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            MaxPrice = 600,
        });
        await service.InstallStartupAsync(binding.Id);
        Assert.True(startup.Configured);

        var stopped = await service.StopAsync(binding.Id);

        Assert.False(stopped.Startup.Configured);
        Assert.Equal(1, startup.RemoveCalls);
    }

    [Fact]
    public async Task Notification_changes_require_a_restart_when_running()
    {
        using var directory = new TempDirectory();
        var data = Path.Combine(directory.Root, "data");
        var startup = new FakeMonitorStartupInstaller();
        var service = CreateService(directory, data, out var library, startup);
        library.AddOrUpdateProducts([Product()]);
        var binding = service.Create(new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            Runtime = new MonitorRuntimeSettings
            {
                TimerSeconds = 600,
                SavePath = data,
                Notify = true,
                NotifyOn = ["alerts"],
            },
        });
        var registry = new MonitorRegistry(
            Path.Combine(directory.Root, "monitors"));
        registry.Register(new MonitorPidInfo
        {
            Id = "run_test",
            BindingId = binding.Id,
            Pid = Environment.ProcessId,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            SavePath = data,
            SaveFile = "amz-monitor",
            PidFile = Path.Combine(data, "amz-monitor.pid"),
            Mode = "detach",
        });

        var updated = await service.UpdateAsync(binding.Id, new MonitorBindingRequest
        {
            Kind = MonitorBindingKind.Product,
            ProductId = "it:B0H25NG1JK",
            Runtime = new MonitorRuntimeSettings
            {
                TimerSeconds = 600,
                SavePath = data,
                Notify = true,
                NotifyOn = ["errors"],
            },
        });

        Assert.True(updated.RestartRequired);
    }

    private static MonitorWebService CreateService(
        TempDirectory directory,
        string dataPath,
        out SavedLibraryStore library) =>
        CreateService(
            directory,
            dataPath,
            out library,
            new FakeMonitorStartupInstaller());

    private static MonitorWebService CreateService(
        TempDirectory directory,
        string dataPath,
        out SavedLibraryStore library,
        FakeMonitorStartupInstaller startup)
    {
        _ = dataPath;
        library = new SavedLibraryStore(
            Path.Combine(directory.Root, "library.json"));
        var bindings = new MonitorBindingStore(
            Path.Combine(directory.Root, "monitor-bindings.json"));
        var registry = new MonitorRegistry(
            Path.Combine(directory.Root, "monitors"));
        return new MonitorWebService(
            library,
            bindings,
            registry,
            NullLogger<MonitorWebService>.Instance,
            startup);
    }

    private static SavedProduct Product() => new()
    {
        Asin = "B0H25NG1JK",
        Domain = "it",
        PriceAtSave = 699,
        PriceLast = 679,
        Currency = "EUR",
        Snapshot = new SavedSnapshot
        {
            Title = "ASUS Vivobook",
            Price = "679,00 €",
            PriceAmount = 679,
            Currency = "EUR",
        },
    };
}
