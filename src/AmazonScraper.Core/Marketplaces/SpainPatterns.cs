using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Amazon.es patterns. The marketplace may serve Spanish or English UI, so
/// both variants are accepted.
/// </summary>
public sealed partial class SpainPatterns : IMarketplacePatterns
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
            "vendido por", "vendedor", "sold by", "seller"
        };

    public IReadOnlySet<string> ShippingLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "remitente", "enviado por", "enviado", "expedido por",
            "ships from", "shipped by", "shipper"
        };

    public IReadOnlySet<string> ReturnsLabels { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "devoluciones", "devolución", "returns", "return"
        };

    [GeneratedRegex(@"<h2[^>]*aria-label=""(?:(?:Anuncio patrocinado|Sponsored Ad)\s*[-–]\s*)?([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchTitleRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:de\s*5\s*estrellas|out of 5 stars)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchRatingRegex();

    // Amazon.es uses different review-count labels across card layouts:
    //   aria-label="205 ratings"
    //   aria-label="6 calificaciones"
    //   aria-label="Tiene una valoración de 5 de 5 estrellas por 1 reseñas..."
    [GeneratedRegex(@"aria-label=""(?:(?:Tiene una valoración de )?[\d.,]+ de 5 estrellas por )?([\d.,]+)\s*(?:reseñas?|ratings|reviews|valoraciones|opiniones|calificaciones)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:€|EUR)?\s*([\d.,]+)\s*(?:€|EUR)?")]
    private static partial Regex SearchPriceSingleRegex();

    [GeneratedRegex(@"/sspa/click|sponsored-ads\.amazon\.es/clk")]
    private static partial Regex SearchSponsoredRegex();

    [GeneratedRegex(@"^(?:Anuncio patrocinado|Sponsored Ad|Sponsored)\s*[-–:]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SearchSponsoredPrefixRegex();

    [GeneratedRegex(@"^(?:Visitar la tienda de|Visita la tienda de|Visit the|Marca\s*:|Brand\s*:)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BylinePrefixRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:de\s*5\s*estrellas|out of 5 stars)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingRegex();

    [GeneratedRegex(@"a-icon-alt[^>]*>\s*([\d.,]+)\s*(?:de\s*5\s*estrellas|out of 5 stars)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRatingFallbackRegex();

    [GeneratedRegex(@"acrCustomerReviewText[^>]*>\s*\(?([\d.,]*)")]
    private static partial Regex DetailReviewsRegex();

    [GeneratedRegex(@"a-price[^>]*>\s*<span[^>]*>\s*(?:€|EUR)?\s*([\d.,]+)\s*(?:€|EUR)?")]
    private static partial Regex DetailPriceSingleRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>\s*(?:PVP:|Precio recomendado:|List:)?\s*(?:€|EUR)?\s*([\d.,]+)\s*(?:€|EUR)?")]
    private static partial Regex DetailOriginalPriceRegex();

    [GeneratedRegex(@"couponLabelText[^>]*>\s*(?:Aplicar cupón|Apply coupon|Applica coupon)\s*(\d+\s*%)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponApplyRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,200}?((?:Ahorra|Save|Risparmi)\s+[€]?\s*[\d.,]+|[\d.,]+\s*%?\s*(?:de descuento|off|di sconto))", RegexOptions.IgnoreCase)]
    private static partial Regex CouponSavingsRegex();

    [GeneratedRegex(@"newCouponBadge[^>]*>[^<]*</i>[\s\S]{0,300}?(\d+\s*%?\s*(?:de descuento|off)?)", RegexOptions.IgnoreCase)]
    private static partial Regex CouponPercentRegex();

    [GeneratedRegex(@"<table[^>]*class=""[^""]*(?:prodDetTable|productDetails)[^""]*""[\s\S]*?</table>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecsSectionRegex();
}
