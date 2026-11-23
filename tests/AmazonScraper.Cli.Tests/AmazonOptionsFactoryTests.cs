using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using CurlImpersonate.Enums;

namespace AmazonScraper.Cli.Tests;

public class AmazonOptionsFactoryTests
{
    [Theory]
    [InlineData("es", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("de", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("uk", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("it", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("fr", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("jp", AmazonTransport.Curl, BrowserProfile.Firefox147)]
    [InlineData("com", AmazonTransport.Http, BrowserProfile.Safari260Ios)]
    public void Marketplace_defaults_use_the_expected_transport_and_profile(
        string domain,
        AmazonTransport expectedTransport,
        BrowserProfile expectedBrowser)
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve(domain),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(expectedTransport, options.Transport);
        Assert.Equal(expectedBrowser, options.BrowserProfile);
    }

    [Fact]
    public void Safari_profile_leaves_the_user_agent_to_the_profile()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Browser = "Safari260Ios" },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Equal(BrowserProfile.Safari260Ios, options.BrowserProfile);
        Assert.Null(options.UserAgent);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("uk")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("jp")]
    public void Curl_firefox_profiles_keep_a_current_firefox_user_agent(
        string domain)
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve(domain),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Equal(BrowserProfile.Firefox147, options.BrowserProfile);
        Assert.NotNull(options.UserAgent);
        Assert.Contains("Firefox/160.0", options.UserAgent);
    }

    [Fact]
    public void Firefox_profile_uses_the_firefox_ua_on_a_safari_marketplace()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Browser = "Firefox147" },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Contains("Firefox/160.0", options.UserAgent);
    }

    [Fact]
    public void Safari_profile_on_a_firefox_marketplace_leaves_the_native_ua()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Browser = "Safari260Ios" },
            MarketplaceRegistry.Resolve("de"),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Null(options.UserAgent);
    }

    [Fact]
    public void Http_transport_uses_a_browser_user_agent()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Http, options.Transport);
        Assert.False(string.IsNullOrWhiteSpace(options.UserAgent));
        Assert.Contains("Safari", options.UserAgent);
    }

    [Fact]
    public void Explicit_user_agent_wins_on_the_curl_transport()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { UserAgent = "custom-agent/1.0" },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: new TransportCooldownStore(directory.Root));

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Equal("custom-agent/1.0", options.UserAgent);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("jp")]
    public void Deals_layout_uses_a_desktop_profile_on_safari_marketplaces(
        string domain)
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve(domain),
            cooldownStore: new TransportCooldownStore(directory.Root),
            desktopLayout: true);

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Equal(BrowserProfile.Firefox147, options.BrowserProfile);
        Assert.Contains("Firefox/160.0", options.UserAgent);
    }

    [Fact]
    public void Deals_layout_with_http_uses_the_desktop_user_agent()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Transport = "http" },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: new TransportCooldownStore(directory.Root),
            desktopLayout: true);

        Assert.Equal(AmazonTransport.Http, options.Transport);
        Assert.Contains("Firefox/160.0", options.UserAgent);
    }

    [Fact]
    public void Deals_layout_respects_an_explicit_browser_profile()
    {
        using var directory = new TempDirectory();
        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Browser = "Safari260Ios" },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: new TransportCooldownStore(directory.Root),
            desktopLayout: true);

        Assert.Equal(BrowserProfile.Safari260Ios, options.BrowserProfile);
        Assert.Null(options.UserAgent);
    }

    [Fact]
    public void Explicit_transport_and_browser_override_the_defaults()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var marketplace = MarketplaceRegistry.Resolve("es");

        var http = AmazonOptionsFactory.Create(
            new CommonSettings
            {
                Transport = "http",
                Browser = "Safari260Ios",
            },
            marketplace,
            cooldownStore: store);
        var curl = AmazonOptionsFactory.Create(
            new CommonSettings(),
            marketplace,
            AmazonTransport.Curl,
            store);

        Assert.Equal(AmazonTransport.Http, http.Transport);
        Assert.Equal(BrowserProfile.Safari260Ios, http.BrowserProfile);
        Assert.Equal(AmazonTransport.Curl, curl.Transport);
        Assert.Equal(BrowserProfile.Firefox147, curl.BrowserProfile);
    }

    [Fact]
    public void Active_http_cooldown_forces_curl_for_the_marketplace()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(2)));

        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store);

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Null(options.UserAgent);
    }

    [Fact]
    public void Expired_cooldown_returns_to_the_http_default()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(1),
            DateTimeOffset.UtcNow.AddHours(-2)));

        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store);

        Assert.Equal(AmazonTransport.Http, options.Transport);
    }

    [Fact]
    public void Explicit_http_override_ignores_an_active_cooldown()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(2)));

        var options = AmazonOptionsFactory.Create(
            new CommonSettings { Transport = "http" },
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store);

        Assert.Equal(AmazonTransport.Http, options.Transport);
    }

    [Fact]
    public void Http_block_callback_persists_the_cooldown()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store);

        options.OnHttpBlocked?.Invoke("HTTP 503");

        Assert.True(store.IsCurlPreferred("com"));
    }

    [Fact]
    public void Search_requests_default_to_safe_multi_marketplace_pacing()
    {
        var request = new ProductSearchRequest("cuffie");

        Assert.Equal(3_000, request.MarketplaceDelayMs);
        Assert.Equal(4_000, request.TransportFallbackDelayMs);
        Assert.True(request.AllowTransportFallback);
    }

    [Fact]
    public void Saved_http_settings_apply_when_the_flags_are_absent()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var saved = new HttpSettings
        {
            Transport = "http",
            Browser = "Safari260Ios",
            UserAgent = "Saved UA",
            BrowserHeaders = true,
            TimeoutSeconds = 45,
        };

        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: store,
            saved: saved);

        Assert.Equal(AmazonTransport.Http, options.Transport);
        Assert.Equal(BrowserProfile.Safari260Ios, options.BrowserProfile);
        Assert.Equal("Saved UA", options.UserAgent);
        Assert.True(options.UseBrowserHeaders);
        Assert.Equal(45, options.TimeoutSeconds);
    }

    [Fact]
    public void Explicit_flags_win_over_saved_http_settings()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var saved = new HttpSettings
        {
            Transport = "curl",
            Browser = "Firefox147",
            UserAgent = "Saved UA",
            BrowserHeaders = false,
            TimeoutSeconds = 45,
        };

        var options = AmazonOptionsFactory.Create(
            new CommonSettings
            {
                Transport = "http",
                Browser = "Safari260Ios",
                UserAgent = "Flag UA",
                BrowserHeaders = true,
                Timeout = 12,
            },
            MarketplaceRegistry.Resolve("it"),
            cooldownStore: store,
            saved: saved);

        Assert.Equal(AmazonTransport.Http, options.Transport);
        Assert.Equal(BrowserProfile.Safari260Ios, options.BrowserProfile);
        Assert.Equal("Flag UA", options.UserAgent);
        Assert.True(options.UseBrowserHeaders);
        Assert.Equal(12, options.TimeoutSeconds);
    }

    [Fact]
    public void Saved_auto_keeps_marketplace_defaults_and_cooldowns()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(2)));

        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store,
            saved: new HttpSettings());

        Assert.Equal(AmazonTransport.Curl, options.Transport);
        Assert.Equal(BrowserProfile.Safari260Ios, options.BrowserProfile);
        Assert.Null(options.UserAgent);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Fact]
    public void Explicit_saved_http_ignores_an_active_cooldown()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(2)));

        var options = AmazonOptionsFactory.Create(
            new CommonSettings(),
            MarketplaceRegistry.Resolve("com"),
            cooldownStore: store,
            saved: new HttpSettings { Transport = "http" });

        Assert.Equal(AmazonTransport.Http, options.Transport);
    }
}
