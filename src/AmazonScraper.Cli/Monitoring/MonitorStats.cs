using System.Text.Json;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed record MonitorStatsTarget(
    string Domain,
    string Asin,
    string? GroupId,
    string? Label,
    string? Currency,
    decimal? FirstPrice,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? LastPrice,
    string? LastSeenAt,
    int Observations,
    int AlertedRules);

public sealed record MonitorStatsGroup(
    string Id,
    string? Name,
    string? Currency,
    int Items,
    decimal? LastTotal,
    decimal? MinTotal,
    decimal? MaxTotal,
    string? LastSeenAt,
    int Observations,
    int AlertedRules);

public sealed record MonitorStatsReport(
    string StatePath,
    string? LastSeenAt,
    int Targets,
    int Groups,
    int AlertedRules,
    IReadOnlyList<MonitorStatsTarget> TargetRows,
    IReadOnlyList<MonitorStatsGroup> GroupRows);

/// <summary>
/// Local-only summary of the monitor state. It reads the state JSON and never
/// contacts Amazon or writes files.
/// </summary>
public static class MonitorStatsService
{
    public static MonitorStatsReport Build(
        MonitorPaths paths,
        IEnumerable<string>? dataFiles = null)
    {
        var state = MonitorStateStore.TryRead(paths.StatePath);
        if (state is null)
        {
            return new MonitorStatsReport(
                paths.StatePath,
                null,
                0,
                0,
                0,
                [],
                []);
        }

        var history = CountHistory(dataFiles);
        var targets = new List<MonitorStatsTarget>();
        foreach (var (key, target) in state.Targets
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var separator = key.IndexOf(':');
            var domain = separator > 0 ? key[..separator] : key;
            var asin = separator > 0 ? key[(separator + 1)..] : key;
            var currency = target.Currency;
            if (string.IsNullOrWhiteSpace(currency)
                && MarketplaceRegistry.TryResolve(domain, out var profile))
            {
                currency = profile.CurrencyCode;
            }

            targets.Add(new MonitorStatsTarget(
                domain,
                asin,
                GroupId: target.GroupId,
                Label: target.Label,
                Currency: currency,
                FirstPrice: target.FirstSeenPrice,
                MinPrice: target.MinSeenPrice,
                MaxPrice: target.MaxSeenPrice,
                LastPrice: target.LastPrice,
                LastSeenAt: FormatTimestamp(target.LastSeenAt),
                Observations: Math.Max(
                    target.Observations,
                    history.Targets.GetValueOrDefault(key)),
                AlertedRules: target.Rules.Values.Count(
                    rule => rule.LastAlertAt is not null)));
        }

        var groups = new List<MonitorStatsGroup>();
        foreach (var (id, group) in state.Groups
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            groups.Add(new MonitorStatsGroup(
                id,
                group.Name,
                group.Currency,
                group.Items,
                group.LastTotal,
                group.MinTotal,
                group.MaxTotal,
                FormatTimestamp(group.LastSeenAt),
                Observations: Math.Max(
                    group.Observations,
                    history.Groups.GetValueOrDefault(id)),
                AlertedRules: group.Rules.Values.Count(
                    rule => rule.LastAlertAt is not null)));
        }

        var lastSeen = state.Targets.Values
            .Select(target => target.LastSeenAt)
            .Concat(state.Groups.Values.Select(group => group.LastSeenAt))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new MonitorStatsReport(
            paths.StatePath,
            lastSeen == default ? null : FormatTimestamp(lastSeen),
            targets.Count,
            groups.Count,
            targets.Sum(target => target.AlertedRules)
                + groups.Sum(group => group.AlertedRules),
            targets,
            groups);
    }

    private static HistoryCounts CountHistory(IEnumerable<string>? dataFiles)
    {
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        if (dataFiles is null)
        {
            return new HistoryCounts(targets, groups);
        }

        foreach (var file in dataFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file)
                || !string.Equals(
                    Path.GetExtension(file),
                    ".jsonl",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement;
                        var recordType = root.TryGetProperty(
                                "record_type",
                                out var typeElement)
                            ? typeElement.GetString()
                            : null;
                        if (string.Equals(
                                recordType,
                                "item",
                                StringComparison.Ordinal))
                        {
                            var domain = root.TryGetProperty(
                                    "domain",
                                    out var domainElement)
                                ? domainElement.GetString()
                                : null;
                            var asin = root.TryGetProperty(
                                    "asin",
                                    out var asinElement)
                                ? asinElement.GetString()
                                : null;
                            if (!string.IsNullOrWhiteSpace(domain)
                                && !string.IsNullOrWhiteSpace(asin))
                            {
                                var key = $"{domain}:{asin}";
                                targets[key] = targets.GetValueOrDefault(key) + 1;
                            }
                        }
                        else if (string.Equals(
                                     recordType,
                                     "group",
                                     StringComparison.Ordinal))
                        {
                            var groupId = root.TryGetProperty(
                                    "group_id",
                                    out var groupElement)
                                ? groupElement.GetString()
                                : null;
                            if (!string.IsNullOrWhiteSpace(groupId))
                            {
                                groups[groupId] =
                                    groups.GetValueOrDefault(groupId) + 1;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore a malformed history line.
                    }
                }
            }
            catch (Exception)
            {
                // A history file can be rotated or locked; keep the state count.
            }
        }

        return new HistoryCounts(targets, groups);
    }

    private sealed record HistoryCounts(
        IReadOnlyDictionary<string, int> Targets,
        IReadOnlyDictionary<string, int> Groups);

    private static string? FormatTimestamp(DateTimeOffset? value) =>
        value is null
            ? null
            : MonitorObservationFactory.FormatTimestamp(value.Value);
}
