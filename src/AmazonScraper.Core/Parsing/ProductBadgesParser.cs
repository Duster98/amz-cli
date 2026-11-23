using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Parses product/section badges and cross-border import charges. The import
/// block is rendered only when Amazon considers the selected destination
/// cross-border; it can contain a numeric estimate or a text-only disclosure.
/// </summary>
public static partial class ProductBadgesParser
{
    public sealed record BestSellerBadge(
        int? Rank,
        string? Category,
        string? Label,
        string? Url);

    public static BestSellerBadge? ParseBestSeller(
        string html,
        IMarketplaceProfile marketplace)
    {
        var block = HtmlBlockExtractor.ExtractDivById(
            html,
            "zeitgeistBadge_feature_div");
        if (string.IsNullOrWhiteSpace(block))
        {
            return null;
        }

        var link = BestSellerLinkRegex().Match(block);
        if (!link.Success)
        {
            return null;
        }

        var inner = link.Groups["inner"].Value;
        var label = CleanClassText(inner, "mvt-best-seller-badge");
        var category = HtmlText.Clean(link.Groups["title"].Value);
        if (string.IsNullOrEmpty(category))
        {
            category = CleanClassText(inner, "cat-link");
            category = Regex.Replace(
                category,
                @"^\s*in\s+",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
        }

        int? rank = null;
        var rankMatch = Regex.Match(
            label,
            @"(?:#|n\.)\s*(\d+)",
            RegexOptions.IgnoreCase);
        if (rankMatch.Success
            && int.TryParse(rankMatch.Groups[1].Value, out var parsedRank))
        {
            rank = parsedRank;
        }

        var href = link.Groups["href"].Value;
        string? url = null;
        if (!string.IsNullOrWhiteSpace(href))
        {
            try
            {
                url = new Uri(marketplace.BaseUri, href).ToString();
            }
            catch (UriFormatException)
            {
                url = null;
            }
        }

        if (rank is null
            && string.IsNullOrEmpty(category)
            && string.IsNullOrEmpty(label))
        {
            return null;
        }

        return new BestSellerBadge(
            rank,
            NullIfEmpty(category),
            NullIfEmpty(label),
            NullIfEmpty(url));
    }

    public static (string? Text, string? For) ParseAmazonChoice(string html)
    {
        var block = HtmlBlockExtractor.ExtractDivById(html, "acBadge_feature_div");
        if (string.IsNullOrWhiteSpace(block))
        {
            return (null, null);
        }

        var text = CleanClassText(block, "mvt-ac-badge-rectangle");
        if (string.IsNullOrEmpty(text))
        {
            text = CleanClassText(block, "a-badge-label");
        }
        if (string.IsNullOrEmpty(text))
        {
            text = CleanClassTexts(block, "a-badge-text");
        }
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        var forText = CleanClassText(block, "mvt-ac-for-text");
        return (text, string.IsNullOrEmpty(forText) ? null : forText);
    }

    public static ImportFeesInfo? ParseImportFees(
        string html,
        DeliveryInfo? delivery,
        string? currentPrice = null,
        string? originalPrice = null,
        IMarketplaceFormat? format = null)
    {
        var block = HtmlBlockExtractor.ExtractDivById(html, "amazonGlobal_feature_div");
        if (string.IsNullOrWhiteSpace(block))
        {
            return null;
        }

        var summary = ExtractSummary(block);
        var tableBody = ExtractLineItemTable(block);
        var rows = ParseRows(tableBody);

        var priceRow = FindRow(rows, PriceKeywords);
        var shippingRow = FindRow(rows, ShippingKeywords);
        var importRow = FindRow(rows, ImportKeywords);
        var totalRow = FindRow(rows, TotalKeywords);

        // The table layout is stable: Price, Shipping, Import charges, Total.
        // Use positions only when all four rows are present, to avoid
        // misclassifying a shorter localized layout.
        if (rows.Count >= 4)
        {
            priceRow ??= rows[0];
            shippingRow ??= rows[1];
            importRow ??= rows[2];
            totalRow ??= rows[^1];
        }

        var price = priceRow?.Value;
        var shipping = shippingRow?.Value;
        var importCharges = importRow?.Value;
        var landedTotal = totalRow?.Value;

        if (string.IsNullOrEmpty(summary)
            && string.IsNullOrEmpty(price)
            && string.IsNullOrEmpty(shipping)
            && string.IsNullOrEmpty(importCharges)
            && string.IsNullOrEmpty(landedTotal))
        {
            return null;
        }

        var atCheckout = importCharges is null
            && (ContainsCheckout(summary)
                || (string.IsNullOrEmpty(shipping) && string.IsNullOrEmpty(landedTotal)));

        return new ImportFeesInfo
        {
            Price = NullIfEmpty(price),
            PriceBasis = DeterminePriceBasis(
                price,
                currentPrice,
                originalPrice,
                format),
            Destination = FirstNonEmpty(delivery?.Location, delivery?.Country),
            Shipping = NullIfEmpty(shipping),
            ImportCharges = NullIfEmpty(importCharges),
            LandedTotal = NullIfEmpty(landedTotal),
            AtCheckout = atCheckout,
            Text = summary,
        };
    }

    private static string ExtractSummary(string block)
    {
        foreach (Match match in SpanTagRegex().Matches(block))
        {
            var attributes = match.Groups["attrs"].Value;
            if (!attributes.Contains("a-size-base", StringComparison.Ordinal)
                || !attributes.Contains("a-color-secondary", StringComparison.Ordinal))
            {
                continue;
            }

            var text = HtmlText.Clean(match.Groups["inner"].Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var cleaned = HtmlText.Clean(ScriptStyleRegex().Replace(block, " "));
        return DetailSuffixRegex().Replace(cleaned, string.Empty).Trim();
    }

    private static string ExtractLineItemTable(string block)
    {
        var table = LineItemTableRegex().Match(block);
        if (!table.Success)
        {
            table = TableRegex().Match(block);
        }

        return table.Success ? table.Groups["inner"].Value : string.Empty;
    }

    private static List<(string Label, string Value)> ParseRows(string tableBody)
    {
        var rows = new List<(string Label, string Value)>();
        if (string.IsNullOrWhiteSpace(tableBody))
        {
            return rows;
        }

        foreach (Match row in RowRegex().Matches(tableBody))
        {
            var cells = CellRegex().Matches(row.Groups["inner"].Value)
                .Select(match => HtmlText.Clean(match.Groups["inner"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (cells.Count >= 2)
            {
                rows.Add((cells[0], cells[^1]));
            }
        }

        return rows;
    }

    private static (string Label, string Value)? FindRow(
        List<(string Label, string Value)> rows,
        string[] keywords)
    {
        foreach (var row in rows)
        {
            if (keywords.Any(keyword =>
                    row.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return row;
            }
        }

        return null;
    }

    private static bool ContainsCheckout(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return CheckoutKeywords.Any(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanClassText(string html, string className)
    {
        var match = ClassTextRegex(className).Match(html);
        return match.Success ? HtmlText.Clean(match.Groups["inner"].Value) : string.Empty;
    }

    private static string CleanClassTexts(string html, string className)
    {
        var values = ClassTextRegex(className).Matches(html)
            .Select(match => HtmlText.Clean(match.Groups["inner"].Value))
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" ", values).Trim();
    }

    private static Regex ClassTextRegex(string className) =>
        new(
            $@"<(?<tag>[a-zA-Z0-9]+)\b[^>]*class\s*=\s*[""'][^""']*\b{Regex.Escape(className)}\b[^""']*[""'][^>]*>(?<inner>[\s\S]*?)</\k<tag>>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first)
            ? first
            : !string.IsNullOrWhiteSpace(second)
                ? second
                : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? DeterminePriceBasis(
        string? tablePrice,
        string? currentPrice,
        string? originalPrice,
        IMarketplaceFormat? format)
    {
        if (string.IsNullOrWhiteSpace(tablePrice) || format is null)
        {
            return null;
        }

        if (!format.TryParsePrice(tablePrice, out var table))
        {
            return null;
        }

        if (format.TryParsePrice(currentPrice ?? string.Empty, out var current)
            && table == current)
        {
            return "current";
        }

        if (format.TryParsePrice(originalPrice ?? string.Empty, out var original)
            && table == original)
        {
            return "list";
        }

        return "other";
    }

    private static readonly string[] PriceKeywords =
    [
        "price", "prezzo", "preis", "prix", "precio", "価格", "价格",
    ];

    private static readonly string[] ShippingKeywords =
    [
        "shipping", "spedizione", "versand", "livraison", "envío", "envio",
        "expédition", "expedition", "配送", "送料", "gastos de envío",
    ];

    private static readonly string[] ImportKeywords =
    [
        "import", "einfuhr", "importation", "importación", "importacion",
        "dazi", "dogan", "tasse", "taxes", "輸入", "関税", "import fees",
        "import charges",
    ];

    private static readonly string[] TotalKeywords =
    [
        "total", "totale", "gesamt", "合計", "合计",
    ];

    private static readonly string[] CheckoutKeywords =
    [
        "checkout", "at checkout", "cassa", "pagamento", "paiement",
        "kasse", "pago", "ご注文確定時", "レジ", "付款", "結帳",
    ];

    [GeneratedRegex(
        @"<a\b(?=[^>]*\bbadge-link\b)(?=[^>]*\bhref\s*=\s*""(?<href>[^""]*)"")(?=[^>]*\btitle\s*=\s*""(?<title>[^""]*)"")(?<attrs>[^>]*)>(?<inner>[\s\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BestSellerLinkRegex();

    [GeneratedRegex(@"<span(?<attrs>[^>]*)>(?<inner>[\s\S]*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpanTagRegex();

    [GeneratedRegex(@"<script\b[\s\S]*?</script>|<style\b[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"\s+(?:Details|See details\.?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailSuffixRegex();

    [GeneratedRegex(@"<table[^>]*class\s*=\s*[""'][^""']*\ba-lineitem\b[^""']*[""'][^>]*>(?<inner>[\s\S]*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LineItemTableRegex();

    [GeneratedRegex(@"<table[^>]*>(?<inner>[\s\S]*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr[^>]*>(?<inner>[\s\S]*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<td[^>]*>(?<inner>[\s\S]*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();
}
