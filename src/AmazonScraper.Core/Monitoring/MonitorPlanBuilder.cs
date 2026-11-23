using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Monitoring;

public sealed class MonitorPlanBuildOptions
{
    public MonitorConfig? Config { get; init; }

    public IReadOnlyList<string> CliAsins { get; init; } = [];

    public string? AsinFile { get; init; }

    public required string DefaultDomain { get; init; }

    public decimal? CliMaxPrice { get; init; }

    public decimal? CliDropPercent { get; init; }

    public decimal? CliRisePercent { get; init; }

    public string? CliBaseline { get; init; }

    public int? CliCooldownSeconds { get; init; }

    public bool ForceInterval { get; init; }

    public string? CliScheduleMode { get; init; }

    public int? CliWindowSeconds { get; init; }

    public int? CliMaxRequests { get; init; }

    public int? CliMinGapSeconds { get; init; }

    public int? CliMaxGapSeconds { get; init; }

    public Action<string>? Warning { get; init; }
}

/// <summary>
/// Merges JSON configuration and CLI targets into one validated plan. Every
/// (domain, ASIN) appears exactly once; an item can belong to at most one
/// group in this version.
/// </summary>
public static partial class MonitorPlanBuilder
{
    public const int MaxTargets = 100;
    public const int DefaultCooldownSeconds = 21_600;

    public static MonitorPlan Build(MonitorPlanBuildOptions options)
    {
        var config = options.Config ?? new MonitorConfig();
        if (config.SchemaVersion != 1)
        {
            throw new MonitorException(
                $"Unsupported configuration version: {config.SchemaVersion}. " +
                "The supported version is 1.");
        }

        _ = ResolveProfile(options.DefaultDomain);

        var groups = new List<MonitorGroupPlan>();
        var ungrouped = new List<MonitorTargetPlan>();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in config.Items ?? [])
        {
            var target = ParseConfigTarget(item, defaultDomain: options.DefaultDomain);
            AddTarget(
                target,
                BuildRule(item, DefaultCooldownSeconds),
                item.Label,
                groupId: null,
                origin: "config",
                ungrouped,
                origins,
                options.Warning,
                throwOnDuplicate: true,
                minGapSeconds: item.MinGapSeconds,
                maxGapSeconds: item.MaxGapSeconds);
        }

        foreach (var groupConfig in config.Groups ?? [])
        {
            var id = ValidateGroupId(groupConfig.Id);
            if (!groupIds.Add(id))
            {
                throw new MonitorException($"Duplicate group ID: '{id}'.");
            }
            if (string.Equals(id, "ungrouped", StringComparison.OrdinalIgnoreCase))
            {
                throw new MonitorException(
                    "The group ID 'ungrouped' is reserved for the per-group layout.");
            }

            if (groupConfig.Items is null || groupConfig.Items.Count == 0)
            {
                throw new MonitorException($"Group '{id}' contains no products.");
            }
            if (groupConfig.TotalMax is { } totalMax && totalMax <= 0)
            {
                throw new MonitorException(
                    $"Group '{id}' has an invalid total_max: {totalMax}.");
            }
            if (groupConfig.CooldownSeconds is { } cooldown && cooldown < 0)
            {
                throw new MonitorException(
                    $"Group '{id}' has a negative cooldown_seconds.");
            }

            var groupItems = new List<MonitorTargetPlan>();
            foreach (var item in groupConfig.Items)
            {
                if (item.Quantity < 1)
                {
                    throw new MonitorException(
                        $"Group '{id}' has an invalid quantity for '{item.Asin}': " +
                        $"{item.Quantity}. It must be at least 1.");
                }
                if (item.MinGapSeconds is not null || item.MaxGapSeconds is not null)
                {
                    throw new MonitorException(
                        $"Group '{id}' defines min_gap/max_gap on '{item.Asin}'. " +
                        "Configure gaps on the group, not on individual items.");
                }

                var groupDefaultDomain = string.IsNullOrWhiteSpace(groupConfig.Domain)
                    ? options.DefaultDomain
                    : groupConfig.Domain;
                var target = ParseConfigTarget(item, groupDefaultDomain);
                var plan = new MonitorTargetPlan
                {
                    Target = target,
                    Label = item.Label,
                    GroupId = id,
                    Rule = BuildRule(item, DefaultCooldownSeconds),
                };

                AddTarget(
                    target,
                    plan.Rule,
                    plan.Label,
                    id,
                    origin: $"group '{id}'",
                    groupItems,
                    origins,
                    options.Warning,
                    throwOnDuplicate: true,
                    quantity: item.Quantity);
            }

            var profiles = groupItems
                .Select(item => ResolveProfile(item.Target.Domain))
                .ToArray();
            var currencies = profiles
                .Select(profile => profile.CurrencyCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (currencies.Length != 1)
            {
                throw new MonitorException(
                    $"Group '{id}' contains different currencies " +
                    $"({string.Join(", ", currencies)}). " +
                    "Create separate groups: FX conversion is not supported.");
            }

            var currency = currencies[0];
            if (!string.IsNullOrWhiteSpace(groupConfig.Currency)
                && !string.Equals(
                    groupConfig.Currency.Trim(),
                    currency,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new MonitorException(
                    $"Group '{id}' declares currency '{groupConfig.Currency}' " +
                    $"but the products use {currency}.");
            }

            groups.Add(new MonitorGroupPlan
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(groupConfig.Name)
                    ? id
                    : groupConfig.Name.Trim(),
                Currency = currency,
                FxRates = new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [currency] = 1m,
                },
                TotalMax = groupConfig.TotalMax,
                AllowIncomplete = groupConfig.AllowIncomplete ?? false,
                CooldownSeconds = groupConfig.CooldownSeconds
                    ?? DefaultCooldownSeconds,
                MinGapSeconds = groupConfig.MinGapSeconds,
                MaxGapSeconds = groupConfig.MaxGapSeconds,
                Items = groupItems,
            });
        }

