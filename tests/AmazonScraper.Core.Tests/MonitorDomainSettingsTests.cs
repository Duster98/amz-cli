using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorDomainSettingsTests
{
    [Fact]
    public void Bare_cap_is_allowed_for_one_domain()
    {
        var settings = MonitorDomainSettings.Build(
            ["it"],
            ["20121"],
            [],
            [],
            bareCookie: null);

        var location = Assert.Single(settings.Locations).Value;
        Assert.Equal("20121", location.PostalCode);
        Assert.Equal("IT", location.CountryCode);
    }

    [Fact]
    public void Bare_cap_is_rejected_for_multiple_domains()
    {
        var error = Assert.Throws<MonitorException>(() =>
            MonitorDomainSettings.Build(
                ["it", "de"],
                ["20121"],
                [],
                [],
                bareCookie: null));

        Assert.Contains("specify the postal code per domain", error.Message);
    }

    [Fact]
    public void Cap_for_a_domain_without_targets_is_rejected()
    {
        var error = Assert.Throws<MonitorException>(() =>
            MonitorDomainSettings.Build(
                ["it"],
                ["de:10115"],
                [],
                [],
                bareCookie: null));

        Assert.Contains("has no targets", error.Message);
    }

    [Fact]
    public void Country_without_cap_is_rejected()
    {
        var error = Assert.Throws<MonitorException>(() =>
            MonitorDomainSettings.Build(
                ["it"],
                [],
                ["it:DE"],
                [],
                bareCookie: null));

        Assert.Contains("is missing", error.Message);
    }

    [Fact]
    public void Domain_cookie_is_scoped()
    {
        var settings = MonitorDomainSettings.Build(
            ["it", "de"],
            [],
            [],
            ["it:session-id=abc; ubid-main=def", "de:session-id=xyz"],
            bareCookie: null);

        Assert.Equal("session-id=abc; ubid-main=def", settings.Cookies["it"]);
        Assert.Equal("session-id=xyz", settings.Cookies["de"]);
    }

    [Fact]
    public void Bare_cookie_is_rejected_for_multiple_domains()
    {
        var error = Assert.Throws<MonitorException>(() =>
            MonitorDomainSettings.Build(
                ["it", "de"],
                [],
                [],
                [],
                bareCookie: "session-id=abc"));

        Assert.Contains("--cookie is ambiguous", error.Message);
    }
}
