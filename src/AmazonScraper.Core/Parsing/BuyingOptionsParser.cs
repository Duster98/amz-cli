using System.Net;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Parses Amazon's "all offers display" (AOD) fragment. The fragment is
/// loaded asynchronously by the product page from
/// <c>/gp/product/ajax/aodAjaxMain</c> and contains one
/// <c>&lt;div id="aod-offer"&gt;</c> block per buying option.
/// </summary>
public static partial class BuyingOptionsParser
{
    public static BuyingOptionsSummary? Parse(
        string html,
        IMarketplaceProfile marketplace,
        string asin)
    {
        var options = new List<BuyingOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in AmazonPatterns.AodOfferBlock.Matches(html))
        {
            var block = HtmlBlockExtractor.ExtractDiv(html, match.Index);
            if (string.IsNullOrEmpty(block))
            {
                continue;
            }

            var option = ParseOption(block, marketplace, asin);
            if (IsValidOption(option))
            {
                AddUnique(options, seen, option);
            }
        }

        var pinnedAdded = false;
        if (options.Count == 0)
        {
            // Mobile/table AOD variant: offers are not wrapped in
            // <div id="aod-offer">. The featured offer is in
            // #aod-pinned-offer and the others follow as
            // #aod-offer-heading + <table> blocks.
            var pinned = ExtractDivById(html, "aod-pinned-offer");
            if (!string.IsNullOrEmpty(pinned)
                && pinned.Contains("offerListingId", StringComparison.OrdinalIgnoreCase))
            {
                var pinnedOption = ParseOption(pinned, marketplace, asin);
                if (IsValidOption(pinnedOption))
                {
                    pinnedAdded = AddUnique(options, seen, pinnedOption);
                }
            }

            var listStart = html.IndexOf(
                "id=\"aod-offer-list\"",
                StringComparison.OrdinalIgnoreCase);
            var headings = Regex.Matches(
                html,
                @"id=""aod-offer-heading""",
                RegexOptions.IgnoreCase);
            for (var index = 0; index < headings.Count; index++)
            {
                var position = headings[index].Index;
                if (listStart >= 0 && position < listStart)
                {
                    continue;
                }

                var end = index + 1 < headings.Count
                    ? headings[index + 1].Index
                    : html.Length;
                var option = ParseOption(html[position..end], marketplace, asin);
                if (IsValidOption(option))
                {
                    AddUnique(options, seen, option);
                }
            }
        }

        var count = ParseCount(html);
        if (pinnedAdded && count > 0)
        {
            // aod-total-offer-count on the pinned/mobile variant counts the
            // other offers; the pinned featured offer is additional.
            count++;
        }
        if (count == 0)
        {
            count = options.Count;
        }

        if (options.Count == 0 && count == 0)
        {
            return null;
        }