        var cliRule = BuildCliRule(options);
        var cliTargets = new List<MonitorTarget>();
        foreach (var value in options.CliAsins)
        {
            foreach (var token in TokenSeparator().Split(value))
            {
                if (token.Length > 0)
                {
                    cliTargets.Add(MonitorTargetParser.Parse(
                        token,
                        options.DefaultDomain));
                }
            }
        }

        if (options.AsinFile is not null)
        {
            cliTargets.AddRange(MonitorTargetParser.ReadFile(
                options.AsinFile,
                options.DefaultDomain,
                options.Warning));
        }

        if (cliRule.MaxPrice is not null && cliTargets.Count > 0)
        {
            var currencies = cliTargets
                .Select(target => ResolveProfile(target.Domain).CurrencyCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (currencies.Length > 1)
            {
                throw new MonitorException(
                    "--max-price requires a single currency across CLI targets; " +
                    "use the JSON configuration for per-domain thresholds.");
            }
        }

        foreach (var target in cliTargets)
        {
            if (origins.TryGetValue(target.Key, out var existing)
                && !string.Equals(existing, "CLI", StringComparison.Ordinal))
            {
                throw new MonitorException(
                    $"Target {target.Key} is already defined in {existing}; " +
                    "do not repeat it from the CLI.");
            }

            AddTarget(
                target,
                cliRule,
                label: null,
                groupId: null,
                origin: "CLI",
                ungrouped,
                origins,
                options.Warning,
                throwOnDuplicate: false);
        }

        var allTargets = groups.SelectMany(group => group.Items)
            .Concat(ungrouped)
            .ToArray();
        if (allTargets.Length == 0)
        {
            throw new MonitorException(
                "No products to monitor. Pass ASINs, --asin-file or --config.");
        }
        if (allTargets.Length > MaxTargets)
        {
            throw new MonitorException(
                $"Too many products: {allTargets.Length}. The maximum is {MaxTargets} per monitor.");
        }

        var schedule = BuildSchedule(config, options, groups, ungrouped);
        return new MonitorPlan
        {
            Groups = groups,
            Ungrouped = ungrouped,
            ConfigHash = ComputeConfigHash(groups, ungrouped),
            Schedule = schedule,
            ScheduleHash = schedule is null
                ? null
                : ComputeScheduleHash(schedule),
        };
    }

