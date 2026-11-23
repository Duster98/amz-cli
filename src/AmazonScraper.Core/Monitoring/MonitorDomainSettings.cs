using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Monitoring;

public sealed record MonitorLocation(string PostalCode, string CountryCode);

public sealed class MonitorDomainSettings
{
    public IReadOnlyDictionary<string, MonitorLocation> Locations { get; }

    public IReadOnlyDictionary<string, string> Cookies { get; }

    private MonitorDomainSettings(
        IReadOnlyDictionary<string, MonitorLocation> locations,
        IReadOnlyDictionary<string, string> cookies)
    {
        Locations = locations;
        Cookies = cookies;
    }

    public static MonitorDomainSettings Build(
        IReadOnlyCollection<string> targetDomains,
        string[] caps,
        string[] countries,
        string[] domainCookies,
        string? bareCookie)
    {
        var domains = new HashSet<string>(targetDomains, StringComparer.Ordinal);
        if (domains.Count == 0)
        {
            throw new MonitorException("No domains to configure.");
        }

        var singleDomain = domains.Count == 1 ? domains.First() : null;
        var locations = new Dictionary<string, MonitorLocation>(StringComparer.Ordinal);
        var countryOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var raw in caps)
        {
            var (domain, postal) = SplitDomainValue(raw, requireDomain: false);
            var effectiveDomain = domain ?? singleDomain
                ?? throw new MonitorException(
                    $"With multiple domains, specify the postal code per domain: --cap it:20121 (value: '{raw}').");
            EnsureDomain(domains, effectiveDomain, "--cap");
            if (locations.ContainsKey(effectiveDomain))
            {
                throw new MonitorException($"Duplicate postal code for domain '{effectiveDomain}'.");
            }

            locations[effectiveDomain] = new MonitorLocation(postal.Trim(), string.Empty);
        }

        foreach (var raw in countries)
        {
            var (domain, country) = SplitDomainValue(raw, requireDomain: false);
            var effectiveDomain = domain ?? singleDomain
                ?? throw new MonitorException(
                    $"With multiple domains, specify the country per domain: --country it:IT (value: '{raw}').");
            EnsureDomain(domains, effectiveDomain, "--country");
            if (countryOverrides.ContainsKey(effectiveDomain))
            {
                throw new MonitorException($"Duplicate country for domain '{effectiveDomain}'.");
            }

            var normalized = country.Trim().ToUpperInvariant();
            if (!PostalCodeValidator.IsCountryCode(normalized))
            {
                throw new MonitorException(
                    $"--country requires a two-letter ISO code for '{effectiveDomain}': '{country}'.");
            }

            countryOverrides[effectiveDomain] = normalized;
        }

        foreach (var domain in countryOverrides.Keys)
        {
            if (!locations.ContainsKey(domain))
            {
                throw new MonitorException(
                    $"You specified --country for '{domain}' but --cap {domain}:<POSTAL> is missing.");
            }
        }

        var normalizedLocations = new Dictionary<string, MonitorLocation>(StringComparer.Ordinal);
        foreach (var (domain, location) in locations)
        {
            var profile = MarketplaceRegistry.Resolve(domain);
            var country = countryOverrides.GetValueOrDefault(domain, profile.CountryCode);
            if (!PostalCodeValidator.TryNormalize(
                    country,
                    location.PostalCode,
                    out var normalizedCountry,
                    out var normalizedPostal,
                    out var error))
            {
                throw new MonitorException(
                    $"Invalid postal code for '{domain}': {error}");
            }

            normalizedLocations[domain] = new MonitorLocation(
                normalizedPostal,
                normalizedCountry);
        }

        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(bareCookie))
        {
            if (singleDomain is null)
            {
                throw new MonitorException(
                    "With multiple domains use --domain-cookie <DOMAIN>:<HEADER>; " +
                    "--cookie is ambiguous.");
            }

            cookies[singleDomain] = bareCookie.Trim();
        }

        foreach (var raw in domainCookies)
        {
            var (domain, cookie) = SplitDomainValue(raw, requireDomain: true);
            var effectiveDomain = domain
                ?? throw new MonitorException(
                    $"Specify the domain in the cookie: --domain-cookie it:<HEADER> (value: '{raw}').");
            EnsureDomain(domains, effectiveDomain, "--domain-cookie");
            if (cookies.ContainsKey(effectiveDomain))
            {
                throw new MonitorException(
                    $"Cookie already set for domain '{effectiveDomain}'.");
            }

            if (cookie.Length == 0)
            {
                throw new MonitorException(
                    $"Empty cookie for domain '{effectiveDomain}'.");
            }

            cookies[effectiveDomain] = cookie;
        }

        return new MonitorDomainSettings(normalizedLocations, cookies);
    }

    private static (string? Domain, string Value) SplitDomainValue(
        string raw,
        bool requireDomain)
    {
        var value = raw.Trim();
        var separator = value.IndexOf(':');
        if (separator > 0)
        {
            var prefix = value[..separator].Trim();
            if (MarketplaceRegistry.TryResolve(prefix, out var profile))
            {
                return (profile.Id, value[(separator + 1)..].Trim());
            }
        }

        if (requireDomain)
        {
            throw new MonitorException(
                $"Value without a domain: '{raw}'. Use <DOMAIN>:<VALUE>.");
        }

        return (null, value);
    }

    private static void EnsureDomain(
        HashSet<string> domains,
        string domain,
        string option)
    {
        if (!domains.Contains(domain))
        {
            throw new MonitorException(
                $"{option} specified for '{domain}', but that domain has no targets.");
        }
    }
}
