using AmazonScraper.Cli.Web;
using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AmazonScraper.Cli.Rendering;

public sealed record CompareShowItem(
    CompareItem Item,
    ProductDetail? Detail,
    string Currency,
    string? Error)
{
    public string Market => Item.Domain.ToUpperInvariant();
}

public sealed record CompareDiffProduct(
    int Index,
    string Market,
    string Asin,
    string Title,
    string Currency,
    string? Error);

public sealed record CompareDiffAttribute(
    string Name,
    IReadOnlyList<string> Values,
    bool Different);

public sealed record CompareDiff(
    IReadOnlyList<CompareDiffProduct> Products,
    IReadOnlyList<CompareDiffAttribute> Attributes)
{
    public int DifferenceCount => Attributes.Count(item => item.Different);
}

/// <summary>
/// Builds the attribute matrix used by <c>amz-cli compare show --diff</c>.
/// It deliberately keeps every product field and every specification key, even
/// when values do not match or are missing: differences and gaps are data.
/// </summary>
public static class CompareDiffBuilder
{
    public static CompareDiff Build(IReadOnlyList<CompareShowItem> items)
    {
        var products = items
            .Select((item, index) => new CompareDiffProduct(
                index + 1,
                item.Item.Domain,
                item.Item.Asin,
                item.Detail?.Title ?? item.Item.Asin,
                item.Currency,
                item.Error))
            .ToArray();

        var attributes = new List<CompareDiffAttribute>();
        var marketValues = items
            .Select(item => item.Item.Domain)
            .ToArray();
        attributes.Add(new CompareDiffAttribute(
            "Market",
            marketValues,
            IsDifferent(marketValues)));
        var asinValues = items
            .Select(item => item.Item.Asin)
            .ToArray();
        attributes.Add(new CompareDiffAttribute(
            "ASIN",
            asinValues,
            IsDifferent(asinValues)));

        foreach (var field in FixedFields)
        {
            var values = items
                .Select(item => Value(item, field.Pick))
                .ToArray();
            attributes.Add(new CompareDiffAttribute(
                field.Label,
                values,
                IsDifferent(values)));
        }

        foreach (var key in SpecKeys(items))
        {
            var values = items
                .Select(item => item.Error is not null
                    ? $"ERROR: {item.Error}"
                    : SpecValue(item.Detail, key))
                .ToArray();
            attributes.Add(new CompareDiffAttribute(
                $"Spec · {key}",
                values,
                IsDifferent(values)));
        }

        foreach (var key in AmazonDetailKeys(items))
        {
            var values = items
                .Select(item => item.Error is not null
                    ? $"ERROR: {item.Error}"
                    : AmazonDetailValue(item.Detail, key))
                .ToArray();
            attributes.Add(new CompareDiffAttribute(
                $"Amazon · {key}",
                values,
                IsDifferent(values)));
        }

        return new CompareDiff(products, attributes);
    }

