using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Amazon.co.jp patterns. The site can serve Japanese or English UI; both
/// variants are accepted where possible.
/// </summary>
public sealed partial class JapanPatterns : IMarketplacePatterns
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
        new HashSet<string>(StringComparer.Ordinal)
        {
            "販売元", "出品者", "sold by", "seller"
        };

    public IReadOnlySet<string> ShippingLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "出荷元", "配送元", "ships from", "shipped by", "shipper"
        };

    public IReadOnlySet<string> ReturnsLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "返品", "返品・返金", "returns", "return"
        };

    [GeneratedRegex(@"<h2[^>]*aria-label=""(?:Sponsored Ad\s*[-–]\s*)?([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchTitleRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*(?:5つ星のうち)?\s*([\d.,]+)\s*(?:out of 5 stars)?")]
    private static partial Regex SearchRatingRegex();

    [GeneratedRegex(@"aria-label=""([\d.,]+)\s*(?:ratings|reviews|レーティング)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:￥|¥)?\s*([\d.,]+)\s*")]
    private static partial Regex SearchPriceSingleRegex();

    [GeneratedRegex(@"/sspa/click|sponsored-ads\.amazon\.co\.jp/clk")]
    private static partial Regex SearchSponsoredRegex();

    [GeneratedRegex(@"^(?:Sponsored(?: Ad)?|スポンサー広告|スポンサー)\s*[-–]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SearchSponsoredPrefixRegex();

    [GeneratedRegex(@"^(?:Visit the|Brand\s*:|ブランド\s*[:：]?|メーカー\s*[:：]?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BylinePrefixRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*(?:5つ星のうち)?\s*([\d.,]+)\s*(?:out of 5 stars)?")]
    private static partial Regex DetailRatingRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:out of 5 stars|5つ星のうち)")]
    private static partial Regex DetailRatingFallbackRegex();

    [GeneratedRegex(@"acrCustomerReviewText[^>]*>\s*\(?([\d.,]*)")]
    private static partial Regex DetailReviewsRegex();

    [GeneratedRegex(@"a-price(?![^>]*(?:a-text-price|basisprice|data-a-strike=""true""))[^>]*>\s*<span[^>]*>\s*(?:￥|¥)?\s*([\d.,]+)\s*")]
    private static partial Regex DetailPriceSingleRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>\s*(?:List:|参考価格:?)?\s*(?:￥|¥|JPY)?\s*([\d.,]+)")]
    private static partial Regex DetailOriginalPriceRegex();

    [GeneratedRegex(@"couponLabelText[^>]*>\s*(?:クーポン|Apply coupon|Applica coupon)\s*(\d+\s*%)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponApplyRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,200}?((?:Save|Risparmi|節約)\s*[￥¥]?\s*[\d.,]+|[\d.,]+\s*%?\s*(?:off|di sconto|オフ))", RegexOptions.IgnoreCase)]
    private static partial Regex CouponSavingsRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,300}?(\d+\s*%?\s*(?:off|di sconto|オフ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponPercentRegex();

    [GeneratedRegex(@"<table[^>]*class=""[^""]*(?:prodDetTable|productDetails)[^""]*""[\s\S]*?</table>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecsSectionRegex();
}
