using CurlImpersonate.Enums;

namespace AmazonScraper.Core.Http;

public enum AmazonTransport
{
    Curl,
    Http,
}

public sealed class AmazonOptions
{
    public double TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Pause between the GLOW seed/modal/address-change steps. Amazon.es
    /// rejects an immediate address-change POST with HTTP 200 and an empty
    /// body; 2 seconds matches the successful browser-like flow.
    /// </summary>
    public int GlowStepDelayMs { get; init; } = 2000;

    public const string DefaultUserAgent =
        "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    public AmazonTransport Transport { get; init; } = AmazonTransport.Curl;
    public BrowserProfile BrowserProfile { get; init; } = BrowserProfile.Safari260Ios;

    /// <summary>
    /// User-Agent sent on the wire. On the curl transport it is left null by
    /// default so curl-impersonate can use the UA that matches its TLS/HTTP2
    /// fingerprint; the plain HTTP transport needs an explicit browser UA.
    /// </summary>
    public string? UserAgent { get; init; } = DefaultUserAgent;
    public string Accept { get; init; } =
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
    public bool UseBrowserHeaders { get; init; }
    public string? CookieHeader { get; init; }

    /// <summary>
    /// Invoked when an HTTP transport request is definitively rejected by
    /// Amazon's anti-bot layer. The CLI uses it to persist a per-marketplace
    /// curl-impersonate cooldown. Curl requests never invoke it.
    /// </summary>
    public Action<string>? OnHttpBlocked { get; init; }

    public AmazonOptions WithCookieHeader(string? cookieHeader) => new()
    {
        TimeoutSeconds = TimeoutSeconds,
        GlowStepDelayMs = GlowStepDelayMs,
        Transport = Transport,
        BrowserProfile = BrowserProfile,
        UserAgent = UserAgent,
        Accept = Accept,
        UseBrowserHeaders = UseBrowserHeaders,
        CookieHeader = cookieHeader,
        OnHttpBlocked = OnHttpBlocked,
    };
}