    // The seller's own offer, as the product page prices it; else the first offer.
    internal static string? FeaturedCondition(ProductDetail? detail)
    {
        var offers = detail?.BuyingOptions;
        List<BuyingOption> options = offers?.Options ?? [];
        var offer = options.FirstOrDefault(option =>
                !string.IsNullOrWhiteSpace(detail?.Seller)
                && option.Seller == detail?.Seller)
            ?? options.FirstOrDefault();
        return new[]
            {
                offer?.Condition,
                options.FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.Condition))?.Condition,
                offers?.Condition,
            }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static readonly (string Label, Func<ProductDetail, string?> Pick)[] FixedFields =
    [
        ("Title", detail => detail.Title),
        ("Brand", detail => detail.Brand),
        ("Model", detail => detail.ModelName),
        ("Subtitle", detail => detail.Subtitle),
        ("Price", detail => detail.Price),
        ("Original price", detail => detail.OriginalPrice),
        ("Discount", detail => detail.Discount),
        ("Coupon", detail => detail.Coupon),
        ("Rating", detail => detail.Rating),
        ("Rating average", detail => detail.RatingDetails?.Average is { } average
            ? average.ToString(
                "0.##",
                System.Globalization.CultureInfo.InvariantCulture)
            : null),
        ("Rating count", detail => detail.RatingDetails?.Count.ToString()),
        ("Reviews", detail => detail.Reviews),
        ("Seller", detail => detail.Seller),
        ("Shipped by", detail => detail.ShippedBy),
        ("Availability", detail => detail.Availability),
        ("Returns", detail => detail.Returns),
        ("Delivery cost", detail => detail.Delivery?.Cost),
        ("Delivery time", detail => detail.Delivery?.Time),
        ("Delivery location", detail => detail.Delivery?.Location),
        ("Delivery type", detail => detail.Delivery?.Type),
        ("Amazon's Choice", detail => detail.AmazonChoice switch
        {
            true => "Yes",
            false => "No",
            _ => null,
        }),
        ("Amazon's Choice for", detail => detail.AmazonChoiceFor),
        ("Deal", detail => detail.Deal),
        ("Deal ends on", detail => detail.DealEndsOn),
        ("Deal ends text", detail => detail.DealEndsText),
        ("Best seller rank", detail => detail.BestSellerRank?.ToString()),
        ("Best seller category", detail => detail.BestSellerCategory),
        ("Frequently returned", detail => detail.FrequentlyReturned),
        ("Offers count", detail => detail.BuyingOptions?.Count.ToString()),
        ("Lowest offer", detail => detail.BuyingOptions?.LowestPrice),
        ("Condition", detail => FeaturedCondition(detail)),
        ("Charger included", detail => detail.Charger?.Included switch
        {
            true => "Yes",
            false => "No",
            _ => null,
        }),
        ("Charger note", detail => detail.Charger?.Note),
        ("Box contents", detail => detail.Charger?.BoxContents),
        ("Import price", detail => detail.ImportFees?.Price),
        ("Import basis", detail => detail.ImportFees?.PriceBasis),
        ("Import destination", detail => detail.ImportFees?.Destination),
        ("Import shipping", detail => detail.ImportFees?.Shipping),
        ("Import charges", detail => detail.ImportFees?.ImportCharges),
        ("Landed total", detail => detail.ImportFees?.LandedTotal),
        ("Import at checkout", detail => detail.ImportFees is { } fees
            ? (fees.AtCheckout ? "Yes" : "No")
            : null),
        ("Safety warnings", detail => detail.SafetyWarnings.Count > 0
            ? string.Join("; ", detail.SafetyWarnings)
            : null),
        ("Variants", detail => detail.Variants?.Items.Count.ToString()),
    ];

    private static string Value(
        CompareShowItem item,
        Func<ProductDetail, string?> pick)
    {
        if (item.Error is not null)
        {
            return $"ERROR: {item.Error}";
        }
        if (item.Detail is null)
        {
            return "—";
        }
        var value = pick(item.Detail)?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static IEnumerable<string> SpecKeys(
        IReadOnlyList<CompareShowItem> items) =>
        items
            .SelectMany(item => item.Detail is null
                ? (IEnumerable<string>)Array.Empty<string>()
                : item.Detail.Specs.Keys)
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key);

    private static string SpecValue(ProductDetail? detail, string key)
    {
        if (detail is null)
        {
            return "—";
        }
        var match = detail.Specs.FirstOrDefault(pair =>
            string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Value)
            ? "—"
            : match.Value.Trim();
    }

    private static IEnumerable<string> AmazonDetailKeys(
        IReadOnlyList<CompareShowItem> items) =>
        items
            .SelectMany(item => item.Detail?.AmazonDetails is { } details
                ? details.Select(extra => extra.Name)
                : Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

    private static string AmazonDetailValue(ProductDetail? detail, string key)
    {
        var match = detail?.AmazonDetails?.FirstOrDefault(extra =>
            string.Equals(extra.Name, key, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match?.Value)
            ? "—"
            : match.Value.Trim();
    }

    private static bool IsDifferent(IReadOnlyList<string> values)
    {
        var normalized = values
            .Select(value => value == "—" ? string.Empty : value.Trim())
            .ToArray();
        if (normalized.All(value => value.Length == 0))
        {
            return false;
        }
        if (normalized.Any(value => value.Length == 0)
            && normalized.Any(value => value.Length > 0))
        {
            return true;
        }
        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
    }
}

public static class CompareTabsRenderer
{
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);
    private static readonly Style LowPriceStyle = new(Color.Green);

    public static void RenderTable(
        IAnsiConsole console,
        IReadOnlyList<CompareShowItem> items)
    {
        var hasChoice = items.Any(item => item.Detail?.AmazonChoice == true);
        var hasDeal = items.Any(item => !string.IsNullOrWhiteSpace(item.Detail?.Deal));
        var hasCharger = items.Any(item => item.Detail?.Charger is not null);
        var hasLanded = items.Any(item => item.Detail?.ImportFees is not null);
        var hasError = items.Any(item => item.Error is not null);
        var hasOriginal = items.Any(item =>
            !string.IsNullOrWhiteSpace(item.Detail?.OriginalPrice));
        var hasDiscount = items.Any(item =>
            !string.IsNullOrWhiteSpace(item.Detail?.Discount));
        var hasCoupon = items.Any(item =>
            !string.IsNullOrWhiteSpace(item.Detail?.Coupon));

        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };
        table.AddColumn(new TableColumn(new Text("#", new Style(Color.Grey)))
        {
            Width = 3,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Market", HeaderStyle))
        {
            Width = 7,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("ASIN", new Style(Color.Yellow)))
        {
            Width = 12,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Title", HeaderStyle))
        {
            Width = 34,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green)))
        {
            Width = 12,
            NoWrap = true,
        });
        if (hasOriginal)
        {
            table.AddColumn(new TableColumn(new Text("Original", new Style(Color.Grey)))
            {
                Width = 10,
                NoWrap = true,
            });
        }
        if (hasDiscount)
        {
            table.AddColumn(new TableColumn(new Text("Disc.", new Style(Color.Magenta)))
            {
                Width = 6,
                NoWrap = true,
            });
        }
        if (hasCoupon)
        {
            table.AddColumn(new TableColumn(new Text("Coupon", new Style(Color.Magenta)))
            {
                Width = 9,
                NoWrap = true,
            });
        }
        table.AddColumn(new TableColumn(new Text("Rating", new Style(Color.Yellow)))
        {
            Width = 8,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Reviews", HeaderStyle))
        {
            Width = 8,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Seller", HeaderStyle))
        {
            Width = 13,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Avail", HeaderStyle))
        {
            Width = 19,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Delivery", HeaderStyle))
        {
            Width = 26,
            NoWrap = true,
        });
        if (hasChoice)
        {
            table.AddColumn(new TableColumn(new Text("Choice", new Style(Color.Cyan)))
            {
                Width = 7,
                NoWrap = true,
            });
        }
        if (hasDeal)
        {
            table.AddColumn(new TableColumn(new Text("Deal", new Style(Color.Magenta)))
            {
                Width = 8,
                NoWrap = true,
            });
        }
        if (hasCharger)
        {
            table.AddColumn(new TableColumn(new Text("Charger", HeaderStyle))
            {
                Width = 7,
                NoWrap = true,
            });
        }
        if (hasLanded)
        {
            table.AddColumn(new TableColumn(new Text("Landed", new Style(Color.Green)))
            {
                Width = 11,
                NoWrap = true,
            });
        }

        var cheapestByCurrency = CheapestByCurrency(items);
        var index = 0;
        foreach (var item in items)
        {
            index++;
            var detail = item.Detail;
            var price = detail?.Price ?? string.Empty;
            if (detail is not null
                && cheapestByCurrency.TryGetValue(item.Currency, out var cheapest)
                && cheapest.Count > 1
                && PriceParser.TryParse(
                    detail.Price,
                    MarketplaceRegistry.Resolve(item.Item.Domain).Format)
                    is { } parsed
                && parsed == cheapest.Min)
            {
                price = $"{price} ★";
            }

            var row = new List<IRenderable>
            {
                new Text(index.ToString(), new Style(Color.Grey)),
                new Text(item.Market),
                new Text(item.Item.Asin, new Style(Color.Yellow)),
                new Text(
                    detail is null
                        ? "ERROR"
                        : TextCleaner.Clean(detail.Title, 36))
                    .Overflow(Overflow.Ellipsis),
                new Text(
                    string.IsNullOrEmpty(price) ? "-" : price,
                    LowPriceStyle),
            };
            if (hasOriginal)
            {
                row.Add(new Text(
                    detail?.OriginalPrice is { Length: > 0 } original
                        ? original
                        : "-",
                    new Style(Color.Grey)));
            }
            if (hasDiscount)
            {
                row.Add(new Text(
                    detail?.Discount is { Length: > 0 } discount
                        ? discount
                        : "-",
                    new Style(Color.Magenta)));
            }
            if (hasCoupon)
            {
                row.Add(new Text(
                    detail?.Coupon is { Length: > 0 } coupon
                        ? TextCleaner.Clean(coupon, 10)
                        : "-",
                    new Style(Color.Magenta)));
            }
            row.Add(new Text(
                detail?.Rating is { Length: > 0 } rating
                    ? rating
                    : "-",
                new Style(Color.Yellow)));
            row.Add(new Text(
                detail?.Reviews is { Length: > 0 } reviews
                    ? reviews
                    : "-"));
            row.Add(new Text(
                detail?.Seller is { Length: > 0 } seller
                    ? TextCleaner.Clean(seller, 14)
                    : "-"));
            row.Add(new Text(
                detail?.Availability is { Length: > 0 } availability
                    ? TextCleaner.Clean(availability, 21)
                    : "-"));
            row.Add(new Text(
                DeliveryText(detail),
                HeaderStyle));
            if (hasChoice)
            {
                row.Add(new Text(
                    detail?.AmazonChoice == true ? "✓" : "-",
                    new Style(Color.Cyan)));
            }
            if (hasDeal)
            {
                row.Add(new Text(
                    detail?.Deal is { Length: > 0 } deal
                        ? TextCleaner.Clean(deal, 9)
                        : "-",
                    new Style(Color.Magenta)));
            }
            if (hasCharger)
            {
                row.Add(new Text(detail?.Charger?.Included switch
                {
                    true => "Yes",
                    false => "No",
                    _ => "-",
                }));
            }
            if (hasLanded)
            {
                row.Add(new Text(detail?.ImportFees?.LandedTotal is { Length: > 0 } landed
                    ? landed
                    : "-", new Style(Color.Green)));
            }
            table.AddRow(row.ToArray());
        }

        console.Write(table);

        if (cheapestByCurrency.Values.Any(group => group.Count > 1))
        {
            console.MarkupLine(
                "[grey]★ lowest price within the same currency.[/]");
        }
        if (hasError)
        {
            console.MarkupLine(
                "[yellow]One or more products could not be loaded; see the errors above.[/]");
        }
    }

    public static void RenderDiff(
        IAnsiConsole console,
        CompareDiff diff)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };
        table.AddColumn(new TableColumn(new Text("Attribute", HeaderStyle))
        {
            Width = 28,
            NoWrap = false,
        });
        foreach (var product in diff.Products)
        {
            var title = product.Error is null
                ? TextCleaner.Clean(product.Title, 30)
                : "ERROR";
            table.AddColumn(new TableColumn(
                new Text($"#{product.Index} {product.Market}\n{title}"))
            {
                Width = diff.Products.Count <= 3 ? 38 : 28,
                NoWrap = false,
            });
        }

        foreach (var attribute in diff.Attributes)
        {
            var label = new Text(
                attribute.Different
                    ? $"● {attribute.Name}"
                    : $"  {attribute.Name}",
                attribute.Different
                    ? new Style(Color.Yellow)
                    : Style.Plain);
            var row = new List<IRenderable> { label };
            row.AddRange(attribute.Values.Select(value =>
                new Text(value)));
            table.AddRow(row.ToArray());
        }

        console.Write(table);
        console.MarkupLine(
            $"[grey]{diff.DifferenceCount} of {diff.Attributes.Count} attributes differ. " +
            "● marks a difference; — means missing data.[/]");
    }

    private static string DeliveryText(ProductDetail? detail)
    {
        if (detail?.Delivery is not { } delivery)
        {
            return "-";
        }
        var text = string.Join(
            " · ",
            new[] { delivery.Cost, delivery.Time, delivery.Location }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(text)
            ? "-"
            : TextCleaner.Clean(text, 20);
    }

    private static Dictionary<string, (int Count, decimal Min)> CheapestByCurrency(
        IReadOnlyList<CompareShowItem> items)
    {
        var result = new Dictionary<string, (int Count, decimal Min)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Detail is null
                || string.IsNullOrWhiteSpace(item.Currency))
            {
                continue;
            }
            if (PriceParser.TryParse(
                    item.Detail.Price,
                    MarketplaceRegistry.Resolve(item.Item.Domain).Format)
                is not { } parsed)
            {
                continue;
            }
            if (result.TryGetValue(item.Currency, out var current))
            {
                result[item.Currency] = (
                    current.Count + 1,
                    Math.Min(current.Min, parsed));
            }
            else
            {
                result[item.Currency] = (1, parsed);
            }
        }
        return result;
    }
}
