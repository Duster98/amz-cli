using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Parses ASINs from bare values, domain prefixes and Amazon URLs, and reads
/// the optional ASIN file (plain text or CSV/TSV with a domain/asin header).
/// </summary>
public static partial class MonitorTargetParser
{
    public static MonitorTarget Parse(string value, string defaultDomain) =>
        Parse(value, explicitDomain: null, defaultDomain);

    public static MonitorTarget Parse(
        string value,
        string? explicitDomain,
        string defaultDomain)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            throw new MonitorException("Empty ASIN.");
        }

        var explicitOrNull = string.IsNullOrWhiteSpace(explicitDomain)
            ? null
            : explicitDomain;

        if (text.StartsWith("/dp/", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("/gp/", StringComparison.OrdinalIgnoreCase))
        {
            var profile = ResolveDomain(explicitOrNull ?? defaultDomain);
            return new MonitorTarget(profile.Id, ExtractAsin(text));
        }

        if (LooksLikeUrl(text))
        {
            return ParseUrl(text, defaultDomain);
        }

        var separator = text.IndexOf(':');
        if (separator > 0)
        {
            var prefix = text[..separator].Trim();
            var rest = text[(separator + 1)..].Trim();
            if (MarketplaceRegistry.TryResolve(prefix, out var prefixed))
            {
                if (LooksLikeUrl(rest))
                {
                    var parsed = ParseUrl(rest, prefixed.Id);
                    return parsed with { Domain = prefixed.Id };
                }

                return new MonitorTarget(prefixed.Id, NormalizeAsin(rest));
            }

            throw new MonitorException(
                $"Unsupported domain: '{prefix}'. " +
                $"Available domains: {string.Join(", ", MarketplaceRegistry.AvailableIds)}.");
        }

        var domain = ResolveDomain(explicitOrNull ?? defaultDomain);
        return new MonitorTarget(domain.Id, NormalizeAsin(text));
    }

    public static IReadOnlyList<MonitorTarget> ReadFile(
        string path,
        string defaultDomain,
        Action<string>? warning = null)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MonitorException(
                $"Unable to read ASIN file '{path}': {ex.Message}");
        }

        var meaningful = lines
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(item => !IsIgnored(item.Line))
            .ToArray();

        if (meaningful.Length == 0)
        {
            throw new MonitorException($"ASIN file '{path}' is empty.");
        }

        var delimiter = DetectDelimiter(meaningful[0].Line);
        var headerFields = delimiter is null
            ? null
            : SplitDelimited(meaningful[0].Line, delimiter.Value);
        var hasHeader = headerFields is not null
            && headerFields.Any(field => field.Equals("domain", StringComparison.OrdinalIgnoreCase))
            && headerFields.Any(field => field.Equals("asin", StringComparison.OrdinalIgnoreCase));

        var targets = new List<MonitorTarget>();
        if (hasHeader)
        {
            var domainIndex = headerFields!.FindIndex(
                field => field.Equals("domain", StringComparison.OrdinalIgnoreCase));
            var asinIndex = headerFields.FindIndex(
                field => field.Equals("asin", StringComparison.OrdinalIgnoreCase));

            foreach (var (line, number) in meaningful.Skip(1))
            {
                var fields = SplitDelimited(line, delimiter!.Value);
                var asin = asinIndex < fields.Count ? fields[asinIndex].Trim() : string.Empty;
                var domain = domainIndex < fields.Count ? fields[domainIndex].Trim() : string.Empty;
                if (asin.Length == 0)
                {
                    throw new MonitorException(
                        $"ASIN file '{path}', line {number}: missing ASIN.");
                }

                try
                {
                    targets.Add(Parse(
                        asin,
                        domain.Length == 0 ? null : domain,
                        defaultDomain));
                }
                catch (MonitorException ex)
                {
                    throw new MonitorException(
                        $"ASIN file '{path}', line {number}: {ex.Message}");
                }
            }

            WarnDuplicates(targets, warning);
            return targets;
        }

        foreach (var (line, number) in meaningful)
        {
            var content = StripInlineComment(line);
            if (content.Length == 0)
            {
                continue;
            }

            foreach (var token in TokenSeparator().Split(content))
            {
                if (token.Length == 0)
                {
                    continue;
                }

                try
                {
                    targets.Add(Parse(token, defaultDomain));
                }
                catch (MonitorException ex)
                {
                    throw new MonitorException(
                        $"ASIN file '{path}', line {number}: {ex.Message}");
                }
            }
        }

        WarnDuplicates(targets, warning);
        return targets;
    }

    private static MonitorTarget ParseUrl(string value, string defaultDomain)
    {
        var candidate = value;
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate.TrimStart('/');
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MonitorException($"Invalid Amazon URL: '{value}'.");
        }

        if (!MarketplaceRegistry.TryResolve(uri.Host, out var profile))
        {
            throw new MonitorException(
                $"Unsupported domain in URL '{value}'. " +
                $"Available domains: {string.Join(", ", MarketplaceRegistry.AvailableIds)}.");
        }

        return new MonitorTarget(profile.Id, ExtractAsin(uri.AbsolutePath));
    }

    private static string ExtractAsin(string value)
    {
        var match = AsinInUrl().Match(value);
        if (!match.Success)
        {
            throw new MonitorException(
                $"No ASIN found in '{value}'. Use a /dp/<ASIN> or /gp/product/<ASIN> URL.");
        }

        return NormalizeAsin(match.Groups[1].Value);
    }

    private static string NormalizeAsin(string value)
    {
        var asin = value.Trim().ToUpperInvariant();
        if (!Asin().IsMatch(asin))
        {
            throw new MonitorException(
                $"Invalid ASIN: '{value}'. It must be 10 alphanumeric characters.");
        }

        return asin;
    }

    private static IMarketplaceProfile ResolveDomain(string value)
    {
        if (!MarketplaceRegistry.TryResolve(value, out var profile))
        {
            throw new MonitorException(
                $"Unsupported domain: '{value}'. " +
                $"Available domains: {string.Join(", ", MarketplaceRegistry.AvailableIds)}.");
        }

        return profile;
    }

    private static bool LooksLikeUrl(string value) =>
        value.Contains("://", StringComparison.Ordinal)
        || (value.Contains("amazon.", StringComparison.OrdinalIgnoreCase)
            && (value.Contains("/dp/", StringComparison.OrdinalIgnoreCase)
                || value.Contains("/gp/", StringComparison.OrdinalIgnoreCase)));

    private static bool IsIgnored(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static string StripInlineComment(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '#')
            {
                continue;
            }

            if (index == 0 || char.IsWhiteSpace(line[index - 1]))
            {
                return line[..index].Trim();
            }
        }

        return line.Trim();
    }

    private static char? DetectDelimiter(string line)
    {
        if (line.Contains('\t'))
        {
            return '\t';
        }

        return line.Contains(',') ? ',' : null;
    }

    private static List<string> SplitDelimited(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (character == delimiter && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static void WarnDuplicates(
        IEnumerable<MonitorTarget> targets,
        Action<string>? warning)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!seen.Add(target.Key))
            {
                warning?.Invoke($"Duplicate target ignored: {target.Key}");
            }
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9]{10}$")]
    private static partial Regex Asin();

    [GeneratedRegex(
        @"(?:/dp/|/gp/product/|/gp/aw/d/)([A-Za-z0-9]{10})",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsinInUrl();

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex TokenSeparator();
}
