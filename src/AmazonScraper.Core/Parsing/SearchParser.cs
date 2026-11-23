using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

public sealed class SearchParser(IMarketplaceProfile marketplace)
{
    private static readonly Regex CouponPercentage = new(@"(\d+\s*%)", RegexOptions.Compiled);
    private static readonly Regex AmazonChoiceBadge = new(
        @"data-component-props=""[^""]*amazons-choice[^""]*""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BadgeText = new(
        @"<span[^>]*class=""[^""]*\ba-badge-text\b[^""]*""[^>]*>(?<value>[\s\S]*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BadgeSupplementary = new(
        @"<span[^>]*class=""[^""]*\ba-badge-supplementary-text\b[^""]*""[^>]*>(?<value>[\s\S]*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BadgeAriaLabel = new(
        @"aria-label=""(?<value>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FreeDeliveryText = new(
        @"(?:gratuit|free|kostenlos|gratis|gr[aá]tis|送料無料|無料配送)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SearchResult> Parse(string html, bool includeDelivery = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SearchResult>();

        foreach (Match match in AmazonPatterns.Asin.Matches(html))
        {
            var asin = match.Groups[1].Value;
            if (seen.Contains(asin))
            {
                continue;
            }

            var card = HtmlBlockExtractor.ExtractDiv(html, match.Index);
            if (string.IsNullOrEmpty(card))
            {
                continue;
            }

            // Add-to-cart fragments also carry data-asin and can appear before
            // the actual search card. They have no title/price, so skip them.
            if (IsAuxiliaryCard(card))
            {
                continue;
            }

            // Amazon sometimes renders sponsored carousel placeholders with
            // a spinner; their title/price arrive via a second AJAX request.
            // They are not usable search-result cards, so skip them instead
            // of emitting empty rows.
            if (card.Contains("asin-ajax-lazy-loader-spinner", StringComparison.Ordinal)
                || card.Contains("s-ajax-lazy-loader-asin-carousel-item", StringComparison.Ordinal))
            {
                seen.Add(asin);
                continue;
            }

            seen.Add(asin);

            var titleMatch = marketplace.Patterns.SearchTitle.Match(card);
            var title = titleMatch.Success ? HtmlText.Clean(titleMatch.Groups[1].Value) : string.Empty;
            if (!string.IsNullOrEmpty(title))
            {
                title = marketplace.Patterns.SearchSponsoredPrefix.Replace(title, string.Empty).Trim();
            }
            if (string.IsNullOrEmpty(title))
            {
                var altMatch = AmazonPatterns.SearchAltTitle.Match(card);
                if (altMatch.Success)
                {
                    title = marketplace.Patterns.SearchSponsoredPrefix.Replace(
                        HtmlText.Clean(altMatch.Groups[1].Value), string.Empty);
                }
            }

            var price = string.Empty;
            var wholeFraction = AmazonPatterns.SearchPriceWholeFraction.Match(card);
            if (wholeFraction.Success)
            {
                price = marketplace.Format.Price(
                    wholeFraction.Groups[1].Value,
                    wholeFraction.Groups[2].Value);
            }
            else
            {
                var single = marketplace.Patterns.SearchPriceSingle.Match(card);
                if (single.Success)
                {
                    price = marketplace.Format.SinglePrice(single.Groups[1].Value);
                }
                else
                {
                    var wholeOnly = AmazonPatterns.PriceWhole.Match(card);
                    if (wholeOnly.Success)
                    {
                        price = marketplace.Format.SinglePrice(wholeOnly.Groups[1].Value);
                    }
                }
            }

            string? originalPrice = null;
            string? discount = null;
            foreach (var originalMatch in new[]
                     {
                         AmazonPatterns.SearchOriginalPrice.Match(card),
                         AmazonPatterns.SearchOriginalPriceFallback.Match(card),
                     })
            {
                if (!originalMatch.Success)
                {
                    continue;
                }

                var candidate = NormalizeReferencePrice(
                    HtmlText.Clean(originalMatch.Groups[1].Value),
                    marketplace.Format);
                if (string.IsNullOrWhiteSpace(candidate)
                    || !marketplace.Format.TryParsePrice(price, out var currentAmount)
                    || !marketplace.Format.TryParsePrice(candidate, out var originalAmount)
                    || originalAmount <= currentAmount)
                {
                    continue;
                }

                originalPrice = candidate;
                discount = CalculateDiscount(
                    price,
                    originalPrice,
                    marketplace.Format);
                break;
            }

            var ratingMatch = marketplace.Patterns.SearchRating.Match(card);
            var rating = ratingMatch.Success
                ? marketplace.Format.Rating(ratingMatch.Groups[1].Value)
                : string.Empty;

            var reviewsMatch = marketplace.Patterns.SearchReviews.Match(card);
            var reviews = reviewsMatch.Success
                ? HtmlText.Clean(reviewsMatch.Groups[1].Value)
                : string.Empty;

            var imageMatch = AmazonPatterns.SearchImage.Match(card);
            if (!imageMatch.Success)
            {
                imageMatch = AmazonPatterns.SearchImageFallback.Match(card);
            }
            var image = imageMatch.Success ? imageMatch.Groups[1].Value : string.Empty;

            var coupon = string.Empty;
            var couponMatch = AmazonPatterns.SearchCoupon.Match(card);
            if (couponMatch.Success)
            {
                var rawCoupon = HtmlText.Clean(couponMatch.Groups[1].Value);
                var percentage = CouponPercentage.Match(rawCoupon);
                coupon = percentage.Success
                    ? $"-{percentage.Groups[1].Value.Trim()}"
                    : rawCoupon;
            }

            string? deal = null;
            string? dealId = null;
            var dealBadge = AmazonPatterns.SearchDealBadge.Match(card);
            if (dealBadge.Success)
            {
                deal = HtmlText.Clean(dealBadge.Groups[1].Value);
                if (string.IsNullOrEmpty(deal))
                {
                    deal = "Deal";
                }
            }

            var dealIdMatch = AmazonPatterns.DealId.Match(card);
            if (dealIdMatch.Success)
            {
                dealId = dealIdMatch.Value;
                if (string.IsNullOrEmpty(deal))
                {
                    deal = "Deal";
                }
            }

            string? deliveryText = null;
            string? deliveryCountry = null;
            bool? freeDelivery = null;
            if (includeDelivery)
            {
                var deliveryMessage = AmazonPatterns.SearchDeliveryMessage.Match(card);
                if (deliveryMessage.Success)
                {
                    var text = HtmlText.Clean(deliveryMessage.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        deliveryText = text;
                        freeDelivery = FreeDeliveryText.IsMatch(text);
                    }
                }

                var deliveryCountryMatch = AmazonPatterns.SearchDeliveryCountry.Match(card);
                if (deliveryCountryMatch.Success)
                {
                    var country = CleanDeliveryCountry(
                        HtmlText.Clean(deliveryCountryMatch.Groups[1].Value));
                    if (!string.IsNullOrWhiteSpace(country))
                    {
                        deliveryCountry = country;
                    }
                }
            }

            var (amazonChoiceText, amazonChoiceFor) = ParseAmazonChoice(card);

            results.Add(new SearchResult
            {
                Asin = asin,
                Title = title,
                Price = price,
                Rating = rating,
                Reviews = reviews,
                Image = image,
                Coupon = coupon,
                Sponsored = marketplace.Patterns.SearchSponsored.IsMatch(card),
                DeliveryText = deliveryText,
                FreeDelivery = freeDelivery,
                DeliveryCountry = deliveryCountry,
                Deal = deal,
                DealId = dealId,
                OriginalPrice = originalPrice,
                Discount = discount,
                AmazonChoice = amazonChoiceText is null ? null : true,
                AmazonChoiceText = amazonChoiceText,
                AmazonChoiceFor = amazonChoiceFor,
            });
        }

        return results;
    }

