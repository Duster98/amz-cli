using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmazonScraper.Cli.Library;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed record MonitorImportResult(
    int ProductsAdded,
    int GroupsImported,
    int ProductBindings,
    int GroupBindings,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Imports the targets and thresholds of a legacy monitor configuration into
/// the shared saved-products library and monitor bindings. It does not start
/// monitoring and does not delete the original configuration.
/// </summary>
public static class MonitorImportService
{
    public static MonitorImportResult Import(
        MonitorConfig config,
        string defaultDomain,
        SavedLibraryStore libraryStore,
        MonitorBindingStore bindingStore,
        bool dryRun)
    {
        if (config.SchemaVersion != 1)
        {
            throw new MonitorException(
                $"Unsupported configuration version: {config.SchemaVersion}. " +
                "The supported version is 1.");
        }

        var current = libraryStore.Read();
        var now = DateTimeOffset.UtcNow;
        var products = new Dictionary<string, SavedProduct>(StringComparer.Ordinal);
        var warnings = new List<string>();

        void AddProduct(MonitorItemConfig item, string? groupDomain = null)
        {
            var target = ParseTarget(
                item,
                groupDomain ?? defaultDomain);
            if (products.ContainsKey(target.Key)
                || current.Products.Any(product => string.Equals(
                    product.Id,
                    target.Key,
                    StringComparison.Ordinal)))
            {
                return;
            }

            var profile = MarketplaceRegistry.Resolve(target.Domain);
            products[target.Key] = new SavedProduct
            {
                Id = target.Key,
                Asin = target.Asin,
                Domain = target.Domain,
                SavedAt = now,
                UpdatedAt = now,
                Currency = profile.CurrencyCode,
                Snapshot = new SavedSnapshot
                {
                    Title = string.IsNullOrWhiteSpace(item.Label)
                        ? target.Asin
                        : item.Label.Trim(),
                    Currency = profile.CurrencyCode,
                    Url = profile.ProductUrl(target.Asin),
                },
            };
        }

        foreach (var item in config.Items ?? [])
        {
            AddProduct(item);
        }

        var groupMappings = new Dictionary<string, SavedGroup>(
            StringComparer.Ordinal);
        var usedGroupIds = current.Groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.Ordinal);
        var importedGroups = new List<SavedGroup>();

        foreach (var groupConfig in config.Groups ?? [])
        {
            var oldId = (groupConfig.Id ?? string.Empty).Trim();
            if (oldId.Length == 0)
            {
                throw new MonitorException(
                    "A monitor group has an empty id.");
            }
            if (groupConfig.Items is null || groupConfig.Items.Count == 0)
            {
                throw new MonitorException(
                    $"Monitor group '{oldId}' contains no products.");
            }

            var existing = current.Groups.FirstOrDefault(group =>
                string.Equals(group.Id, oldId, StringComparison.Ordinal))
                ?? current.Groups.FirstOrDefault(group =>
                    string.Equals(
                        group.Name,
                        groupConfig.Name ?? oldId,
                        StringComparison.CurrentCultureIgnoreCase));
            var groupId = existing?.Id ?? MakeGroupId(oldId, usedGroupIds);
            usedGroupIds.Add(groupId);
            var groupName = string.IsNullOrWhiteSpace(groupConfig.Name)
                ? oldId
                : groupConfig.Name.Trim();

            var items = new List<SavedGroupItem>();
            foreach (var item in groupConfig.Items)
            {
                AddProduct(item, groupConfig.Domain);
                var target = ParseTarget(
                    item,
                    groupConfig.Domain ?? defaultDomain);
                items.Add(new SavedGroupItem
                {
                    ProductId = target.Key,
                    Quantity = Math.Clamp(item.Quantity, 1, 99),
                });
            }

            var imported = new SavedGroup
            {
                Id = groupId,
                Name = existing?.Name ?? groupName,
                Accent = existing?.Accent
                    ?? SavedLibraryNormalizer.NormalizeAccent(null),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
                Items = items,
            };
            groupMappings[oldId] = imported;
            importedGroups.Add(imported);
        }

        var bindingItems = new List<(MonitorBinding Binding, MonitorGroupConfig? Group)>();
        foreach (var groupConfig in config.Groups ?? [])
        {
            var imported = groupMappings[groupConfig.Id.Trim()];
            var binding = new MonitorBinding
            {
                Id = MonitorBinding.GroupBindingId(imported.Id),
                Kind = MonitorBindingKind.Group,
                GroupId = imported.Id,
                Name = imported.Name,
                TotalMax = groupConfig.TotalMax,
                AllowIncomplete = groupConfig.AllowIncomplete,
                CooldownSeconds = groupConfig.CooldownSeconds,
                Currency = string.IsNullOrWhiteSpace(groupConfig.Currency)
                    ? null
                    : groupConfig.Currency.Trim().ToUpperInvariant(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var item in groupConfig.Items ?? [])
            {
                var target = ParseTarget(
                    item,
                    groupConfig.Domain ?? defaultDomain);
                binding.ItemRules[target.Key] = ToBindingRule(item);
            }
            bindingItems.Add((binding, groupConfig));
        }

        foreach (var item in config.Items ?? [])
        {
            var target = ParseTarget(item, defaultDomain);
            var binding = new MonitorBinding
            {
                Id = MonitorBinding.ProductBindingId(target.Key),
                Kind = MonitorBindingKind.Product,
                ProductId = target.Key,
                Name = string.IsNullOrWhiteSpace(item.Label)
                    ? target.Key
                    : item.Label.Trim(),
                MaxPrice = item.MaxPrice,
                DropPercent = item.DropPercent,
                RisePercent = item.RisePercent,
                Baseline = item.Baseline,
                BaselinePrice = item.BaselinePrice,
                CooldownSeconds = item.CooldownSeconds,
                CreatedAt = now,
                UpdatedAt = now,
            };
            bindingItems.Add((binding, null));
        }

        if (products.Count == 0 && importedGroups.Count == 0)
        {
            throw new MonitorException(
                "The monitor configuration contains no products or groups.");
        }

        if (!dryRun)
        {
            libraryStore.EnsureProducts(products.Values);
            foreach (var imported in importedGroups)
            {
                var currentGroup = libraryStore.Read().Groups.FirstOrDefault(group =>
                    string.Equals(
                        group.Id,
                        imported.Id,
                        StringComparison.Ordinal));
                if (currentGroup is null)
                {
                    libraryStore.CreateGroup(imported);
                }
                else
                {
                    libraryStore.AddGroupItems(imported.Id, imported.Items);
                }
            }
            foreach (var (binding, _) in bindingItems)
            {
                bindingStore.Upsert(binding);
            }
        }

        foreach (var group in importedGroups)
        {
            var currencies = group.Items
                .Select(item => products.GetValueOrDefault(item.ProductId)
                    ?? current.Products.FirstOrDefault(product =>
                        string.Equals(
                            product.Id,
                            item.ProductId,
                            StringComparison.Ordinal)))
                .Where(product => product is not null)
                .Select(product => SavedLibraryLookup.Currency(product!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (currencies.Count > 1)
            {
                warnings.Add(
                    $"Group '{group.Name}' is multi-currency " +
                    $"({string.Join(", ", currencies)}); set --currency and " +
                    "--fx when starting its monitor.");
            }
        }

        return new MonitorImportResult(
            products.Count,
            importedGroups.Count,
            bindingItems.Count(item => item.Group is null),
            bindingItems.Count(item => item.Group is not null),
            warnings);
    }

    private static MonitorTarget ParseTarget(
        MonitorItemConfig item,
        string defaultDomain)
    {
        if (string.IsNullOrWhiteSpace(item.Asin))
        {
            throw new MonitorException(
                $"Missing ASIN in monitor item '{item.Label ?? "?"}'.");
        }
        return MonitorTargetParser.Parse(
            item.Asin,
            item.Domain,
            defaultDomain);
    }

    private static MonitorBindingItemRule ToBindingRule(
        MonitorItemConfig item) => new()
    {
        MaxPrice = item.MaxPrice,
        DropPercent = item.DropPercent,
        RisePercent = item.RisePercent,
        Baseline = item.Baseline,
        BaselinePrice = item.BaselinePrice,
        CooldownSeconds = item.CooldownSeconds,
    };

    private static string MakeGroupId(
        string oldId,
        ISet<string> used)
    {
        if (SavedLibraryNormalizer.IsGroupId(oldId)
            && !used.Contains(oldId))
        {
            return oldId;
        }

        var sanitized = new string(oldId
            .Trim()
            .Select(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'
                    ? character
                    : '_')
            .ToArray())
            .Trim('_');
        if (sanitized.Length == 0)
        {
            sanitized = "imported";
        }
        var candidate = $"g_{sanitized}";
        if (candidate.Length > 64)
        {
            candidate = $"g_{Hash(oldId)[..12]}";
        }
        if (!used.Contains(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (used.Contains($"{candidate}_{suffix}"))
        {
            suffix++;
        }
        return $"{candidate}_{suffix}";
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
