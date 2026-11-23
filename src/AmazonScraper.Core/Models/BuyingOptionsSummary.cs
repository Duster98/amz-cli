using System.Text.Json.Serialization;

namespace AmazonScraper.Core.Models;

/// <summary>
/// Buying options shown in the product page ("7 options from 87,25 €") and,
/// when available, the individual offers returned by the AOD endpoint.
/// </summary>
public sealed class BuyingOptionsSummary
{
    [JsonPropertyName("count")]
    [JsonPropertyOrder(0)]
    public int Count { get; set; }

    [JsonPropertyName("lowest_price")]
    [JsonPropertyOrder(1)]
    public string LowestPrice { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    [JsonPropertyOrder(2)]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    [JsonPropertyOrder(3)]
    public List<BuyingOption> Options { get; set; } = [];

    /// <summary>
    /// Combines the summary parsed from the product page with the offers
    /// parsed from the AOD response. The product page is authoritative for
    /// the "from" price; AOD is authoritative for the individual offers.
    /// </summary>
    public static BuyingOptionsSummary Combine(
        BuyingOptionsSummary? fromProductPage,
        BuyingOptionsSummary fromAod)
    {
        if (fromProductPage is null)
        {
            return fromAod;
        }

        var options = new List<BuyingOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // The product page can carry the featured offer code while AOD returns
        // the same seller/price/condition under a different (session-specific)
        // code. Prefer the AOD entry to avoid counting the same offer twice.
        foreach (var option in fromProductPage.Options)
        {
            if (fromAod.Options.Any(aod => IsSameOffer(option, aod)))
            {
                continue;
            }

            AddUnique(options, seen, option);
        }

        foreach (var option in fromAod.Options)
        {
            if (MatchesFeaturedOffer(options, option))
            {
                continue;
            }

            AddUnique(options, seen, option);
        }

        var combinedCondition = !string.IsNullOrEmpty(fromProductPage.Condition)
            ? fromProductPage.Condition
            : fromAod.Condition;

        var count = fromProductPage.Count > 0
            ? fromProductPage.Count
            : fromAod.Count > 0
                ? fromAod.Count
                : options.Count;

        return new BuyingOptionsSummary
        {
            Count = count,
            LowestPrice = !string.IsNullOrEmpty(fromProductPage.LowestPrice)
                ? fromProductPage.LowestPrice
                : fromAod.LowestPrice,
            Condition = combinedCondition,
            Options = options,
        };
    }

    /// <summary>
    /// Adds the offers of a further AOD page, skipping duplicates. The total
    /// count stays the value from the first page/product summary.
    /// </summary>
    public void AddOptions(IEnumerable<BuyingOption> options)
    {
        foreach (var option in options)
        {
            if (MatchesFeaturedOffer(Options, option))
            {
                continue;
            }

            if (Options.Any(existing => IsSameOffer(existing, option)))
            {
                continue;
            }

            Options.Add(option);
        }

        if (Options.Count > Count)
        {
            Count = Options.Count;
        }
    }

    private static void AddUnique(
        List<BuyingOption> options,
        HashSet<string> seen,
        BuyingOption option)
    {
        if (!string.IsNullOrEmpty(option.OfferListingId)
            && !seen.Add(option.OfferListingId))
        {
            return;
        }

        options.Add(option);
    }

    /// <summary>
    /// Same physical offer can appear with different offer listing IDs (the
    /// buybox and AOD generate the code independently). Treat seller + price
    /// + condition as the same offer; keep different conditions separate.
    /// </summary>
    /// <summary>
    /// AOD often describes the buybox offer with a generic condition
    /// ("Rinnovato") while the product page has the detailed grade
    /// ("Ricondizionato: eccellente"). Keep the featured version only.
    /// </summary>
    private static bool MatchesFeaturedOffer(
        IEnumerable<BuyingOption> options,
        BuyingOption candidate)
    {
        var featured = options.FirstOrDefault(option => option.IsFeatured);
        if (featured is null)
        {
            return false;
        }

        var featuredSeller = !string.IsNullOrEmpty(featured.SellerId)
            ? featured.SellerId
            : featured.Seller;
        var candidateSeller = !string.IsNullOrEmpty(candidate.SellerId)
            ? candidate.SellerId
            : candidate.Seller;
        return !string.IsNullOrEmpty(featuredSeller)
            && !string.IsNullOrEmpty(candidateSeller)
            && !string.IsNullOrEmpty(featured.Price)
            && !string.IsNullOrEmpty(candidate.Price)
            && string.Equals(
                featuredSeller,
                candidateSeller,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                featured.Price,
                candidate.Price,
                StringComparison.Ordinal);
    }

    internal static bool IsSameOffer(BuyingOption left, BuyingOption right)
    {
        if (!string.IsNullOrEmpty(left.OfferListingId)
            && string.Equals(
                left.OfferListingId,
                right.OfferListingId,
                StringComparison.Ordinal))
        {
            return true;
        }

        var leftSeller = !string.IsNullOrEmpty(left.SellerId) ? left.SellerId : left.Seller;
        var rightSeller = !string.IsNullOrEmpty(right.SellerId) ? right.SellerId : right.Seller;
        if (string.IsNullOrEmpty(leftSeller)
            || string.IsNullOrEmpty(rightSeller)
            || string.IsNullOrEmpty(left.Price)
            || string.IsNullOrEmpty(right.Price))
        {
            return false;
        }

        if (!string.Equals(leftSeller, rightSeller, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(left.Price, right.Price, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrEmpty(left.Condition)
            || string.IsNullOrEmpty(right.Condition)
            || string.Equals(
                NormalizeCondition(left.Condition),
                NormalizeCondition(right.Condition),
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Amazon uses different punctuation for the same condition in the
    /// product-page summary and AOD ("D'occasion: Très bon" vs
    /// "D'occasion - Très bon"). Compare the words, not the separators.
    /// </summary>
    private static string NormalizeCondition(string value) =>
        string.Concat(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit));
}
