using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Cli.Library;

public sealed class SavedLibrary
{
    public List<SavedProduct> Products { get; set; } = [];

    public List<SavedGroup> Groups { get; set; } = [];
}

public sealed class SavedProduct
{
    public string Id { get; set; } = string.Empty;

    public string Asin { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public decimal? PriceAtSave { get; set; }

    public decimal? PriceLast { get; set; }

    public string Currency { get; set; } = string.Empty;

    public SavedSnapshot Snapshot { get; set; } = new();
}

public sealed class SavedSnapshot
{
    public string Title { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    public string Price { get; set; } = string.Empty;

    public decimal? PriceAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string OriginalPrice { get; set; } = string.Empty;

    public string Discount { get; set; } = string.Empty;

    public string Rating { get; set; } = string.Empty;

    public string Reviews { get; set; } = string.Empty;

    public string DeliveryText { get; set; } = string.Empty;

    public string Availability { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public ChargerInfo? Charger { get; set; }
}

public sealed class SavedGroup
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Accent { get; set; } = "accent";

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<SavedGroupItem> Items { get; set; } = [];
}

public sealed class SavedGroupItem
{
    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;
}

public static class SavedLibraryNormalizer
{
    public const int MaxProducts = 500;
    public const int MaxGroups = 100;
    public const int MaxItemsPerGroup = 100;
    public const int MaxGroupNameLength = 80;
    public const int MaxGroupDescriptionLength = 500;

    private static readonly HashSet<string> AllowedAccents =
        new(StringComparer.Ordinal)
        {
            "accent",
            "blue",
            "gold",
            "rust",
            "green",
            "teal",
            "violet",
            "rose",
            "slate",
            "orange",
            "plum",
            "sky",
        };

