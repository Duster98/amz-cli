using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Tests;

public class HttpDiagnosticsTests
{
    [Fact]
    public void Report_exposes_profiles_and_effective_marketplace_defaults()
    {
        using var directory = new TempDirectory();
        var cooldowns = new TransportCooldownStore(directory.Root);

        var report = HttpDiagnostics.Build(
            new HttpSettings(),
            ["it", "com"],
            cooldowns);

        Assert.Equal("auto", report.Settings.Transport);
        Assert.Contains("Firefox147", report.Profiles);
        Assert.Contains("Safari260Ios", report.Profiles);
        Assert.Equal(2, report.Effective.Count);

        var italy = Assert.Single(
            report.Effective,
            item => item.Domain == "it");
        Assert.Equal("curl", italy.Transport);
        Assert.Equal("Firefox147", italy.Browser);
        Assert.Equal("Firefox147", italy.BrowserDeals);
        Assert.Equal(30, italy.TimeoutSeconds);
        Assert.False(italy.BrowserHeaders);

        var us = Assert.Single(
            report.Effective,
            item => item.Domain == "com");
        Assert.Equal("http", us.Transport);
        Assert.Equal("Safari260Ios", us.Browser);
        Assert.Equal("Firefox147", us.BrowserDeals);
    }

    [Fact]
    public void Saved_overrides_and_cooldowns_are_reflected_in_the_report()
    {
        using var directory = new TempDirectory();
        var cooldowns = new TransportCooldownStore(directory.Root);
        Assert.True(cooldowns.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(2)));

        var report = HttpDiagnostics.Build(
            new HttpSettings
            {
                Transport = "auto",
                Browser = "Safari260Ios",
                BrowserHeaders = true,
                TimeoutSeconds = 45,
            },
            ["com"],
            cooldowns);

        var us = Assert.Single(report.Effective);
        Assert.Equal("curl", us.Transport);
        Assert.Equal("Safari260Ios", us.Browser);
        Assert.Equal("Safari260Ios", us.BrowserDeals);
        Assert.True(us.BrowserHeaders);
        Assert.Equal(45, us.TimeoutSeconds);

        var cooldown = Assert.Single(report.Cooldowns);
        Assert.Equal("com", cooldown.Domain);
        Assert.Equal("HTTP 503", cooldown.Reason);
    }
}
