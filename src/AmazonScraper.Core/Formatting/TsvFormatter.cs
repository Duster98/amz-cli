using System.Globalization;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Formatting;

public static class TsvFormatter
{
    public static string Search(IEnumerable<SearchResult> results, bool includeDelivery = false)
    {
        var header = "ASIN\tTitle\tPrice\tRating\tReviews\tCoupon\tSponsored";
        if (includeDelivery)
        {
            header += "\tDelivery";
        }
        header += "\tDeal\tDealId\tOriginalPrice\tDiscount\tChoice\tChoiceText\tChoiceFor";

        var lines = new List<string> { header };
        foreach (var result in results)
        {
            var fields = new List<string>
            {
                result.Asin,
                TextCleaner.Clean(result.Title).Replace('\t', ' '),
                result.Price,
                result.Rating,
                result.Reviews,
                result.Coupon,
                result.Sponsored ? "yes" : string.Empty,
            };
            if (includeDelivery)
            {
                fields.Add(SearchDeliveryFormatter.Format(result).Replace('\t', ' '));
            }

            fields.Add(TextCleaner.Clean(result.Deal ?? string.Empty).Replace('\t', ' '));
            fields.Add(result.DealId ?? string.Empty);
            fields.Add(result.OriginalPrice ?? string.Empty);
            fields.Add(result.Discount ?? string.Empty);
            fields.Add(result.AmazonChoice == true ? "true" : string.Empty);
            fields.Add(TextCleaner.Clean(result.AmazonChoiceText ?? string.Empty));
            fields.Add(TextCleaner.Clean(result.AmazonChoiceFor ?? string.Empty));
            lines.Add(string.Join('\t', fields));
        }

        return string.Join('\n', lines);
    }

