using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using CurlImpersonate.Enums;
using Spectre.Console;

namespace AmazonScraper.Cli.Commands;

public static class AmazonOptionsFactory
{
    public static AmazonOptions Create(
        CommonSettings settings,
        IMarketplaceProfile marketplace,
        AmazonTransport? transportOverride = null,
        TransportCooldownStore? cooldownStore = null,
        bool desktopLayout = false,
        HttpSettings? saved = null)
    {
        var savedSettings = (saved ?? HttpSettings.Default).Normalize();
        var isUs = marketplace.Id == "com";
        var isJp = marketplace.Id == "jp";
        var isEs = marketplace.Id == "es";

        // The product parser needs the desktop layout: Amazon's mobile pages
        // omit colorImages (the gallery) and the technical specifications.
        // Firefox 147 with a current Firefox UA is a coherent desktop
        // fingerprint and is the stable choice on the European marketplaces.
        // Amazon.com keeps its HTTP transport and mobile UA for compatibility.
        var desktopByDefault = !isUs;
        var defaultBrowser = desktopLayout || desktopByDefault
            ? BrowserProfile.Firefox147
            : BrowserProfile.Safari260Ios;
        var configuredBrowser = !string.IsNullOrWhiteSpace(settings.Browser)
            ? settings.Browser
            : savedSettings.Browser;
        var browser = string.IsNullOrWhiteSpace(configuredBrowser)
            || string.Equals(
                configuredBrowser,
                HttpSettings.Auto,
                StringComparison.OrdinalIgnoreCase)
            ? defaultBrowser
            : Enum.TryParse<BrowserProfile>(
                configuredBrowser,
                ignoreCase: true,
                out var parsed)
                ? parsed
                : defaultBrowser;

        var firefoxUserAgent =
            "Mozilla/5.0 (X11; Linux x86_64; rv:160.0) Gecko/20100101 Firefox/160.0";
        var defaultUserAgent = desktopLayout || desktopByDefault
            ? firefoxUserAgent
            : AmazonOptions.DefaultUserAgent;

        var store = cooldownStore ?? TransportCooldownStore.Default;
        var flagTransport = settings.Transport switch
        {
            "curl" => AmazonTransport.Curl,
            "http" => AmazonTransport.Http,
            _ => (AmazonTransport?)null,
        };
        var savedTransport = savedSettings.Transport switch
        {
            "curl" => AmazonTransport.Curl,
            "http" => AmazonTransport.Http,
            _ => (AmazonTransport?)null,
        };
        var transport = transportOverride
            ?? flagTransport
            ?? savedTransport
            ?? (isUs ? AmazonTransport.Http : AmazonTransport.Curl);
        if (transportOverride is null
            && flagTransport is null
            && savedTransport is null
            && store.IsCurlPreferred(marketplace.Id))
        {
            transport = AmazonTransport.Curl;
        }

        // curl-impersonate already advertises the User-Agent that matches its
        // TLS/HTTP2 fingerprint. Overriding it (for example the old iPad UA on
        // the Safari 26 profile) makes the fingerprint incoherent and triggers
        // Amazon's "Continue shopping" anti-bot wall. Keep the profile UA on
        // curl, except for Firefox: Amazon.de rejects the Firefox 147 UA that
        // ships with curl-impersonate, while the current Firefox string works.
        var isFirefoxProfile = browser is BrowserProfile.Firefox133
            or BrowserProfile.Firefox135
            or BrowserProfile.Firefox144
            or BrowserProfile.Firefox147
            or BrowserProfile.Tor145;
        var explicitUserAgent = !string.IsNullOrWhiteSpace(settings.UserAgent)
            ? settings.UserAgent
            : !string.IsNullOrWhiteSpace(savedSettings.UserAgent)
                ? savedSettings.UserAgent
                : null;
        var userAgent = explicitUserAgent
            ?? (transport == AmazonTransport.Http
                ? defaultUserAgent
                : isFirefoxProfile
                    ? firefoxUserAgent
                    : null);

        return new AmazonOptions
        {
            TimeoutSeconds = settings.Timeout ?? savedSettings.TimeoutSeconds,
            Transport = transport,
            BrowserProfile = browser,
            UserAgent = userAgent,
            UseBrowserHeaders = settings.BrowserHeaders
                || savedSettings.BrowserHeaders
                || isUs
                || isJp
                || isEs,
            CookieHeader = settings.Cookie,
            OnHttpBlocked = reason =>
                store.RememberHttpFailure(marketplace.Id, reason),
        };
    }

    public static ValidationResult ValidateBrowser(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Success();
        }

        return Enum.TryParse<BrowserProfile>(value, ignoreCase: true, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error(
                $"Invalid browser: '{value}'. Values: {string.Join(", ", Enum.GetNames<BrowserProfile>())}");
    }
}
