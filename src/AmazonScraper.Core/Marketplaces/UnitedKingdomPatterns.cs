using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Amazon.co.uk patterns. The site serves an English UI; prices are forced to
/// GBP with the i18n-prefs cookie.
/// </summary>
public sealed partial class UnitedKingdomPatterns : IMarketplacePatterns
{
    public Regex SearchTitle => SearchTitleRegex();
    public Regex SearchRating => SearchRatingRegex();
    public Regex SearchReviews => SearchReviewsRegex();
    public Regex SearchPriceSingle => SearchPriceSingleRegex();
    public Regex SearchSponsored => SearchSponsoredRegex();
    public Regex SearchSponsoredPrefix => SearchSponsoredPrefixRegex();
    public Regex BylinePrefix => BylinePrefixRegex();

    public Regex DetailRating => DetailRatingRegex();
    public Regex DetailRatingFallback => DetailRatingFallbackRegex();
    public Regex DetailReviews => DetailReviewsRegex();
    public Regex DetailPriceSingle => DetailPriceSingleRegex();
    public Regex DetailOriginalPrice => DetailOriginalPriceRegex();
    public Regex CouponApply => CouponApplyRegex();
    public Regex CouponSavings => CouponSavingsRegex();
    public Regex CouponPercent => CouponPercentRegex();
    public Regex SpecsSection => SpecsSectionRegex();

    public IReadOnlySet<string> SellerLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "sold by", "seller" };

    public IReadOnlySet<string> ShippingLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ships from", "shipped from", "shipped by", "shipper",
            "dispatches from", "dispatch from", "dispatched from"
        };

    public IReadOnlySet<string> ReturnsLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "returns", "return" };

    [GeneratedRegex(@"<h2[^>]*aria-label=""(?:Sponsored Ad\s*[-–]\s*)?([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchTitleRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*out\s*of\s*5\s*stars", RegexOptions.IgnoreCase)]
    private static partial Regex SearchRatingRegex();

    [GeneratedRegex(@"aria-label=""([\d.,]+)\s+(?:ratings|reviews)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:£\s*)?([\d.,]+)\s*")]
    private static partial Regex SearchPriceSingleRegex();

    [GeneratedRegex(@"/sspa/click|sponsored-ads\.amazon\.co\.uk/clk")]
    private static partial Regex SearchSponsoredRegex();

    [GeneratedRegex(@"^(?:Sponsored Ad|Sponsored)\s*[-–]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SearchSponsoredPrefixRegex();

    [GeneratedRegex(@"^(?:Visit the|Brand\s*:)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BylinePrefixRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*out\s*of\s*5\s*stars", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*out\s*of\s*5\s*stars", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingFallbackRegex();

    [GeneratedRegex(@"acrCustomerReviewText[^>]*>\s*\(?([\d.,]*)")]
    private static partial Regex DetailReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:£\s*)?([\d.,]+)\s*")]
    private static partial Regex DetailPriceSingleRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>\s*(?:Was:\s*)?(?:£|GBP)?\s*([\d.,]+)")]
    private static partial Regex DetailOriginalPriceRegex();

    [GeneratedRegex(@"couponLabelText[^>]*>\s*(?:Apply coupon|Applica coupon)\s*(\d+\s*%)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponApplyRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,200}?((?:Save|Risparmi)\s+[£€]?\s*[\d.,]+|[\d.,]+\s*%?\s*(?:off|di sconto))", RegexOptions.IgnoreCase)]
    private static partial Regex CouponSavingsRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,300}?(\d+\s*%?\s*(?:off|di sconto)?)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponPercentRegex();

    [GeneratedRegex(@"<table[^>]*class=""[^""]*prodDetTable[^""]*""[\s\S]*?</table>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecsSectionRegex();
}