    private static void AddTarget(
        MonitorTarget target,
        MonitorRule rule,
        string? label,
        string? groupId,
        string origin,
        List<MonitorTargetPlan> destination,
        Dictionary<string, string> origins,
        Action<string>? warning,
        bool throwOnDuplicate,
        int quantity = 1,
        int? minGapSeconds = null,
        int? maxGapSeconds = null)
    {
        if (origins.TryGetValue(target.Key, out var existingOrigin))
        {
            if (throwOnDuplicate)
            {
                throw new MonitorException(
                    $"Duplicate product: {target.Key} (already in {existingOrigin}, " +
                    $"now in {origin}). A product can belong to only one group.");
            }

            warning?.Invoke(
                $"Duplicate target ignored: {target.Key} (already in {existingOrigin}).");
            return;
        }

        origins[target.Key] = origin;
        destination.Add(new MonitorTargetPlan
        {
            Target = target,
            Label = label,
            GroupId = groupId,
            Quantity = quantity,
            MinGapSeconds = minGapSeconds,
            MaxGapSeconds = maxGapSeconds,
            Rule = rule,
        });
    }

    private static MonitorTarget ParseConfigTarget(
        MonitorItemConfig item,
        string defaultDomain)
    {
        if (string.IsNullOrWhiteSpace(item.Asin))
        {
            throw new MonitorException(
                $"Configuration: missing ASIN for item '{item.Label ?? "?"}'.");
        }

        return MonitorTargetParser.Parse(
            item.Asin,
            item.Domain,
            defaultDomain);
    }

    private static MonitorRule BuildCliRule(MonitorPlanBuildOptions options)
    {
        var baselineText = options.CliBaseline;
        return BuildRule(
            new MonitorItemConfig
            {
                Asin = "CLI",
                MaxPrice = options.CliMaxPrice,
                DropPercent = options.CliDropPercent,
                RisePercent = options.CliRisePercent,
                Baseline = baselineText,
                CooldownSeconds = options.CliCooldownSeconds,
            },
            DefaultCooldownSeconds);
    }

    private static MonitorRule BuildRule(
        MonitorItemConfig item,
        int defaultCooldown)
    {
        if (item.MaxPrice is { } maxPrice && maxPrice <= 0)
        {
            throw new MonitorException(
                $"invalid max_price for '{item.Asin}': {maxPrice}.");
        }
        if (item.DropPercent is { } drop)
        {
            if (drop <= 0 || drop > 100)
            {
                throw new MonitorException(
                    $"invalid drop_percent for '{item.Asin}': {drop}. " +
                    "It must be between 0 and 100.");
            }
        }
        if (item.RisePercent is { } rise)
        {
            if (rise <= 0 || rise > 100)
            {
                throw new MonitorException(
                    $"invalid rise_percent for '{item.Asin}': {rise}. " +
                    "It must be between 0 and 100.");
            }
        }
        if (item.CooldownSeconds is { } cooldown && cooldown < 0)
        {
            throw new MonitorException(
                $"invalid cooldown_seconds for '{item.Asin}': {cooldown}.");
        }
        if (item.BaselinePrice is { } fixedPrice && fixedPrice <= 0)
        {
            throw new MonitorException(
                $"invalid baseline_price for '{item.Asin}': {fixedPrice}.");
        }

        var baseline = ParseBaseline(item.Baseline, item.BaselinePrice);
        if (baseline != PriceBaseline.Fixed && item.BaselinePrice is not null)
        {
            throw new MonitorException(
                $"For '{item.Asin}' use baseline 'fixed' with baseline_price, " +
                "or remove baseline_price.");
        }
        if (baseline == PriceBaseline.Fixed && item.BaselinePrice is null)
        {
            throw new MonitorException(
                $"For '{item.Asin}' baseline 'fixed' requires baseline_price.");
        }

        return new MonitorRule
        {
            MaxPrice = item.MaxPrice,
            DropPercent = item.DropPercent,
            RisePercent = item.RisePercent,
            Baseline = baseline,
            BaselinePrice = item.BaselinePrice,
            CooldownSeconds = item.CooldownSeconds ?? defaultCooldown,
        };
    }