    private static readonly Regex HexColor = new(
        "^#[0-9a-f]{6}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SavedLibrary Normalize(SavedLibrary? source, bool strict = false)
    {
        var library = new SavedLibrary();
        var products = new Dictionary<string, SavedProduct>(
            StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in source?.Products ?? [])
        {
            if (candidate is null)
            {
                continue;
            }

            var product = NormalizeProduct(candidate, strict);
            if (product is null)
            {
                continue;
            }

            if (products.TryGetValue(product.Id, out var existing))
            {
                if (strict)
                {
                    throw new ArgumentException(
                        $"Duplicate saved product: {product.Id}.");
                }
                if (existing.UpdatedAt > product.UpdatedAt)
                {
                    continue;
                }
            }

            products[product.Id] = product;
            if (products.Count > MaxProducts)
            {
                if (strict)
                {
                    throw new ArgumentException(
                        $"Too many saved products (maximum {MaxProducts}).");
                }
                break;
            }
        }

        library.Products = products.Values
            .OrderByDescending(item => item.SavedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var productIds = products.Keys.ToHashSet(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in source?.Groups ?? [])
        {
            if (candidate is null)
            {
                continue;
            }

            var group = NormalizeGroup(candidate, productIds, strict, now);
            if (group is null)
            {
                continue;
            }
            if (!groupIds.Add(group.Id))
            {
                if (strict)
                {
                    throw new ArgumentException($"Duplicate group id: {group.Id}.");
                }
                continue;
            }

            library.Groups.Add(group);
            if (library.Groups.Count >= MaxGroups)
            {
                if (strict)
                {
                    throw new ArgumentException(
                        $"Too many product groups (maximum {MaxGroups}).");
                }
                break;
            }
        }

        library.Groups = library.Groups
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        return library;
    }

    public static SavedProduct? NormalizeProduct(
        SavedProduct? candidate,
        bool strict = false)
    {
        if (candidate is null)
        {
            return null;
        }

        var asin = (candidate.Asin ?? string.Empty).Trim().ToUpperInvariant();
        var domain = (candidate.Domain ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsAsin(asin)
            || !MarketplaceRegistry.TryResolve(domain, out var marketplace))
        {
            if (strict)
            {
                throw new ArgumentException(
                    $"Invalid saved product: ASIN '{candidate.Asin}' or " +
                    $"domain '{candidate.Domain}'.");
            }
            return null;
        }

        var id = $"{marketplace.Id}:{asin}";
        var snapshot = candidate.Snapshot ?? new SavedSnapshot();
        var currency = string.IsNullOrWhiteSpace(snapshot.Currency)
            ? marketplace.CurrencyCode
            : snapshot.Currency.Trim().ToUpperInvariant();
        var priceAtSave = NonNegative(candidate.PriceAtSave) ?? snapshot.PriceAmount;
        var priceLast = NonNegative(candidate.PriceLast) ?? snapshot.PriceAmount;
        var savedAt = candidate.SavedAt == default ? DateTimeOffset.UtcNow : candidate.SavedAt;
        var updatedAt = candidate.UpdatedAt == default ? savedAt : candidate.UpdatedAt;
        if (updatedAt < savedAt)
        {
            updatedAt = savedAt;
        }
        var lastCheckedAt = candidate.LastCheckedAt;

        return new SavedProduct
        {
            Id = id,
            Asin = asin,
            Domain = marketplace.Id,
            SavedAt = savedAt,
            UpdatedAt = updatedAt,
            LastCheckedAt = lastCheckedAt,
            PriceAtSave = priceAtSave,
            PriceLast = priceLast,
            Currency = currency,
            Snapshot = new SavedSnapshot
            {
                Title = Clean(snapshot.Title),
                Image = Clean(snapshot.Image),
                Price = Clean(snapshot.Price),
                PriceAmount = NonNegative(snapshot.PriceAmount),
                Currency = currency,
                OriginalPrice = Clean(snapshot.OriginalPrice),
                Discount = Clean(snapshot.Discount),
                Rating = Clean(snapshot.Rating),
                Reviews = Clean(snapshot.Reviews),
                DeliveryText = Clean(snapshot.DeliveryText),
                Availability = Clean(snapshot.Availability),
                Url = string.IsNullOrWhiteSpace(snapshot.Url)
                    ? marketplace.ProductUrl(asin)
                    : snapshot.Url.Trim(),
                Charger = snapshot.Charger,
            },
        };
    }

    public static SavedGroup? NormalizeGroup(
        SavedGroup? candidate,
        IReadOnlySet<string> productIds,
        bool strict = false,
        DateTimeOffset? now = null)
    {
        if (candidate is null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var name = Clean(candidate.Name);
        if (name.Length > MaxGroupNameLength)
        {
            name = name[..MaxGroupNameLength].TrimEnd();
        }
        if (name.Length == 0)
        {
            if (strict)
            {
                throw new ArgumentException("A group name is required.");
            }
            return null;
        }

        var id = (candidate.Id ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            id = NewGroupId();
        }
        if (!IsGroupId(id))
        {
            if (strict)
            {
                throw new ArgumentException($"Invalid group id: {id}.");
            }
            return null;
        }

        var accent = NormalizeAccent(candidate.Accent);
        var description = Clean(candidate.Description);
        if (description.Length > MaxGroupDescriptionLength)
        {
            description = description[..MaxGroupDescriptionLength].TrimEnd();
        }
        var createdAt = candidate.CreatedAt == default ? timestamp : candidate.CreatedAt;
        var updatedAt = candidate.UpdatedAt == default ? createdAt : candidate.UpdatedAt;
        if (updatedAt < createdAt)
        {
            updatedAt = createdAt;
        }

        var items = new List<SavedGroupItem>();
        var seenItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in candidate.Items ?? [])
        {
            var productId = (item?.ProductId ?? string.Empty).Trim();
            if (productId.Length == 0 || !seenItems.Add(productId))
            {
                continue;
            }
            if (!productIds.Contains(productId))
            {
                if (strict)
                {
                    throw new ArgumentException(
                        $"Group '{name}' references an unknown product: {productId}.");
                }
                continue;
            }

            items.Add(new SavedGroupItem
            {
                ProductId = productId,
                Quantity = Math.Clamp(item!.Quantity, 1, 99),
            });
            if (items.Count >= MaxItemsPerGroup)
            {
                break;
            }
        }

        return new SavedGroup
        {
            Id = id,
            Name = name,
            Accent = accent,
            Description = description.Length == 0 ? null : description,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Items = items,
        };
    }

    public static string NewGroupId() =>
        $"g_{Guid.NewGuid():N}"[..14];

    public static bool IsGroupId(string value) =>
        value.Length is > 2 and <= 64
        && value[0] == 'g'
        && value[1] == '_'
        && value[2..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public static bool IsValidAccent(string? value)
    {
        var accent = Clean(value).ToLowerInvariant();
        return AllowedAccents.Contains(accent) || HexColor.IsMatch(accent);
    }

    public static string NormalizeAccent(string? value)
    {
        var accent = Clean(value).ToLowerInvariant();
        if (AllowedAccents.Contains(accent) || HexColor.IsMatch(accent))
        {
            return accent;
        }
        return "accent";
    }

    private static bool IsAsin(string value) =>
        value.Length == 10
        && value.All(character => char.IsAsciiLetterOrDigit(character));

    private static string Clean(string? value) =>
        (value ?? string.Empty).Trim();

    private static decimal? NonNegative(decimal? value) =>
        value is null || value < 0 ? null : value;
}
