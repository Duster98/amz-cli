using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Parses the "all offers" entry point in the product page, for example
/// "Nuovo (7) da 87,25 €" or "7 opzioni a partire da 87,25 €".
/// </summary>
public static partial class BuyingOptionsSummaryParser
{
    public static BuyingOptionsSummary? Parse(string html, IMarketplaceProfile marketplace)
    {
        var direct = AmazonPatterns.AodIngressAnchor.Match(html);
        if (direct.Success && ParseAnchor(direct.Groups[1].Value, marketplace) is { } summary)
        {
            return summary;
        }

        foreach (Match match in AmazonPatterns.OfferListingAnchor.Matches(html))
        {
            if (ParseAnchor(match.Groups[1].Value, marketplace) is { } fallback)
            {
                return fallback;
            }
        }

        var used = AmazonPatterns.UsedOnlyBuybox.Match(html);
        if (used.Success)
        {
            var block = HtmlBlockExtractor.ExtractDiv(html, used.Index);
            if (ParseUsedOnly(block, marketplace) is { } usedSummary)
            {
                return usedSummary;
            }
        }

        return null;
    }

    /// <summary>
    /// Used-only pages ("Acheter D'occasion", "Acquista usato") expose the
    /// featured offer and its condition without an AOD ingress link. The
    /// total count is unknown here; AOD fills it in when available.
    /// </summary>
    private static BuyingOptionsSummary? ParseUsedOnly(
        string block,
        IMarketplaceProfile marketplace)
    {
        if (string.IsNullOrEmpty(block))
        {
            return null;
        }

        var price = ParsePrice(block, marketplace);
        var condition = ParseUsedCondition(block);
        if (string.IsNullOrEmpty(price) && string.IsNullOrEmpty(condition))
        {
            return null;
        }

        return new BuyingOptionsSummary
        {
            Count = 0,
            LowestPrice = price,
            Condition = condition,
        };
    }

    private static string ParseUsedCondition(string block)
    {
        foreach (Match match in StrongText().Matches(block))
        {
            var text = HtmlText.Clean(match.Groups[1].Value);
            if (string.IsNullOrEmpty(text)
                || text.StartsWith("État", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Etat", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Commentaire", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Condition", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Estado", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return text;
        }

        return string.Empty;
    }

    private static BuyingOptionsSummary? ParseAnchor(
        string anchor,
        IMarketplaceProfile marketplace)
    {
        var count = ParseCount(anchor);
        var lowestPrice = ParsePrice(anchor, marketplace);

        if (count == 0 && string.IsNullOrEmpty(lowestPrice))
        {
            return null;
        }

        return new BuyingOptionsSummary
        {
            Count = count,
            LowestPrice = lowestPrice,
            Condition = ParseCondition(anchor),
        };
    }

    private static string ParseCondition(string anchor)
    {
        var text = HtmlText.Clean(anchor);
        var parenthesis = text.IndexOf('(');
        if (parenthesis <= 0)
        {
            return string.Empty;
        }

        var candidate = text[..parenthesis].Trim();
        return candidate.Length == 0 || candidate.All(char.IsDigit)
            ? string.Empty
            : candidate;
    }

    private static int ParseCount(string anchor)
    {
        var match = ParenthesizedCount().Match(anchor);
        if (!match.Success)
        {
            match = LocalizedCount().Match(anchor);
        }

        return match.Success && int.TryParse(match.Groups[1].Value, out var count)
            ? count
            : 0;
    }

    private static string ParsePrice(string anchor, IMarketplaceProfile marketplace)
    {
        var whole = AmazonPatterns.PriceWhole.Match(anchor);
        var fraction = AmazonPatterns.PriceFraction.Match(anchor);
        if (whole.Success && fraction.Success)
        {
            return marketplace.Format.Price(
                whole.Groups[1].Value,
                fraction.Groups[1].Value);
        }

        if (whole.Success)
        {
            return marketplace.Format.SinglePrice(whole.Groups[1].Value);
        }

        var offscreen = OffscreenPrice().Match(anchor);
        return offscreen.Success
            ? HtmlText.Clean(offscreen.Groups[1].Value)
            : string.Empty;
    }

    // The count is usually in parentheses ("Nuovo (7) da", "New (7) from").
    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex ParenthesizedCount();

    // Some locales render the count before a localized word ("7 opzioni",
    // "7 options", "7 Angebote", "7 opciones", "7個").
    [GeneratedRegex(
        @"(\d+)\s*(?:opzioni|options?|opciones|opções|angebote|artikel|offres|propositions|個|点|개)",
        RegexOptions.IgnoreCase)]
    private static partial Regex LocalizedCount();

    [GeneratedRegex(@"a-offscreen[^>]*>\s*([^<]+?)\s*</span>", RegexOptions.IgnoreCase)]
    private static partial Regex OffscreenPrice();

    [GeneratedRegex(@"<strong[^>]*>([\s\S]*?)</strong>", RegexOptions.IgnoreCase)]
    private static partial Regex StrongText();
}