    private static PriceBaseline ParseBaseline(string? value, decimal? baselinePrice)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return baselinePrice is null ? PriceBaseline.Original : PriceBaseline.Fixed;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "original" => PriceBaseline.Original,
            "first_seen" => PriceBaseline.FirstSeen,
            "max_seen" => PriceBaseline.MaxSeen,
            "last_seen" => PriceBaseline.LastSeen,
            "fixed" => PriceBaseline.Fixed,
            _ => throw new MonitorException(
                $"Unsupported baseline: '{value}'. " +
                "Values: original, first_seen, max_seen, last_seen, fixed."),
        };
    }

    private static string ValidateGroupId(string id)
    {
        var trimmed = id.Trim();
        if (trimmed.Length == 0 || !GroupId().IsMatch(trimmed))
        {
            throw new MonitorException(
                $"Invalid group ID: '{id}'. Use letters, digits, '-' and '_'.");
        }

        return trimmed;
    }

    private static IMarketplaceProfile ResolveProfile(string domain)
    {
        try
        {
            return MarketplaceRegistry.Resolve(domain);
        }
        catch (MarketplaceException ex)
        {
            throw new MonitorException(ex.Message);
        }
    }

    private static MonitorSchedulePlan? BuildSchedule(
        MonitorConfig config,
        MonitorPlanBuildOptions options,
        IReadOnlyList<MonitorGroupPlan> groups,
        IReadOnlyList<MonitorTargetPlan> ungrouped)
    {
        var hasCli = options.CliScheduleMode is not null
            || options.CliWindowSeconds is not null
            || options.CliMaxRequests is not null
            || options.CliMinGapSeconds is not null
            || options.CliMaxGapSeconds is not null;

        if (options.ForceInterval)
        {
            if (config.Schedule is not null)
            {
                options.Warning?.Invoke(
                    "Timer mode requested from the CLI: schedule block ignored.");
            }
            EnsureNoUnitGaps(groups, ungrouped);
            return null;
        }

        var configured = config.Schedule;
        if (configured is null && !hasCli)
        {
            EnsureNoUnitGaps(groups, ungrouped);
            return null;
        }

        var mode = options.CliScheduleMode
            ?? configured?.Mode
            ?? "adaptive";
        if (!string.Equals(mode, "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            throw new MonitorException(
                "schedule.mode is not supported. Use 'adaptive'.");
        }

        if (configured is not null && config.TimerSeconds is not null)
        {
            options.Warning?.Invoke(
                "Configuration with both timer_seconds and schedule: using adaptive mode.");
        }

        var windowSeconds = options.CliWindowSeconds
            ?? configured?.WindowSeconds
            ?? 86_400;
        var maxRequests = options.CliMaxRequests
            ?? configured?.MaxRequests
            ?? throw new MonitorException(
                "schedule.max_requests is required in adaptive mode.");
        var minGapSeconds = options.CliMinGapSeconds
            ?? configured?.MinGapSeconds
            ?? 600;
        var maxGapSeconds = options.CliMaxGapSeconds
            ?? configured?.MaxGapSeconds
            ?? minGapSeconds * 2;

        ValidateScheduleBounds(
            windowSeconds,
            maxRequests,
            minGapSeconds,
            maxGapSeconds,
            "schedule");

        var units = new List<MonitorUnitPlan>();
        foreach (var item in ungrouped)
        {
            var unitMin = item.MinGapSeconds ?? minGapSeconds;
            var unitMax = item.MaxGapSeconds
                ?? (item.MinGapSeconds is null
                    ? maxGapSeconds
                    : unitMin * 2);
            ValidateUnitGaps(unitMin, unitMax, $"item {item.Target.Key}");
            units.Add(new MonitorUnitPlan
            {
                Id = $"item:{item.Target.Key}",
                Kind = MonitorUnitKind.Item,
                Cost = 1,
                MinGapSeconds = unitMin,
                MaxGapSeconds = unitMax,
                Item = item,
            });
        }

        foreach (var group in groups)
        {
            var unitMin = group.MinGapSeconds ?? minGapSeconds;
            var unitMax = group.MaxGapSeconds
                ?? (group.MinGapSeconds is null
                    ? maxGapSeconds
                    : unitMin * 2);
            ValidateUnitGaps(unitMin, unitMax, $"group '{group.Id}'");
            units.Add(new MonitorUnitPlan
            {
                Id = $"group:{group.Id}",
                Kind = MonitorUnitKind.Group,
                Cost = group.Items.Count,
                MinGapSeconds = unitMin,
                MaxGapSeconds = unitMax,
                Group = group,
            });
        }

        if (units.Count == 0)
        {
            throw new MonitorException(
                "Adaptive mode requires at least one unit to monitor.");
        }

        var maxUnitCost = units.Max(unit => unit.Cost);
        if (maxRequests < maxUnitCost)
        {
            throw new MonitorException(
                $"schedule.max_requests ({maxRequests}) is lower than the cost " +
                $"of the largest unit ({maxUnitCost} requests): " +
                "that group could never run.");
        }
        if (maxRequests < units.Count)
        {
            options.Warning?.Invoke(
                $"The budget ({maxRequests}) is lower than the number of units " +
                $"({units.Count}): some units may not run.");
        }

        return new MonitorSchedulePlan
        {
            WindowSeconds = windowSeconds,
            MaxRequests = maxRequests,
            DefaultMinGapSeconds = minGapSeconds,
            DefaultMaxGapSeconds = maxGapSeconds,
            Units = units,
        };
    }

    private static void ValidateScheduleBounds(
        int windowSeconds,
        int maxRequests,
        int minGapSeconds,
        int maxGapSeconds,
        string label)
    {
        if (windowSeconds is < 60 or > 2_592_000)
        {
            throw new MonitorException(
                $"{label}.window_seconds must be between 60 and 2592000 seconds.");
        }
        if (maxRequests is < 1 or > 1_000_000)
        {
            throw new MonitorException(
                $"{label}.max_requests must be between 1 and 1000000.");
        }
        if (minGapSeconds is < 1 or > 2_592_000)
        {
            throw new MonitorException(
                $"{label}.min_gap_seconds must be between 1 and 2592000 seconds.");
        }
        if (maxGapSeconds < minGapSeconds || maxGapSeconds > 2_592_000)
        {
            throw new MonitorException(
                $"{label}.max_gap_seconds must be between min_gap_seconds and 2592000 seconds.");
        }
    }

    private static void ValidateUnitGaps(
        int minGapSeconds,
        int maxGapSeconds,
        string unit)
    {
        if (minGapSeconds < 1)
        {
            throw new MonitorException(
                $"Invalid minimum gap for {unit}: {minGapSeconds}.");
        }
        if (maxGapSeconds < minGapSeconds)
        {
            throw new MonitorException(
                $"Invalid maximum gap for {unit}: {maxGapSeconds} < {minGapSeconds}.");
        }
    }

    private static void EnsureNoUnitGaps(
        IReadOnlyList<MonitorGroupPlan> groups,
        IReadOnlyList<MonitorTargetPlan> ungrouped)
    {
        foreach (var item in ungrouped)
        {
            if (item.MinGapSeconds is not null || item.MaxGapSeconds is not null)
            {
                throw new MonitorException(
                    $"Item {item.Target.Key} defines min_gap/max_gap " +
                    "without schedule.mode=adaptive.");
            }
        }
        foreach (var group in groups)
        {
            if (group.MinGapSeconds is not null || group.MaxGapSeconds is not null)
            {
                throw new MonitorException(
                    $"Group '{group.Id}' defines min_gap/max_gap " +
                    "without schedule.mode=adaptive.");
            }
        }
    }

    private static string ComputeScheduleHash(MonitorSchedulePlan schedule)
    {
        var lines = new List<string>
        {
            string.Join(
                '|',
                "schedule",
                schedule.WindowSeconds.ToString(CultureInfo.InvariantCulture),
                schedule.MaxRequests.ToString(CultureInfo.InvariantCulture),
                schedule.DefaultMinGapSeconds.ToString(CultureInfo.InvariantCulture),
                schedule.DefaultMaxGapSeconds.ToString(CultureInfo.InvariantCulture)),
        };
        foreach (var unit in schedule.Units
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            lines.Add(string.Join(
                '|',
                "unit",
                unit.Id,
                unit.Kind,
                unit.Cost.ToString(CultureInfo.InvariantCulture),
                unit.MinGapSeconds.ToString(CultureInfo.InvariantCulture),
                unit.MaxGapSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeConfigHash(
        IReadOnlyList<MonitorGroupPlan> groups,
        IReadOnlyList<MonitorTargetPlan> ungrouped)
    {
        var lines = new List<string>();
        foreach (var target in groups.SelectMany(group => group.Items)
                     .Concat(ungrouped)
                     .OrderBy(item => item.Target.Key, StringComparer.Ordinal))
        {
            var rule = target.Rule;
            lines.Add(string.Join(
                '|',
                "item",
                target.Target.Key,
                target.GroupId ?? string.Empty,
                target.Quantity.ToString(CultureInfo.InvariantCulture),
                rule.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.DropPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.RisePercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.Baseline,
                rule.BaselinePrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.CooldownSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var group in groups.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            lines.Add(string.Join(
                '|',
                "group",
                group.Id,
                group.Currency,
                group.TotalMax?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                group.AllowIncomplete,
                group.CooldownSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex GroupId();

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex TokenSeparator();
}
