using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Amazon.de patterns. The marketplace may serve German or English UI; both
/// variants are accepted. Prices are EUR.
/// </summary>
public sealed partial class GermanyPatterns : IMarketplacePatterns
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
            "sold by", "seller", "verkauft von", "verkauf durch", "verkäufer"
        };

    public IReadOnlySet<string> ShippingLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ships from", "shipped from", "shipped by", "shipper",
            "dispatches from", "dispatch from", "dispatched from",
            "versand durch", "versand von", "lieferung von"
        };

    public IReadOnlySet<string> ReturnsLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "returns", "return", "rückgabe", "rücksendung"
        };

    [GeneratedRegex(@"<h2[^>]*aria-label=""(?:(?:Sponsored Ad|Gesponsert)\s*[-–]\s*)?([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchTitleRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:out\s*of\s*5\s*stars|von\s*5\s*Sternen)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchRatingRegex();

    [GeneratedRegex(@"aria-label=""([\d.,]+)\s+(?:ratings|reviews|Bewertungen|Rezensionen)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:€|EUR)?\s*([\d.,]+)\s*(?:€|EUR)?")]
    private static partial Regex SearchPriceSingleRegex();

    [GeneratedRegex(@"/sspa/click|sponsored-ads\.amazon\.de/clk")]
    private static partial Regex SearchSponsoredRegex();

    [GeneratedRegex(@"^(?:Sponsored Ad|Sponsored|Gesponsert)\s*[-–:]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SearchSponsoredPrefixRegex();

    [GeneratedRegex(@"^(?:Visit the|Brand\s*:|Besuchen Sie den|Marke\s*:)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BylinePrefixRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:out\s*of\s*5\s*stars|von\s*5\s*Sternen)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:out\s*of\s*5\s*stars|von\s*5\s*Sternen)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingFallbackRegex();

    [GeneratedRegex(@"acrCustomerReviewText[^>]*>\s*\(?([\d.,]*)")]
    private static partial Regex DetailReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:€|EUR)?\s*([\d.,]+)\s*")]
    private static partial Regex DetailPriceSingleRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>\s*(?:Was:|Statt:|UVP:|List:)?\s*(?:€|EUR)?\s*([\d.,]+)")]
    private static partial Regex DetailOriginalPriceRegex();

    [GeneratedRegex(@"couponLabelText[^>]*>\s*(?:Apply coupon|Gutschein anwenden|Applica coupon)\s*(\d+\s*%)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponApplyRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,200}?((?:Save|Sparen|Risparmi)\s+[€]?\s*[\d.,]+|[\d.,]+\s*%?\s*(?:off|di sconto))", RegexOptions.IgnoreCase)]
    private static partial Regex CouponSavingsRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,300}?(\d+\s*%?\s*(?:off|di sconto)?)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponPercentRegex();

    [GeneratedRegex(@"<table[^>]*class=""[^""]*(?:prodDetTable|productDetails)[^""]*""[\s\S]*?</table>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecsSectionRegex();
}
