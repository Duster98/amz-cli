using System.Text.RegularExpressions;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Localized regular expressions and label sets for a marketplace.
/// Structural patterns shared by all Amazon domains live in
/// <see cref="AmazonPatterns"/>.
/// </summary>
public interface IMarketplacePatterns
{
    // Italian and French pages use the first product <h2>; US/UK pages put the
    // brand in the first <h2> and the full title in <h2 aria-label="...">.
    Regex SearchTitle => AmazonPatterns.SearchTitle;

    Regex SearchRating { get; }
    Regex SearchReviews { get; }
    Regex SearchPriceSingle { get; }
    Regex SearchSponsored { get; }
    Regex SearchSponsoredPrefix { get; }
    Regex BylinePrefix { get; }

    Regex DetailRating { get; }
    Regex DetailRatingFallback { get; }
    Regex DetailReviews { get; }
    Regex DetailPriceSingle { get; }
    Regex DetailOriginalPrice { get; }
    Regex CouponApply { get; }
    Regex CouponSavings { get; }
    Regex CouponPercent { get; }
    Regex SpecsSection { get; }

    IReadOnlySet<string> SellerLabels { get; }
    IReadOnlySet<string> ShippingLabels { get; }
    IReadOnlySet<string> ReturnsLabels { get; }
}
