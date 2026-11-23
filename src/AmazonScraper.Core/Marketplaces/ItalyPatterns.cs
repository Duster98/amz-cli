using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Italian localized patterns, ported one-to-one from the Python
/// <c>config.py</c> constants.
/// </summary>
public sealed partial class ItalyPatterns : IMarketplacePatterns
{
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
            "venduto da", "venditore"
        };

    public IReadOnlySet<string> ShippingLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "spedizione", "speditore", "spedito da" };

    public IReadOnlySet<string> ReturnsLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "resi", "reso", "restituzione" };

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*su\s*5\s*stelle")]
    private static partial Regex SearchRatingRegex();

    [GeneratedRegex(@"(\d[\d.]*)\s*(?:recensioni|valutazioni)")]
    private static partial Regex SearchReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*([\d.,]+)\s*€")]
    private static partial Regex SearchPriceSingleRegex();

    [GeneratedRegex(@"/sspa/click|sponsored-ads\.amazon\.it/clk")]
    private static partial Regex SearchSponsoredRegex();

    [GeneratedRegex(@"^Annuncio sponsorizzato\s*[-–]\s*")]
    private static partial Regex SearchSponsoredPrefixRegex();

    [GeneratedRegex(@"^(?:Visita lo Store di|Marca:)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BylinePrefixRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*su\s*5\s*stelle")]
    private static partial Regex DetailRatingRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d,]+)\s*out\s*of\s*5", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingFallbackRegex();

    [GeneratedRegex(@"acrCustomerReviewText[^>]*>\s*\(?(\d[\d.]*)")]
    private static partial Regex DetailReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*([\d.,]+)\s*€")]
    private static partial Regex DetailPriceSingleRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>([\d.,]+)\s*€")]
    private static partial Regex DetailOriginalPriceRegex();

    [GeneratedRegex(@"couponLabelText[^>]*>\s*Applica coupon\s*(\d+\s*%)")]
    private static partial Regex CouponApplyRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,200}?(Risparmi\s+\d+[.,]?\d*\s*€|€?\s*\d+[.,]?\d*\s*€?\s*(?:di\s+)?sconto)")]
    private static partial Regex CouponSavingsRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,300}?(\d+\s*%?\s*(?:di\s+sconto)?)")]
    private static partial Regex CouponPercentRegex();

    [GeneratedRegex(@"<table[^>]*class=""[^""]*(?:prodDetTable|productDetails)[^""]*""[\s\S]*?</table>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecsSectionRegex();
}
