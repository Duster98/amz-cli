using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmazonScraper.Cli.Library;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Builds a monitor plan from a saved-library binding. The plan is rebuilt on
/// every cycle so group membership changes are picked up live.
/// </summary>
public static class LibraryMonitorPlanBuilder
{
    public const int DefaultCooldownSeconds = 21_600;

    public static MonitorPlan Build(
        MonitorBinding binding,
        SavedLibraryDocument library,
        MonitorBindingDocument bindings)
    {
        return binding.IsGroup
            ? BuildGroup(binding, library, bindings)
            : BuildProduct(binding, library);
    }

    private static MonitorPlan BuildProduct(
        MonitorBinding binding,
        SavedLibraryDocument library)
    {
        var productId = binding.ProductId ?? string.Empty;
        var product = library.Products.FirstOrDefault(item =>
            string.Equals(item.Id, productId, StringComparison.Ordinal))
            ?? throw new MonitorException(
                $"Saved product not found: {productId}.");
        var containingGroups = library.Groups
            .Where(group => group.Items.Any(item =>
                string.Equals(
                    item.ProductId,
                    product.Id,
                    StringComparison.Ordinal)))
            .Select(group => $"'{group.Name}' ({group.Id})")
            .ToList();
        if (containingGroups.Count > 0)
        {
            throw new MonitorException(
                $"Product {product.Id} belongs to group " +
                $"{string.Join(", ", containingGroups)}. " +
                "Remove it from the group or monitor the group instead.");
        }

        var rule = BuildRule(binding, null);
        var target = new MonitorTargetPlan
        {
            Target = new MonitorTarget(product.Domain, product.Asin),
            Label = binding.Name,
            Rule = rule,
        };
        return new MonitorPlan
        {
            Groups = [],
            Ungrouped = [target],
            ConfigHash = HashBinding(binding, groupPlan: null),
        };
    }