    private static bool IsAuxiliaryCard(string card)
    {
        var tagEnd = card.IndexOf('>');
        var startTag = tagEnd > 0 ? card[..tagEnd] : card;
        return startTag.Contains("atc-faceout-container", StringComparison.OrdinalIgnoreCase)
            || startTag.Contains("data-component-type=\"s-add-to-cart\"", StringComparison.OrdinalIgnoreCase);
    }

    private static (string? Text, string? For) ParseAmazonChoice(string card)
    {
        var badge = AmazonChoiceBadge.Match(card);
        if (!badge.Success)
        {
            return (null, null);
        }

        var start = card.LastIndexOf("<span", badge.Index, StringComparison.Ordinal);
        if (start < 0)
        {
            start = badge.Index;
        }

        var block = ExtractSpanBlock(card, start);
        var text = string.Join(
            " ",
            BadgeText.Matches(block)
                .Select(match => HtmlText.Clean(match.Groups["value"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (string.IsNullOrEmpty(text))
        {
            var aria = BadgeAriaLabel.Match(
                card[Math.Max(0, start - 400)..Math.Min(card.Length, start + block.Length)]);
            if (aria.Success)
            {
                text = HtmlText.Clean(aria.Groups["value"].Value);
            }
        }

        var forText = BadgeSupplementary.Match(block) is { Success: true } supplement
            ? HtmlText.Clean(supplement.Groups["value"].Value)
            : string.Empty;

        return (
            string.IsNullOrEmpty(text) ? "Amazon's Choice" : text,
            string.IsNullOrEmpty(forText) ? null : forText);
    }

    private static string ExtractSpanBlock(string html, int start)
    {
        var tagEnd = html.IndexOf('>', start);
        if (tagEnd < 0)
        {
            return html[start..];
        }

        var depth = 1;
        var index = tagEnd + 1;
        while (index < html.Length)
        {
            var opening = html.IndexOf("<span", index, StringComparison.Ordinal);
            var closing = html.IndexOf("</span>", index, StringComparison.Ordinal);
            if (closing < 0)
            {
                break;
            }

            if (opening >= 0 && opening < closing)
            {
                depth++;
                var openingEnd = html.IndexOf('>', opening + 5);
                if (openingEnd < 0)
                {
                    break;
                }
                index = openingEnd + 1;
            }
            else
            {
                depth--;
                index = closing + 7;
                if (depth == 0)
                {
                    return html[start..index];
                }
            }
        }

        return html[start..Math.Min(html.Length, start + 5000)];
    }

    private static string NormalizeReferencePrice(
        string value,
        IMarketplaceFormat format)
    {
        var numeric = Regex.Replace(
            value,
            @"^(?:List(?: Price)?|リスト|参考価格|通常価格|RRP|Prix conseillé)\s*[:：]\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
        numeric = numeric
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("£", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("￥", string.Empty, StringComparison.Ordinal)
            .Replace("¥", string.Empty, StringComparison.Ordinal)
            .Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("GBP", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("JPY", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return format.SinglePrice(numeric);
    }

    private static string? CalculateDiscount(
        string price,
        string originalPrice,
        IMarketplaceFormat format)
    {
        if (string.IsNullOrWhiteSpace(price)
            || !format.TryParsePrice(price, out var current)
            || !format.TryParsePrice(originalPrice, out var original)
            || original <= current)
        {
            return null;
        }

        var percentage = (int)Math.Round(
            (1 - (current / original)) * 100,
            MidpointRounding.AwayFromZero);
        return percentage > 0 ? $"-{percentage}%" : null;
    }

    private static string CleanDeliveryCountry(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        foreach (var prefix in new[]
                 {
                     "Delivers to",
                     "Ships to",
                     "spedisce a",
                     "Consegna a",
                     "Envío a",
                     "Enviar a",
                     "Livraison à",
                     "Livraison a",
                     "Versand nach",
                     "Lieferung nach",
                     "liefert nach",
                 })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        foreach (var suffix in new[] { "に発送", "へ発送", "に配送", "へ配送" })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                text = text[..^suffix.Length].Trim();
                break;
            }
        }

        return text.Trim(' ', ':', '.');
    }
}