        return new BuyingOptionsSummary
        {
            Count = count,
            LowestPrice = LowestPrice(options, marketplace),
            Condition = options
                .Select(option => option.Condition)
                .FirstOrDefault(condition => !string.IsNullOrWhiteSpace(condition))
                ?? string.Empty,
            Options = options,
        };
    }

    private static bool IsValidOption(BuyingOption option) =>
        !string.IsNullOrEmpty(option.OfferListingId)
        || !string.IsNullOrEmpty(option.Seller)
        || !string.IsNullOrEmpty(option.Price);

    private static bool AddUnique(
        List<BuyingOption> options,
        HashSet<string> seen,
        BuyingOption option)
    {
        if (!string.IsNullOrEmpty(option.OfferListingId)
            && !seen.Add(option.OfferListingId))
        {
            return false;
        }

        if (options.Any(existing => BuyingOptionsSummary.IsSameOffer(existing, option)))
        {
            return false;
        }

        options.Add(option);
        return true;
    }

    private static BuyingOption ParseOption(
        string block,
        IMarketplaceProfile marketplace,
        string asin)
    {
        var offerIdMatch = AmazonPatterns.AodOfferListingId.Match(block);
        var offerId = offerIdMatch.Success
            ? DecodeOfferId(offerIdMatch.Groups[1].Value)
            : string.Empty;

        var seller = ExtractSeller(block, out var sellerId);
        var (shipping, time) = ReadDelivery(block);
        var condition = ExtractCondition(block);
        var conditionInfo = BuyingConditionClassifier.Parse(condition);

        return new BuyingOption
        {
            OfferListingId = offerId,
            Seller = seller,
            SellerId = sellerId,
            Price = ExtractPrice(block, marketplace),
            Shipping = shipping,
            Condition = condition,
            ConditionType = conditionInfo.ConditionType,
            ConditionGrade = conditionInfo.ConditionGrade,
            Delivery = time,
            DetailUrl = marketplace.BuyingOptionDetailUrl(asin, sellerId),
        };
    }

    /// <summary>
    /// AOD values are percent-encoded once for the add-to-cart form. Decode
    /// them so callers receive the logical offer id (Amazon decodes the form
    /// value once when it processes <c>items[0.base][offerListingId]</c>).
    /// </summary>
    private static string DecodeOfferId(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : WebUtility.UrlDecode(WebUtility.HtmlDecode(value)).Trim();

    private static string ExtractCondition(string block)
    {
        var match = AmazonPatterns.AodOfferHeading.Match(block);
        return match.Success ? HtmlText.Clean(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractPrice(string block, IMarketplaceProfile marketplace)
    {
        // Prefer the structured price spans: the accessibility label can be
        // a sentence ("17パーセントの割引で￥8,300"), and the visible
        // a-offscreen is often empty.
        var whole = AmazonPatterns.PriceWhole.Match(block);
        var fraction = AmazonPatterns.PriceFraction.Match(block);
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

        var label = AmazonPatterns.AodPriceLabel.Match(block);
        if (label.Success)
        {
            var text = HtmlText.Clean(label.Groups[1].Value);
            if (!string.IsNullOrEmpty(text) && !text.StartsWith('{'))
            {
                return text;
            }
        }

        var offscreen = OffscreenPrice().Match(block);
        return offscreen.Success
            ? HtmlText.Clean(offscreen.Groups[1].Value)
            : string.Empty;
    }

    private static string ExtractSeller(string block, out string sellerId)
    {
        sellerId = string.Empty;
        var soldBy = ExtractDivById(block, "aod-offer-soldBy");
        if (string.IsNullOrEmpty(soldBy))
        {
            return string.Empty;
        }

        var link = SellerLink().Match(soldBy);
        if (link.Success)
        {
            sellerId = ExtractSellerId(link.Groups[1].Value);
            return HtmlText.Clean(link.Groups[2].Value);
        }

        // Amazon-sold offers can render the merchant name as plain text.
        var rightColumn = RightColumn().Match(soldBy);
        if (rightColumn.Success)
        {
            var span = SpanText().Match(rightColumn.Groups[1].Value);
            if (span.Success)
            {
                return HtmlText.Clean(span.Groups[1].Value);
            }
        }

        return string.Empty;
    }

    private static string ExtractSellerId(string href)
    {
        var match = SellerId().Match(WebUtility.HtmlDecode(href));
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static (string Shipping, string Time) ReadDelivery(string block)
    {
        var shipping = string.Empty;
        var time = string.Empty;

        foreach (Match match in AmazonPatterns.DeliveryAttr.Matches(block))
        {
            var name = match.Groups[1].Value;
            var value = HtmlText.Clean(match.Groups[2].Value);
            if (name.Equals("price", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(shipping))
            {
                shipping = value;
            }
            else if (name.Equals("time", StringComparison.OrdinalIgnoreCase)
                     && string.IsNullOrEmpty(time))
            {
                time = value;
            }
        }

        return (shipping, time);
    }

    private static string ExtractDivById(string html, string id)
    {
        var match = Regex.Match(
            html,
            $@"<div[^>]*\bid=""{Regex.Escape(id)}""",
            RegexOptions.IgnoreCase);
        return match.Success ? HtmlBlockExtractor.ExtractDiv(html, match.Index) : string.Empty;
    }

    private static int ParseCount(string html)
    {
        var input = CountInput().Match(html);
        if (input.Success)
        {
            var value = ValueAttribute().Match(input.Value);
            if (value.Success && int.TryParse(value.Groups[1].Value, out var count))
            {
                return count;
            }
        }

        var text = CountText().Match(html);
        return text.Success && int.TryParse(text.Groups[1].Value, out var textCount)
            ? textCount
            : 0;
    }

    private static string LowestPrice(
        List<BuyingOption> options,
        IMarketplaceProfile marketplace)
    {
        BuyingOption? lowest = null;
        decimal lowestAmount = 0;

        foreach (var option in options)
        {
            if (!marketplace.Format.TryParsePrice(option.Price, out var amount))
            {
                continue;
            }

            if (lowest is null || amount < lowestAmount)
            {
                lowest = option;
                lowestAmount = amount;
            }
        }

        return lowest?.Price
            ?? options.FirstOrDefault()?.Price
            ?? string.Empty;
    }

    [GeneratedRegex(@"<a[^>]*href=""([^""]*)""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex SellerLink();

    [GeneratedRegex(@"a-col-right[^>]*>([\s\S]*)", RegexOptions.IgnoreCase)]
    private static partial Regex RightColumn();

    [GeneratedRegex(@"<span[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanText();

    [GeneratedRegex(@"[?&](?:seller|smid)=([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SellerId();

    [GeneratedRegex(@"a-offscreen[^>]*>\s*([^<]+?)\s*</span>", RegexOptions.IgnoreCase)]
    private static partial Regex OffscreenPrice();

    [GeneratedRegex(@"<input[^>]*\bid=""aod-total-offer-count""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CountInput();

    [GeneratedRegex(@"\bvalue=""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttribute();

    [GeneratedRegex(
        @"id=""(?:aod-filter-offer-count-string|aod-total-offer-count-string)""[^>]*>\s*(\d+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CountText();
}