    public static string Detail(
        ProductDetail detail,
        bool includeVariants = false,
        bool includeRating = false,
        bool includeReviews = false)
    {
        var lines = new List<string>
        {
            $"ASIN\t{detail.Asin}",
            $"URL\t{detail.Url}",
            $"Title\t{TextCleaner.Clean(detail.Title)}",
            $"Brand\t{detail.Brand}",
            $"Price\t{detail.Price}",
            $"Original\t{detail.OriginalPrice}",
            $"Discount\t{detail.Discount}",
        };

        if (!string.IsNullOrEmpty(detail.ModelName))
        {
            lines.Add($"Model\t{TextCleaner.Clean(detail.ModelName)}");
        }

        if (!string.IsNullOrEmpty(detail.Deal))
        {
            lines.Add($"Deal\t{TextCleaner.Clean(detail.Deal)}");
            if (!string.IsNullOrEmpty(detail.DealId))
            {
                lines.Add($"DealId\t{detail.DealId}");
            }
            if (!string.IsNullOrEmpty(detail.DealEndsText))
            {
                lines.Add($"DealEnds\t{TextCleaner.Clean(detail.DealEndsText)}");
            }
            if (!string.IsNullOrEmpty(detail.DealEndsOn))
            {
                lines.Add($"DealEndsOn\t{detail.DealEndsOn}");
            }
            if (detail.DealEndsInDays is { } days)
            {
                lines.Add($"DealEndsInDays\t{days}");
            }
            if (detail.DealHasCountdown is { } hasCountdown)
            {
                lines.Add($"DealCountdown\t{(hasCountdown ? "true" : "false")}");
            }
        }

        lines.AddRange(new[]
        {
            $"Rating\t{detail.Rating}",
            $"Reviews\t{detail.Reviews}",
            $"Seller\t{detail.Seller}",
            $"ShippedBy\t{detail.ShippedBy}",
            $"Returns\t{detail.Returns}",
            $"Avail\t{detail.Availability}",
        });

        if (detail.Charger is { } charger)
        {
            if (charger.Included is { } included)
            {
                lines.Add($"ChargerIncluded\t{(included ? "true" : "false")}");
            }
            if (!string.IsNullOrEmpty(charger.Label))
            {
                lines.Add($"Charger\t{TextCleaner.Clean(charger.Label)}");
            }
            if (!string.IsNullOrEmpty(charger.Note))
            {
                lines.Add($"ChargerNote\t{TextCleaner.Clean(charger.Note)}");
            }
            if (!string.IsNullOrEmpty(charger.BoxContents))
            {
                lines.Add($"BoxContents\t{TextCleaner.Clean(charger.BoxContents)}");
            }
        }

        if (detail.AmazonChoice == true)
        {
            lines.Add("AmazonChoice\ttrue");
            if (!string.IsNullOrEmpty(detail.AmazonChoiceText))
            {
                lines.Add($"AmazonChoiceText\t{TextCleaner.Clean(detail.AmazonChoiceText)}");
            }
            if (!string.IsNullOrEmpty(detail.AmazonChoiceFor))
            {
                lines.Add($"AmazonChoiceFor\t{TextCleaner.Clean(detail.AmazonChoiceFor)}");
            }
        }

        if (detail.BestSellerRank is { } bestSellerRank)
        {
            lines.Add($"BestSellerRank\t{bestSellerRank}");
        }
        if (!string.IsNullOrEmpty(detail.BestSellerCategory))
        {
            lines.Add($"BestSellerCategory\t{TextCleaner.Clean(detail.BestSellerCategory)}");
        }
        if (!string.IsNullOrEmpty(detail.BestSellerLabel))
        {
            lines.Add($"BestSellerLabel\t{TextCleaner.Clean(detail.BestSellerLabel)}");
        }
        if (!string.IsNullOrEmpty(detail.BestSellerUrl))
        {
            lines.Add($"BestSellerUrl\t{detail.BestSellerUrl}");
        }

        if (includeRating && detail.RatingDetails is { } rating)
        {
            if (rating.Average is { } average)
            {
                lines.Add($"RatingAverage\t{average.ToString("0.#", CultureInfo.InvariantCulture)}");
            }
            if (rating.Count is { } count)
            {
                lines.Add($"RatingCount\t{count}");
            }
            foreach (var (star, percent) in rating.Histogram)
            {
                lines.Add($"Rating{star}\t{percent}");
            }
        }

        if (includeReviews && detail.TopReviews is { } topReviews)
        {
            lines.Add($"ReviewsAvailable\t{topReviews.Count}");
            lines.Add($"ReviewsLocal\t{topReviews.LocalCount}");
            lines.Add($"ReviewsInternational\t{topReviews.InternationalCount}");
            foreach (var review in topReviews.Items)
            {
                lines.Add(string.Join('\t',
                    "Review",
                    TextCleaner.Clean(review.Id),
                    TextCleaner.Clean(review.Rating),
                    TextCleaner.Clean(review.Author),
                    TextCleaner.Clean(review.Title),
                    TextCleaner.Clean(review.Date),
                    review.VerifiedPurchase ? "true" : "false",
                    TextCleaner.Clean(review.Helpful),
                    TextCleaner.Clean(review.Variant),
                    TextCleaner.Clean(review.Body),
                    TextCleaner.Clean(review.Url)));
                if (review.Images is { Count: > 0 } images)
                {
                    foreach (var image in images)
                    {
                        lines.Add($"ReviewImage\t{TextCleaner.Clean(review.Id)}\t{image}");
                    }
                }
            }
        }

        if (detail.Delivery is { } delivery)
        {
            lines.Add($"DeliveryLocation\t{delivery.Location}");
            lines.Add($"DeliveryCountry\t{delivery.Country}");
            lines.Add($"DeliveryPostalCode\t{delivery.PostalCode}");
            lines.Add($"DeliveryCost\t{delivery.Cost}");
            lines.Add($"DeliveryTime\t{delivery.Time}");
            lines.Add($"DeliveryCutoff\t{delivery.Cutoff}");
            if (delivery.Options is { Count: > 1 } options)
            {
                var optionIndex = 2;
                foreach (var option in options.Skip(1))
                {
                    lines.Add(string.Join('\t',
                        $"DeliveryOption {optionIndex++}",
                        option.Type,
                        option.Cost,
                        option.Time,
                        option.Cutoff));
                }
            }
        }

        if (detail.ImportFees is { } importFees)
        {
            if (!string.IsNullOrEmpty(importFees.Price))
            {
                lines.Add($"ImportPrice\t{importFees.Price}");
            }
            if (!string.IsNullOrEmpty(importFees.PriceBasis))
            {
                lines.Add($"ImportPriceBasis\t{importFees.PriceBasis}");
            }
            if (!string.IsNullOrEmpty(importFees.Destination))
            {
                lines.Add($"ImportDestination\t{TextCleaner.Clean(importFees.Destination)}");
            }
            if (!string.IsNullOrEmpty(importFees.Shipping))
            {
                lines.Add($"ImportShipping\t{importFees.Shipping}");
            }
            if (!string.IsNullOrEmpty(importFees.ImportCharges))
            {
                lines.Add($"ImportCharges\t{importFees.ImportCharges}");
            }
            if (!string.IsNullOrEmpty(importFees.LandedTotal))
            {
                lines.Add($"ImportLandedTotal\t{importFees.LandedTotal}");
            }
            lines.Add($"ImportAtCheckout\t{(importFees.AtCheckout ? "true" : "false")}");
            if (!string.IsNullOrEmpty(importFees.Text))
            {
                lines.Add($"ImportText\t{TextCleaner.Clean(importFees.Text)}");
            }
        }

        lines.Add($"Coupon\t{detail.Coupon}");
        lines.Add($"Image\t{detail.Image}");

        if (detail.Variants is { Items.Count: > 0 } variants)
        {
            lines.Add($"VariantsCount\t{variants.Items.Count}");
            lines.Add($"VariantsParent\t{variants.ParentAsin}");
            if (includeVariants)
            {
                lines.Add($"VariantsSelected\t{variants.SelectedAsin}");
                foreach (var variant in variants.Items)
                {
                    var attributes = string.Join(
                        "; ",
                        variants.Dimensions.Keys.Select(key =>
                            $"{key}={variant.Attributes.GetValueOrDefault(key, string.Empty)}"));
                    lines.Add(string.Join('\t',
                        "Variant",
                        variant.Asin,
                        variant.Selected ? "yes" : string.Empty,
                        attributes,
                        variant.Price ?? string.Empty,
                        variant.OriginalPrice ?? string.Empty,
                        variant.Discount ?? string.Empty,
                        variant.Availability ?? string.Empty));
                }
            }
        }

        if (!string.IsNullOrEmpty(detail.FrequentlyReturned))
        {
            lines.Add($"FreqReturned\t{detail.FrequentlyReturned}");
        }

        if (detail.BuyingOptions is { } buyingOptions)
        {
            if (buyingOptions.Count > 0)
            {
                lines.Add($"BuyingOptionsCount\t{buyingOptions.Count}");
            }
            if (!string.IsNullOrEmpty(buyingOptions.LowestPrice))
            {
                lines.Add($"BuyingOptionsFrom\t{buyingOptions.LowestPrice}");
            }

            var index = 1;
            foreach (var option in buyingOptions.Options)
            {
                // Fixed columns: index, offer listing id, seller, seller id,
                // price, shipping, condition, delivery, detail URL.
                lines.Add(string.Join('\t',
                    "BuyingOption",
                    index.ToString(CultureInfo.InvariantCulture),
                    option.OfferListingId,
                    TextCleaner.Clean(option.Seller),
                    option.SellerId,
                    option.Price,
                    TextCleaner.Clean(option.Shipping),
                    TextCleaner.Clean(option.Condition),
                    TextCleaner.Clean(option.Delivery),
                    option.DetailUrl));
                index++;
            }
        }

        return string.Join('\n', lines);
    }

    public static string Compare(IEnumerable<ProductDetail> details)
    {
        var lines = new List<string>
        {
            "ASIN\tTitle\tBrand\tPrice\tOriginal\tDiscount\tCoupon\tRating\t" +
            "Reviews\tSeller\tShippedBy\tAvail\tReturns\tDeliveryCost\tDeliveryTime\tDeliveryLocation\t" +
            "AmazonChoice\tImportPrice\tImportPriceBasis\tImportCharges\tLandedTotal\tImportAtCheckout\tImportText",
        };

        foreach (var detail in details)
        {
            lines.Add(string.Join('\t',
                detail.Asin,
                TextCleaner.Clean(detail.Title),
                detail.Brand,
                detail.Price,
                detail.OriginalPrice,
                detail.Discount,
                detail.Coupon,
                detail.Rating,
                detail.Reviews,
                detail.Seller,
                detail.ShippedBy,
                detail.Availability,
                detail.Returns,
                detail.Delivery?.Cost ?? string.Empty,
                detail.Delivery?.Time ?? string.Empty,
                detail.Delivery?.Location ?? string.Empty,
                detail.AmazonChoice == true ? "true" : string.Empty,
                detail.ImportFees?.Price ?? string.Empty,
                detail.ImportFees?.PriceBasis ?? string.Empty,
                detail.ImportFees?.ImportCharges ?? string.Empty,
                detail.ImportFees?.LandedTotal ?? string.Empty,
                detail.ImportFees is { } importFees
                    ? (importFees.AtCheckout ? "true" : "false")
                    : string.Empty,
                TextCleaner.Clean(detail.ImportFees?.Text ?? string.Empty)));
        }

        return string.Join('\n', lines);
    }
}
