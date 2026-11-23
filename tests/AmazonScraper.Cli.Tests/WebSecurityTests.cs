using AmazonScraper.Cli.Web;
using Microsoft.AspNetCore.Http;

namespace AmazonScraper.Cli.Tests;

public class WebSecurityTests
{
    [Fact]
    public void Accepts_only_loopback_hosts()
    {
        Assert.True(WebSecurity.IsAllowedHost(new HostString("127.0.0.1:5210")));
        Assert.True(WebSecurity.IsAllowedHost(new HostString("localhost:5210")));
        Assert.True(WebSecurity.IsAllowedHost(new HostString("[::1]:5210")));
        Assert.False(WebSecurity.IsAllowedHost(new HostString("evil.example:5210")));
        Assert.False(WebSecurity.IsAllowedHost(new HostString("192.168.1.10:5210")));
    }

    [Fact]
    public void Remote_mode_allows_only_the_configured_lan_hosts()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1",
            "localhost",
            "::1",
            "192.168.1.10",
        };

        Assert.True(WebSecurity.IsAllowedHost(
            new HostString("192.168.1.10:5210"),
            allowed));
        Assert.True(WebSecurity.IsAllowedHost(
            new HostString("[::1]:5210"),
            allowed));
        Assert.False(WebSecurity.IsAllowedHost(
            new HostString("192.168.1.11:5210"),
            allowed));
        Assert.False(WebSecurity.IsAllowedHost(
            new HostString("evil.example:5210"),
            allowed));
    }

    [Fact]
    public void Remote_origins_use_the_same_host_allow_list()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1",
            "localhost",
            "::1",
            "192.168.1.10",
        };

        Assert.True(WebSecurity.IsAllowedOrigin(
            "http://192.168.1.10:5210",
            5210,
            development: false,
            allowed));
        Assert.False(WebSecurity.IsAllowedOrigin(
            "http://192.168.1.11:5210",
            5210,
            development: false,
            allowed));
        Assert.False(WebSecurity.IsAllowedOrigin(
            "https://192.168.1.10:5210",
            5210,
            development: false,
            allowed));
    }

    [Fact]
    public void Network_helpers_normalize_ipv6_and_trailing_dots()
    {
        Assert.Equal("::1", WebNetwork.NormalizeHost("[::1]"));
        Assert.Equal("192.168.1.10", WebNetwork.NormalizeHost("192.168.1.10."));
        Assert.Equal("[fe80::1]", WebNetwork.FormatHostForUrl("fe80::1"));
        Assert.Equal("192.168.1.10", WebNetwork.FormatHostForUrl("192.168.1.10"));

        var hosts = WebNetwork.AllowedHosts();
        Assert.Contains("127.0.0.1", hosts);
        Assert.Contains("localhost", hosts);
        Assert.Contains("::1", hosts);
    }

    [Fact]
    public void Rejects_cross_site_origins_in_production()
    {
        Assert.True(WebSecurity.IsAllowedOrigin(
            "http://127.0.0.1:5210",
            5210,
            development: false));
        Assert.True(WebSecurity.IsAllowedOrigin(
            "http://localhost:5210",
            5210,
            development: false));
        Assert.False(WebSecurity.IsAllowedOrigin(
            "https://evil.example",
            5210,
            development: false));
        Assert.False(WebSecurity.IsAllowedOrigin(
            "http://127.0.0.1:5211",
            5210,
            development: false));
    }

    [Fact]
    public void Allows_the_vite_origin_only_in_development()
    {
        Assert.True(WebSecurity.IsAllowedOrigin(
            "http://localhost:5173",
            5210,
            development: true));
        Assert.False(WebSecurity.IsAllowedOrigin(
            "http://localhost:5173",
            5210,
            development: false));
    }

    [Fact]
    public void Compares_secrets_without_accepting_empty_values()
    {
        Assert.True(WebSecurity.ValuesEqual("abc", "abc"));
        Assert.False(WebSecurity.ValuesEqual("abc", "abd"));
        Assert.False(WebSecurity.ValuesEqual("", ""));
        Assert.False(WebSecurity.ValuesEqual(null, "abc"));
    }

    [Fact]
    public void Session_cookie_is_http_only_and_same_site_strict()
    {
        var cookie = WebSecurity.SessionCookie();
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path);
    }
}