    private static MonitorPlan BuildGroup(
        MonitorBinding binding,
        SavedLibraryDocument library,
        MonitorBindingDocument bindings)
    {
        var groupId = binding.GroupId ?? string.Empty;
        var group = library.Groups.FirstOrDefault(item =>
            string.Equals(item.Id, groupId, StringComparison.Ordinal))
            ?? throw new MonitorException(
                $"Saved group not found: {groupId}.");
        if (group.Items.Count == 0)
        {
            throw new MonitorException(
                $"Saved group '{group.Name}' is empty. Add at least one " +
                "product before monitoring it.");
        }

        var productIds = group.Items
            .Select(item => item.ProductId)
            .ToHashSet(StringComparer.Ordinal);
        var productBindings = bindings.Bindings
            .Where(item => string.Equals(
                item.Kind,
                MonitorBindingKind.Product,
                StringComparison.Ordinal))
            .Where(item => item.ProductId is not null
                && productIds.Contains(item.ProductId))
            .ToList();
        if (productBindings.Count > 0)
        {
            throw new MonitorException(
                $"Group '{group.Name}' contains products that are already " +
                $"monitored individually: " +
                $"{string.Join(", ", productBindings.Select(item => item.Id))}. " +
                "Remove those product bindings or exclude the products from " +
                "the group before monitoring it.");
        }

        var products = new Dictionary<string, SavedProduct>(StringComparer.Ordinal);
        foreach (var item in group.Items)
        {
            var product = library.Products.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    item.ProductId,
                    StringComparison.Ordinal))
                ?? throw new MonitorException(
                    $"Group '{group.Name}' references a missing saved product: " +
                    $"{item.ProductId}.");
            products[item.ProductId] = product;
        }

        var currencies = products.Values
            .Select(ProductCurrency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var targetCurrency = string.IsNullOrWhiteSpace(binding.Currency)
            ? currencies.Count == 1
                ? currencies[0]
                : throw new MonitorException(
                    $"Group '{group.Name}' contains multiple currencies " +
                    $"({string.Join(", ", currencies)}). " +
                    "Specify the target currency with --currency and provide " +
                    "an FX rate for each other currency with --fx CUR=RATE.")
            : binding.Currency.Trim().ToUpperInvariant();

        var fx = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [targetCurrency] = 1m,
        };
        foreach (var currency in currencies)
        {
            if (string.Equals(
                    currency,
                    targetCurrency,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!binding.Fx.TryGetValue(currency, out var rate) || rate <= 0)
            {
                throw new MonitorException(
                    $"Group '{group.Name}' contains {currency} but has no FX " +
                    $"rate to {targetCurrency}. Add --fx {currency}=<rate>.");
            }
            fx[currency] = rate;
        }

        var items = new List<MonitorTargetPlan>();
        foreach (var item in group.Items)
        {
            var product = products[item.ProductId];
            binding.ItemRules.TryGetValue(item.ProductId, out var itemRule);
            items.Add(new MonitorTargetPlan
            {
                Target = new MonitorTarget(product.Domain, product.Asin),
                Label = itemRule is null ? binding.Name : null,
                GroupId = group.Id,
                Quantity = Math.Max(1, item.Quantity),
                Rule = BuildRule(binding, itemRule),
            });
        }

        var groupPlan = new MonitorGroupPlan
        {
            Id = group.Id,
            Name = group.Name,
            Currency = targetCurrency,
            FxRates = fx,
            TotalMax = binding.TotalMax,
            AllowIncomplete = binding.AllowIncomplete ?? false,
            CooldownSeconds = binding.CooldownSeconds
                ?? DefaultCooldownSeconds,
            Items = items,
        };
        return new MonitorPlan
        {
            Groups = [groupPlan],
            Ungrouped = [],
            ConfigHash = HashBinding(binding, groupPlan),
        };
    }

    private static MonitorRule BuildRule(
        MonitorBinding binding,
        MonitorBindingItemRule? overrideRule)
    {
        var maxPrice = overrideRule?.MaxPrice ?? binding.MaxPrice;
        var dropPercent = overrideRule?.DropPercent ?? binding.DropPercent;
        var risePercent = overrideRule?.RisePercent ?? binding.RisePercent;
        var baseline = string.IsNullOrWhiteSpace(overrideRule?.Baseline)
            ? binding.Baseline
            : overrideRule!.Baseline;
        var baselinePrice = overrideRule?.BaselinePrice ?? binding.BaselinePrice;
        var cooldown = overrideRule?.CooldownSeconds
            ?? binding.CooldownSeconds
            ?? DefaultCooldownSeconds;

        if (maxPrice is <= 0)
        {
            throw new MonitorException("max_price must be greater than zero.");
        }
        if (dropPercent is <= 0 or > 100)
        {
            throw new MonitorException(
                "drop_percent must be between 0 and 100.");
        }
        if (risePercent is <= 0 or > 100)
        {
            throw new MonitorException(
                "rise_percent must be between 0 and 100.");
        }
        if (cooldown < 0)
        {
            throw new MonitorException("cooldown_seconds cannot be negative.");
        }

        var parsedBaseline = ParseBaseline(baseline, baselinePrice);
        if (parsedBaseline != PriceBaseline.Fixed && baselinePrice is not null)
        {
            throw new MonitorException(
                "baseline_price requires baseline 'fixed'.");
        }
        if (parsedBaseline == PriceBaseline.Fixed && baselinePrice is null)
        {
            throw new MonitorException(
                "baseline 'fixed' requires baseline_price.");
        }

        return new MonitorRule
        {
            MaxPrice = maxPrice,
            DropPercent = dropPercent,
            RisePercent = risePercent,
            Baseline = parsedBaseline,
            BaselinePrice = baselinePrice,
            CooldownSeconds = cooldown,
        };
    }

    private static PriceBaseline ParseBaseline(
        string? value,
        decimal? baselinePrice)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return baselinePrice is null
                ? PriceBaseline.Original
                : PriceBaseline.Fixed;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "original" => PriceBaseline.Original,
            "first_seen" => PriceBaseline.FirstSeen,
            "max_seen" => PriceBaseline.MaxSeen,
            "last_seen" => PriceBaseline.LastSeen,
            "fixed" => PriceBaseline.Fixed,
            _ => throw new MonitorException(
                $"Unsupported baseline: '{value}'. Values: original, " +
                "first_seen, max_seen, last_seen, fixed."),
        };
    }

    private static string ProductCurrency(SavedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Currency))
        {
            return product.Currency.Trim().ToUpperInvariant();
        }
        if (!string.IsNullOrWhiteSpace(product.Snapshot.Currency))
        {
            return product.Snapshot.Currency.Trim().ToUpperInvariant();
        }
        return MarketplaceRegistry.Resolve(product.Domain).CurrencyCode;
    }

    private static string HashBinding(
        MonitorBinding binding,
        MonitorGroupPlan? groupPlan)
    {
        var lines = new List<string>
        {
            binding.Id,
            binding.Kind,
            binding.ProductId ?? string.Empty,
            binding.GroupId ?? string.Empty,
            binding.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.DropPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.RisePercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.Baseline ?? string.Empty,
            binding.BaselinePrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.CooldownSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.TotalMax?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            binding.AllowIncomplete?.ToString() ?? string.Empty,
            groupPlan?.Currency ?? string.Empty,
        };
        foreach (var (currency, rate) in binding.Fx
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            lines.Add($"fx|{currency}|{rate.ToString(CultureInfo.InvariantCulture)}");
        }
        foreach (var (productId, rule) in binding.ItemRules
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            lines.Add(string.Join('|',
                "item",
                productId,
                rule.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.DropPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.RisePercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.Baseline ?? string.Empty,
                rule.BaselinePrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                rule.CooldownSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
        }

        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
