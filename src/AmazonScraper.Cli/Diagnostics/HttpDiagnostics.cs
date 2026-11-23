using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Cli.Diagnostics;

public sealed record HttpCurlDiagnostics(
    bool Available,
    string Version,
    string Runtime,
    string Mode,
    string Transport,
    string Error);

public sealed record HttpCooldownDiagnostics(
    string Domain,
    DateTimeOffset Until,
    string Reason);

public sealed record EffectiveHttpDiagnostics(
    string Domain,
    string Transport,
    string Browser,
    string BrowserDeals,
    string? UserAgent,
    bool BrowserHeaders,
    double TimeoutSeconds);

public sealed record HttpSettingsDiagnostics(
    HttpSettings Settings,
    HttpCurlDiagnostics Curl,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<HttpCooldownDiagnostics> Cooldowns,
    IReadOnlyList<EffectiveHttpDiagnostics> Effective);

/// <summary>
/// Builds the read-only report shared by <c>amz-cli settings show</c> and the
/// Web Settings page. It never performs a network request; the only external
/// command is the local <c>curl-impersonate --version</c> probe, cached briefly
/// because the Settings page refreshes the report after every save.
/// </summary>
public static class HttpDiagnostics
{
    private static readonly TimeSpan CurlCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly object CurlGate = new();
    private static SelfTestResult? _curlReport;
    private static DateTimeOffset _curlReportAt;

    public static HttpSettingsDiagnostics Build(
        HttpSettings settings,
        IReadOnlyList<string>? domains = null,
        TransportCooldownStore? cooldowns = null)
    {
        var normalized = settings.Normalize();
        var store = cooldowns ?? TransportCooldownStore.Default;
        var available = MarketplaceRegistry.All
            .Where(marketplace => marketplace.Available)
            .Select(marketplace => marketplace.Id)
            .ToArray();
        var requested = (domains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => MarketplaceRegistry.TryResolve(domain, out var marketplace)
                ? marketplace.Id
                : null)
            .Where(domain => domain is not null)
            .Select(domain => domain!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selected = requested.Length > 0 ? requested : available;

        var effective = selected
            .Select(domain =>
            {
                var marketplace = MarketplaceRegistry.Resolve(domain);
                var standard = AmazonOptionsFactory.Create(
                    new CommonSettings(),
                    marketplace,
                    cooldownStore: store,
                    saved: normalized);
                var deals = AmazonOptionsFactory.Create(
                    new CommonSettings(),
                    marketplace,
                    cooldownStore: store,
                    desktopLayout: true,
                    saved: normalized);
                return new EffectiveHttpDiagnostics(
                    domain,
                    standard.Transport == AmazonTransport.Curl
                        ? "curl"
                        : "http",
                    standard.BrowserProfile.ToString(),
                    deals.BrowserProfile.ToString(),
                    standard.UserAgent,
                    standard.UseBrowserHeaders,
                    standard.TimeoutSeconds);
            })
            .ToArray();

        var activeCooldowns = store.ListActive()
            .Select(item => new HttpCooldownDiagnostics(
                item.Domain,
                item.Until,
                item.Reason))
            .ToArray();

        var curl = CurlReport();
        return new HttpSettingsDiagnostics(
            normalized,
            new HttpCurlDiagnostics(
                curl.CurlAvailable,
                curl.CurlVersion,
                curl.CurlRuntimeIdentifier,
                curl.CurlMode,
                curl.Transport,
                curl.CurlError),
            HttpSettings.AvailableBrowsers,
            activeCooldowns,
            effective);
    }

    private static SelfTestResult CurlReport()
    {
        lock (CurlGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_curlReport is not null
                && now - _curlReportAt < CurlCacheTtl)
            {
                return _curlReport;
            }

            _curlReport = RuntimeDiagnostics.Run(VersionInfo.Informational);
            _curlReportAt = now;
            return _curlReport;
        }
    }
}
