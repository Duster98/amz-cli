using System.Globalization;
using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Library;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public static class MonitorBindingFactory
{
    public static MonitorBinding Resolve(
        MonitorSettings settings,
        SavedLibraryDocument library,
        MonitorBindingStore store,
        bool persist = true)
    {
        if (!string.IsNullOrWhiteSpace(settings.Binding))
        {
            return store.Find(settings.Binding.Trim())
                ?? throw new MonitorException(
                    $"Monitor binding not found: {settings.Binding}.");
        }

        var existing = default(MonitorBinding);
        MonitorBinding binding;
        if (!string.IsNullOrWhiteSpace(settings.LibraryProduct))
        {
            var target = MonitorTargetParser.Parse(
                settings.LibraryProduct,
                settings.Domain ?? "it");
            var product = SavedLibraryLookup.RequireProduct(library, target);
            var groups = SavedLibraryLookup.GroupsForProduct(library, product.Id);
            if (groups.Count > 0)
            {
                throw new MonitorException(
                    $"Product {product.Id} belongs to group " +
                    $"{string.Join(", ", groups.Select(group => $"'{group.Name}'"))}. " +
                    "Remove it from the group or monitor the group instead.");
            }

            var id = MonitorBinding.ProductBindingId(product.Id);
            existing = store.Find(id);
            binding = new MonitorBinding
            {
                Id = id,
                Kind = MonitorBindingKind.Product,
                ProductId = product.Id,
                Name = product.Snapshot.Title,
                MaxPrice = settings.MaxPrice,
                DropPercent = settings.DropPercent,
                RisePercent = settings.RisePercent,
                Baseline = settings.Baseline,
                CooldownSeconds = settings.Cooldown,
            };
            if (settings.TotalMax is not null
                || settings.Currency is not null
                || settings.Fx.Length > 0
                || settings.AllowIncomplete)
            {
                throw new MonitorException(
                    "Group total options require --library-group.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(settings.LibraryGroup))
        {
            var group = SavedLibraryLookup.ResolveGroup(
                library,
                settings.LibraryGroup);
            var id = MonitorBinding.GroupBindingId(group.Id);
            existing = store.Find(id);
            binding = new MonitorBinding
            {
                Id = id,
                Kind = MonitorBindingKind.Group,
                GroupId = group.Id,
                Name = group.Name,
                MaxPrice = settings.MaxPrice,
                DropPercent = settings.DropPercent,
                RisePercent = settings.RisePercent,
                Baseline = settings.Baseline,
                CooldownSeconds = settings.Cooldown,
                TotalMax = settings.TotalMax,
                AllowIncomplete = settings.AllowIncomplete ? true : null,
                Currency = string.IsNullOrWhiteSpace(settings.Currency)
                    ? null
                    : settings.Currency.Trim().ToUpperInvariant(),
                Fx = ParseFx(settings.Fx),
            };
        }
        else
        {
            throw new MonitorException(
                "No monitor target was specified.");
        }

        if (existing is not null)
        {
            binding.CreatedAt = existing.CreatedAt;
            binding.ItemRules = existing.ItemRules;
            binding.Name ??= existing.Name;
            binding.MaxPrice ??= existing.MaxPrice;
            binding.DropPercent ??= existing.DropPercent;
            binding.RisePercent ??= existing.RisePercent;
            binding.Baseline ??= existing.Baseline;
            binding.BaselinePrice ??= existing.BaselinePrice;
            binding.CooldownSeconds ??= existing.CooldownSeconds;
            binding.TotalMax ??= existing.TotalMax;
            binding.AllowIncomplete ??= existing.AllowIncomplete;
            binding.Currency ??= existing.Currency;
            binding.Runtime ??= existing.Runtime;
            if (binding.Fx.Count == 0)
            {
                binding.Fx = existing.Fx;
            }
            else
            {
                foreach (var (currency, rate) in existing.Fx)
                {
                    binding.Fx.TryAdd(currency, rate);
                }
            }
        }

        if (persist)
        {
            store.Upsert(binding);
        }
        return binding;
    }

    private static Dictionary<string, decimal> ParseFx(
        IReadOnlyList<string> values)
    {
        var result = new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new MonitorException(
                    $"Invalid --fx value '{value}'. Use CUR=RATE.");
            }
            var currency = value[..separator].Trim().ToUpperInvariant();
            if (currency.Length != 3
                || !currency.All(char.IsAsciiLetter)
                || !decimal.TryParse(
                    value[(separator + 1)..],
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var rate)
                || rate <= 0)
            {
                throw new MonitorException(
                    $"Invalid --fx value '{value}'. Use CUR=RATE, for example GBP=1.17.");
            }
            result[currency] = rate;
        }
        return result;
    }
}
