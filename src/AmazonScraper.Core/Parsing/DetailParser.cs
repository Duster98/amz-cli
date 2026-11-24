using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

public sealed partial class DetailParser(IMarketplaceProfile marketplace)
{
    public ProductDetail Parse(
        string html,
        string asin,
        string? deliveryCountry = null,
        string? deliveryPostalCode = null)
    {
        var title = string.Empty;
        var titleMatch = AmazonPatterns.DetailTitle.Match(html);
        if (!titleMatch.Success)
        {
            titleMatch = AmazonPatterns.DetailTitleFallback.Match(html);
        }
        if (titleMatch.Success)
        {
            title = HtmlText.Clean(titleMatch.Groups[1].Value);
        }

        var subtitle = string.Empty;
        var subtitleMatch = TitleSubtitleRegex().Match(html);
        if (subtitleMatch.Success)
        {
            subtitle = HtmlText.Clean(subtitleMatch.Groups["subtitle"].Value);
        }

        var brand = string.Empty;
        var brandMatch = AmazonPatterns.BrandPrimary.Match(html);
        if (!brandMatch.Success)
        {
            brandMatch = AmazonPatterns.BrandSecondary.Match(html);
        }

        if (brandMatch.Success)
        {
            brand = HtmlText.Clean(brandMatch.Groups[1].Value);
        }
        else
        {
            var byline = AmazonPatterns.Byline.Match(html);
            if (byline.Success)
            {
                brand = marketplace.Patterns.BylinePrefix.Replace(
                    HtmlText.Clean(byline.Groups[1].Value), string.Empty);
            }
            else
            {
                var brandFallback = AmazonPatterns.BrandFallback.Match(html);
                if (brandFallback.Success)
                {
                    brand = HtmlText.Clean(brandFallback.Groups[1].Value);
                }
                else
                {
                    var storeFallback = AmazonPatterns.BrandStoreFallback.Match(html);
                    if (storeFallback.Success)
                    {
                        brand = HtmlText.Clean(storeFallback.Groups[1].Value);
                    }
                    else
                    {
                        var logoFallback = AmazonPatterns.BrandLogoFallback.Match(html);
                        if (logoFallback.Success)
                        {
                            brand = HtmlText.Clean(logoFallback.Groups[1].Value);
                        }
                    }
                }
            }
        }

        brand = brand.Trim();
        if (brand.EndsWith(" Store", StringComparison.OrdinalIgnoreCase))
        {
            brand = brand[..^" Store".Length].Trim();
        }
        if (brand.EndsWith("のストアを表示", StringComparison.Ordinal))
        {
            brand = brand[..^"のストアを表示".Length].Trim();
        }
        if (brand.StartsWith("Visit the ", StringComparison.OrdinalIgnoreCase)
            && brand.EndsWith(" Store", StringComparison.OrdinalIgnoreCase))
        {
            brand = brand["Visit the ".Length..^" Store".Length].Trim();
        }

        var (displayHtml, displayMobile) = FindPriceBlock(html);
        ReadPriceDetails(
            displayHtml,
            displayMobile,
            out var displayPrice,
            out var displayOriginalPrice,
            out var displayDiscount);

        var buybox = FindBuyboxPriceBlocks(html);
        string price;
        string originalPrice;
        string discount;
        PrimePricing? prime = null;

        if (buybox.Standard is not null)
        {
            ReadPriceDetails(
                buybox.Standard,
                false,
                out price,
                out originalPrice,
                out discount);
            if (string.IsNullOrWhiteSpace(price) && buybox.Prime is not null)
            {
                ReadPriceDetails(
                    buybox.Prime,
                    false,
                    out price,
                    out originalPrice,
                    out discount);
                if (!string.IsNullOrWhiteSpace(price))
                {
                    prime = new PrimePricing
                    {
                        Price = price,
                        OriginalPrice = originalPrice,
                        Discount = discount,
                        Only = true,
                    };
                }
            }
            else if (buybox.Prime is not null)
            {
                ReadPriceDetails(
                    buybox.Prime,
                    false,
                    out var primePrice,
                    out var primeOriginalPrice,
                    out var primeDiscount);
                if (!string.IsNullOrWhiteSpace(primePrice))
                {
                    if (displayPrice == primePrice)
                    {
                        primeOriginalPrice = displayOriginalPrice;
                        primeDiscount = displayDiscount;
                    }
                    prime = new PrimePricing
                    {
                        Price = primePrice,
                        OriginalPrice = primeOriginalPrice,
                        Discount = primeDiscount,
                        Only = false,
                    };
                }
            }
        }
        else if (buybox.Prime is not null)
        {
            ReadPriceDetails(
                buybox.Prime,
                false,
                out price,
                out originalPrice,
                out discount);
            if (!string.IsNullOrWhiteSpace(price))
            {
                if (displayPrice == price)
                {
                    originalPrice = displayOriginalPrice;
                    discount = displayDiscount;
                }
                prime = new PrimePricing
                {
                    Price = price,
                    OriginalPrice = originalPrice,
                    Discount = discount,
                    Only = true,
                };
            }
        }
        else
        {
            price = displayPrice;
            originalPrice = displayOriginalPrice;
            discount = displayDiscount;
        }

        if (string.IsNullOrWhiteSpace(price))
        {
            price = displayPrice;
            originalPrice = displayOriginalPrice;
            discount = displayDiscount;
        }

        var rating = string.Empty;
        var ratingMatch = marketplace.Patterns.DetailRating.Match(html);
        if (!ratingMatch.Success)
        {
            ratingMatch = marketplace.Patterns.DetailRatingFallback.Match(html);
        }
        if (ratingMatch.Success)
        {
            rating = marketplace.Format.Rating(ratingMatch.Groups[1].Value);
        }

        var reviews = string.Empty;
        var reviewsMatch = marketplace.Patterns.DetailReviews.Match(html);
        if (reviewsMatch.Success)
        {
            reviews = reviewsMatch.Groups[1].Value.Trim();
        }
        else
        {
            var reviewsFallback = AmazonPatterns.DetailReviewsFallback.Match(html);
            if (reviewsFallback.Success)
            {
                reviews = reviewsFallback.Groups[1].Value.Trim();
            }
        }

        reviews = reviews
            .Replace('\u00a0', ' ')
            .Replace('\u202f', ' ')
            .Trim();

        var seller = string.Empty;
        var shippedBy = string.Empty;
        var returns = string.Empty;

        var featuresSection = AmazonPatterns.FeaturesSection.Match(html);
        if (featuresSection.Success)
        {
            foreach (Match feature in AmazonPatterns.Feature.Matches(featuresSection.Value))
            {
                var label = HtmlText.Clean(feature.Groups[1].Value);
                var text = HtmlText.Clean(feature.Groups[2].Value);
                var labelLower = label.ToLowerInvariant();

                if (marketplace.Patterns.SellerLabels.Any(labelName => labelLower.Contains(labelName, StringComparison.Ordinal)))
                {
                    seller = text;
                }
                if (marketplace.Patterns.ShippingLabels.Any(labelName => labelLower.Contains(labelName, StringComparison.Ordinal)))
                {
                    shippedBy = text;
                }
                if (marketplace.Patterns.ReturnsLabels.Contains(labelLower) && string.IsNullOrEmpty(returns))
                {
                    returns = Deduplicate(text);
                }
            }
        }

        if (string.IsNullOrEmpty(seller))
        {
            var sellerFallback = AmazonPatterns.SellerFallback.Match(html);
            if (sellerFallback.Success)
            {
                seller = HtmlText.Clean(sellerFallback.Groups[1].Value);
            }
        }

        var delivery = ExtractDelivery(html);
        if (delivery is not null)
        {
            delivery.Country = deliveryCountry ?? string.Empty;
            delivery.PostalCode = deliveryPostalCode ?? string.Empty;
            if (string.IsNullOrEmpty(delivery.Location))
            {
                delivery.Location = !string.IsNullOrEmpty(delivery.PostalCode)
                    ? delivery.PostalCode
                    : delivery.Country;
            }
        }

        var (amazonChoiceText, amazonChoiceFor) =
            ProductBadgesParser.ParseAmazonChoice(html);
        var importFees = ProductBadgesParser.ParseImportFees(
            html,
            delivery,
            price,
            originalPrice,
            marketplace.Format);

        var availability = string.Empty;
        var availabilityMatch = AmazonPatterns.Availability.Match(html);
        if (availabilityMatch.Success)
        {
            availability = HtmlText.Clean(availabilityMatch.Groups[1].Value);
        }
        if (string.IsNullOrEmpty(availability) && delivery is not null)
        {
            availability = delivery.Raw;
        }

        if (string.IsNullOrEmpty(returns))
        {
            var returnsFallback = AmazonPatterns.ReturnsFallback.Match(html);
            if (returnsFallback.Success && !string.IsNullOrWhiteSpace(returnsFallback.Groups[1].Value))
            {
                returns = HtmlText.Clean(returnsFallback.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(returns))
        {
            var warrantyReturns = AmazonPatterns.WarrantyReturns.Match(html);
            if (warrantyReturns.Success
                && !string.IsNullOrWhiteSpace(warrantyReturns.Groups[1].Value))
            {
                returns = HtmlText.Clean(warrantyReturns.Groups[1].Value);
            }
        }

        var coupon = string.Empty;
        var couponApply = marketplace.Patterns.CouponApply.Match(html);
        if (couponApply.Success)
        {
            coupon = $"-{couponApply.Groups[1].Value.Trim()}";
        }
        else
        {
            var couponMatch = marketplace.Patterns.CouponSavings.Match(html);
            if (!couponMatch.Success)
            {
                couponMatch = marketplace.Patterns.CouponPercent.Match(html);
            }
            if (couponMatch.Success)
            {
                coupon = HtmlText.Clean(couponMatch.Groups[1].Value);
            }
        }

        var numericCoupon = coupon.Length > 0
            && coupon.All(character =>
                char.IsDigit(character) || character is ',' or '.');
        if (string.IsNullOrEmpty(coupon) || numericCoupon)
        {
            var percentageCoupon = AmazonPatterns.DetailCouponPercent.Match(html);
            if (percentageCoupon.Success)
            {
                coupon = $"-{percentageCoupon.Groups[1].Value}%";
            }
            else if (numericCoupon)
            {
                // An internal coupon amount (for example "9900") is not a
                // user-facing discount.
                coupon = string.Empty;
            }
        }

        var deal = ExtractDealInfo(html);

        var gallery = GalleryParser.Parse(html);
        var image = gallery.Count > 0 ? gallery[0] : string.Empty;
        if (string.IsNullOrEmpty(image))
        {
            var imageFallback = AmazonPatterns.DetailImageFallback.Match(html);
            if (imageFallback.Success)
            {
                image = HtmlText.Clean(imageFallback.Groups[1].Value);
            }
            else
            {
                var hiResFallback = AmazonPatterns.DetailHiResFallback.Match(html);
                if (hiResFallback.Success)
                {
                    image = HtmlText.Clean(hiResFallback.Groups[1].Value);
                }
            }
        }
        var images = gallery.Count > 1 ? gallery : [];

        var description = new List<string>();
        var bulletsSection = AmazonPatterns.FeatureBullets.Match(html);
        if (bulletsSection.Success)
        {
            foreach (Match bullet in AmazonPatterns.Bullet.Matches(bulletsSection.Value))
            {
                var text = HtmlText.Clean(bullet.Groups[1].Value);
                if (!string.IsNullOrEmpty(text))
                {
                    description.Add(text);
                }
            }
        }

        var specs = new Dictionary<string, string>(StringComparer.Ordinal);
        var amazonDetails = new List<ProductDetailExtra>();
        var seenDetails = new HashSet<string>(StringComparer.Ordinal);
        var specsSections = marketplace.Patterns.SpecsSection.Matches(html);
        for (var sectionIndex = 0; sectionIndex < specsSections.Count; sectionIndex++)
        {
            var section = specsSections[sectionIndex];
            if (sectionIndex == 0)
            {
                foreach (Match row in AmazonPatterns.SpecRow.Matches(section.Value))
                {
                    var key = HtmlText.Clean(row.Groups[1].Value);
                    var value = HtmlText.Clean(row.Groups[2].Value);
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        specs[key] = value;
                    }
                }

                continue;
            }

            // Secondary tables (manufacturer, product type, sales rank, ...)
            // are kept separate from the technical specs.
            foreach (Match row in AmazonPatterns.SpecRowLoose.Matches(section.Value))
            {
                var key = HtmlText.Clean(row.Groups[1].Value);
                var rawValue = row.Groups[2].Value;
                if (IsReviewSummaryCell(rawValue))
                {
                    // The secondary table includes an interactive review
                    // summary (with inline JavaScript) that is already
                    // exposed through rating_details.
                    continue;
                }

                var value = CleanAmazonDetailValue(rawValue);
                if (string.IsNullOrEmpty(key)
                    || string.IsNullOrEmpty(value)
                    || specs.ContainsKey(key)
                    || !seenDetails.Add(key))
                {
                    continue;
                }

                amazonDetails.Add(new ProductDetailExtra
                {
                    Name = key,
                    Value = value,
                });
            }
        }

        var modelName = ExtractModelName(amazonDetails, specs);
        var charger = ExtractCharger(html, description, specs, amazonDetails);

        var frequentlyReturned = string.Empty;
        var modernReturned = AmazonPatterns.FrequentlyReturnedModern.Match(html);
        if (modernReturned.Success)
        {
            frequentlyReturned = HtmlText.Clean(modernReturned.Groups[1].Value);
        }
        else
        {
            var frequentlyReturnedMatch = AmazonPatterns.FrequentlyReturned.Match(html);
            if (frequentlyReturnedMatch.Success)
            {
                frequentlyReturned = HtmlText.Clean(frequentlyReturnedMatch.Groups[2].Value);
            }
        }

        var usuallyKept = ExtractUsuallyKept(html);

        var bestSeller = ProductBadgesParser.ParseBestSeller(html, marketplace);
        var variants = ProductVariantsParser.Parse(html, asin, marketplace);
        var buyingOptions = BuyingOptionsSummaryParser.Parse(html, marketplace);
        if (buyingOptions is not null
            && !string.IsNullOrEmpty(price)
            && TryParseFeaturedOffer(
                html,
                marketplace,
                asin,
                price,
                seller,
                delivery,
                buyingOptions.Condition,
                out var featuredOffer))
        {
            // AOD lists the other sellers when the buybox has a featured
            // offer; the product page carries the featured offer code.
            buyingOptions.Options.Insert(0, featuredOffer);
        }

        return new ProductDetail
        {
            Asin = asin,
            Title = title,
            Subtitle = subtitle,
            Brand = brand,
            Price = price,
            OriginalPrice = originalPrice,
            Discount = discount,
            Prime = prime,
            Rating = rating,
            Reviews = reviews,
            Seller = seller,
            ShippedBy = shippedBy,
            Returns = returns,
            Availability = availability,
            Coupon = coupon,
            Image = image,
            FrequentlyReturned = frequentlyReturned,
            UsuallyKept = usuallyKept,
            Variants = variants,
            Images = images,
            Description = description,
            Specs = specs,
            SafetyWarnings = SafetyWarningsParser.Parse(html),
            AmazonDetails = amazonDetails.Count > 0 ? amazonDetails : null,
            Charger = charger,
            Url = marketplace.ProductUrl(asin),
            Delivery = delivery,
            BuyingOptions = buyingOptions,
            Deal = deal.Label,
            DealId = deal.Id,
            DealEndsText = deal.EndsText,
            DealEndsOn = deal.EndsOn,
            DealEndsInDays = deal.EndsInDays,
            DealHasCountdown = deal.HasCountdown,
            AmazonChoice = string.IsNullOrEmpty(amazonChoiceText) ? null : true,
            AmazonChoiceText = string.IsNullOrEmpty(amazonChoiceText) ? null : amazonChoiceText,
            AmazonChoiceFor = string.IsNullOrEmpty(amazonChoiceFor) ? null : amazonChoiceFor,
            BestSellerRank = bestSeller?.Rank,
            BestSellerCategory = bestSeller?.Category,
            BestSellerLabel = bestSeller?.Label,
            BestSellerUrl = bestSeller?.Url,
            ModelName = modelName,
            ImportFees = importFees,
        };
    }

    private static bool IsReviewSummaryCell(string rawValue)
    {
        // These IDs are shared by every Amazon marketplace and identify the
        // rating widget rather than a static product attribute.
        return rawValue.Contains(
                "averageCustomerReviews",
                StringComparison.OrdinalIgnoreCase)
            || rawValue.Contains(
                "acrCustomerReviewText",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanAmazonDetailValue(string fragment)
    {
        var items = Regex.Matches(
            fragment,
            @"<li[^>]*>([\s\S]*?)</li>",
            RegexOptions.IgnoreCase);
        if (items.Count > 0)
        {
            return string.Join(
                " · ",
                items
                    .Select(item => HtmlText.Clean(item.Groups[1].Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return HtmlText.Clean(fragment);
    }

    private static bool TryParseFeaturedOffer(
        string html,
        IMarketplaceProfile marketplace,
        string asin,
        string price,
        string seller,
        DeliveryInfo? delivery,
        string condition,
        out BuyingOption option)
    {
        option = null!;

        var offerInput = AmazonPatterns.FeaturedOfferInput.Match(html);
        if (!offerInput.Success)
        {
            // Used-only pages use a separate featured offer input.
            offerInput = AmazonPatterns.UsedOfferInput.Match(html);
        }
        if (!offerInput.Success)
        {
            return false;
        }

        var offerValue = AmazonPatterns.HtmlValueAttribute.Match(offerInput.Value);
        if (!offerValue.Success)
        {
            return false;
        }

        var offerListingId = WebUtility
            .UrlDecode(WebUtility.HtmlDecode(offerValue.Groups[1].Value))
            .Trim();
        if (string.IsNullOrEmpty(offerListingId))
        {
            return false;
        }

        var sellerId = ExtractFeaturedSellerId(html);
        var featuredCondition = ExtractFeaturedCondition(html, condition);
        var resolvedCondition = string.IsNullOrEmpty(featuredCondition)
            ? condition
            : featuredCondition;
        var conditionInfo = BuyingConditionClassifier.Parse(resolvedCondition);
        option = new BuyingOption
        {
            OfferListingId = offerListingId,
            Seller = seller,
            SellerId = sellerId,
            Price = price,
            Shipping = delivery?.Cost ?? string.Empty,
            Condition = resolvedCondition,
            ConditionType = conditionInfo.ConditionType,
            ConditionGrade = conditionInfo.ConditionGrade,
            Delivery = delivery?.Time ?? string.Empty,
            DetailUrl = marketplace.BuyingOptionDetailUrl(asin, sellerId),
            IsFeatured = true,
        };
        return true;
    }

    /// <summary>
    /// Reads the condition of the active buybox offer ("新品", "中古 - 良い",
    /// "Nuovo") instead of the mixed summary ("新品＆中古品"). When the
    /// accordion is missing (for example Amazon.fr), uses buyingOptionType
    /// (NEW/USED) to choose the matching part of the summary.
    /// </summary>
    internal static string ExtractFeaturedCondition(
        string html,
        string summaryCondition = "")
    {
        var renewed = HtmlBlockExtractor.ExtractDivById(
            html,
            "renewedSingleOfferCaption_feature_div");
        if (!string.IsNullOrWhiteSpace(renewed))
        {
            var renewedCondition = HtmlText.Clean(renewed);
            if (!string.IsNullOrEmpty(renewedCondition))
            {
                return renewedCondition;
            }
        }

        var match = AmazonPatterns.FeaturedCondition.Match(html);
        if (match.Success)
        {
            return HtmlText.Clean(match.Groups[1].Value);
        }

        var type = AmazonPatterns.BuyingOptionType.Match(html);
        if (!type.Success || string.IsNullOrEmpty(summaryCondition))
        {
            return string.Empty;
        }

        var buyingOptionType = type.Groups[1].Success
            ? type.Groups[1].Value
            : type.Groups[2].Value;
        var separator = summaryCondition.IndexOfAny(['&', '＆']);
        if (separator < 0)
        {
            // A single condition ("D'occasion: Très bon") belongs to the
            // featured offer regardless of NEW/USED.
            return summaryCondition.Trim();
        }

        return buyingOptionType.Equals("NEW", StringComparison.OrdinalIgnoreCase)
            ? summaryCondition[..separator].Trim()
            : summaryCondition[(separator + 1)..].Trim();
    }

    private static string ExtractFeaturedSellerId(string html)
    {
        var merchantInput = AmazonPatterns.FeaturedMerchantInput.Match(html);
        if (!merchantInput.Success)
        {
            merchantInput = AmazonPatterns.UsedMerchantInput.Match(html);
        }
        if (merchantInput.Success)
        {
            var merchantValue = AmazonPatterns.HtmlValueAttribute.Match(merchantInput.Value);
            if (merchantValue.Success)
            {
                var merchantId = WebUtility.HtmlDecode(merchantValue.Groups[1].Value).Trim();
                if (!string.IsNullOrEmpty(merchantId))
                {
                    return merchantId;
                }
            }
        }

        var anchor = AmazonPatterns.SellerProfileAnchor.Match(html);
        if (!anchor.Success)
        {
            return string.Empty;
        }

        var href = AmazonPatterns.HrefAttribute.Match(anchor.Value);
        if (!href.Success)
        {
            return string.Empty;
        }

        var seller = Regex.Match(
            WebUtility.HtmlDecode(href.Groups[1].Value),
            @"[?&](?:seller|smid)=([A-Za-z0-9]+)",
            RegexOptions.IgnoreCase);
        return seller.Success ? seller.Groups[1].Value : string.Empty;
    }

    private static string ExtractUsuallyKept(string html)
    {
        var block = HtmlBlockExtractor.ExtractDivById(
            html,
            "buyingOptionRipersInlineBadge_feature_div");
        if (string.IsNullOrEmpty(block)
            || !block.Contains("a-alert-success", StringComparison.OrdinalIgnoreCase)
            || !block.Contains("ripers-lrr-badge", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            block,
            @"<p[^>]*>\s*<span[^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);
        return match.Success
            ? HtmlText.Clean(match.Groups[1].Value)
            : string.Empty;
    }

    private static (string? Standard, string? Prime) FindBuyboxPriceBlocks(
        string html)
    {
        var buybox = HtmlBlockExtractor.ExtractDivById(html, "desktop_buybox");
        var scope = string.IsNullOrEmpty(buybox) ? html : buybox;
        var prime = HtmlBlockExtractor.ExtractDivById(
            scope,
            "primeSavingsUpsellAccordionRow");
        var standard = HtmlBlockExtractor.ExtractDivById(
            scope,
            "newAccordionRow_1");
        if (string.IsNullOrEmpty(standard))
        {
            standard = HtmlBlockExtractor.ExtractDivById(scope, "newAccordionRow");
        }

        return (
            string.IsNullOrEmpty(standard) ? null : standard,
            string.IsNullOrEmpty(prime) ? null : prime);
    }

    private void ReadPriceDetails(
        string html,
        bool mobilePriceBlock,
        out string price,
        out string originalPrice,
        out string discount)
    {
        price = ExtractPrice(html, mobilePriceBlock) ?? string.Empty;

        originalPrice = string.Empty;
        foreach (var originalMatch in new[]
                 {
                     marketplace.Patterns.DetailOriginalPrice.Match(html),
                     AmazonPatterns.DetailOriginalPriceFallback.Match(html),
                 })
        {
            if (!originalMatch.Success)
            {
                continue;
            }

            var candidate = marketplace.Format.OriginalPrice(
                originalMatch.Groups[1].Value);
            if (IsHigherThanCurrent(candidate, price, marketplace.Format))
            {
                originalPrice = candidate;
                break;
            }
        }

        discount = string.Empty;
        var discountMatch = AmazonPatterns.Discount.Match(html);
        if (discountMatch.Success)
        {
            discount = $"-{discountMatch.Groups[1].Value}%";
        }
        else if (TryComputeDiscount(
                     price,
                     originalPrice,
                     marketplace.Format,
                     out var computedDiscount))
        {
            discount = computedDiscount;
        }
    }

    private (string Html, bool MobilePriceBlock) FindPriceBlock(string html)
    {
        string? mobileHeaderFallback = null;
        foreach (var blockId in AmazonPatterns.PriceBlockIds)
        {
            var match = Regex.Match(html, $@"<div[^>]*\sid=""{Regex.Escape(blockId)}""");
            if (!match.Success)
            {
                continue;
            }

            var block = HtmlBlockExtractor.ExtractDiv(html, match.Index);
            if (!block.Contains("a-price", StringComparison.Ordinal))
            {
                continue;
            }

            // Mobile pages repeat the price in a small header block that does
            // not contain the original/RRP price. Keep it as a fallback and
            // prefer the real buybox block below.
            if (IsMobileHeaderPriceBlock(block))
            {
                mobileHeaderFallback ??= block;
                continue;
            }

            return (block, false);
        }

        // No featured offer: the buybox has no price. Check before mobile
        // fallbacks so sponsored carousel prices are never used.
        if (AmazonPatterns.NoFeaturedOffer.IsMatch(html))
        {
            return (string.Empty, false);
        }

        // Mobile detail pages put the main price in `priceToPay`. Include a
        // small amount of context before it for the savings percentage and a
        // few thousand characters after it for the "Was"/RRP price. Sponsored
        // carousels use `apex-price-to-pay-value` and are not matched here.
        // The mobile header repeats the price with `reinventMobileHeaderPrice`;
        // prefer the real buybox block, which also contains the original price.
        Match? mobileFallback = null;
        foreach (Match mobilePrice in AmazonPatterns.MobilePrice.Matches(html))
        {
            mobileFallback ??= mobilePrice;
            if (mobilePrice.Value.Contains("reinventMobileHeaderPrice", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = Math.Max(0, mobilePrice.Index - 500);
            var length = Math.Min(3500, html.Length - start);
            return (html.Substring(start, length), true);
        }

        if (mobileFallback is not null)
        {
            var start = Math.Max(0, mobileFallback.Index - 500);
            var length = Math.Min(3500, html.Length - start);
            return (html.Substring(start, length), true);
        }

        if (mobileHeaderFallback is not null)
        {
            return (mobileHeaderFallback, false);
        }

        // A mobile price container exists but is empty: the offer is
        // unavailable. Do not fall back to sponsored carousel prices.
        if (AmazonPatterns.MobilePriceContainer.IsMatch(html))
        {
            return (string.Empty, false);
        }

        // The buybox has no featured offer (only "see all buying options").
        // The page can still contain prices in recommendation carousels; never
        // use those as the product price.
        if (AmazonPatterns.NoFeaturedOffer.IsMatch(html))
        {
            return (string.Empty, false);
        }

        return (marketplace.AllowPriceFallback ? html : string.Empty, false);
    }

    private string? ExtractPrice(string html, bool mobilePriceBlock)
    {
        var whole = AmazonPatterns.PriceWhole.Match(html);
        var fraction = AmazonPatterns.PriceFraction.Match(html);
        if (whole.Success && fraction.Success)
        {
            return marketplace.Format.Price(whole.Groups[1].Value, fraction.Groups[1].Value);
        }

        if (mobilePriceBlock)
        {
            // The mobile main price does not always have an a-price-fraction
            // (for example Japanese integer yen), so prefer its whole price
            // over the generic single-price fallback.
            var mobileWhole = AmazonPatterns.PriceWhole.Match(html);
            if (mobileWhole.Success)
            {
                return marketplace.Format.SinglePrice(mobileWhole.Groups[1].Value);
            }
        }

        var single = marketplace.Patterns.DetailPriceSingle.Match(html);
        if (single.Success)
        {
            return marketplace.Format.SinglePrice(single.Groups[1].Value);
        }

        var wholeOnly = AmazonPatterns.PriceWhole.Match(html);
        return wholeOnly.Success ? marketplace.Format.SinglePrice(wholeOnly.Groups[1].Value) : null;
    }

    private static bool IsMobileHeaderPriceBlock(string block) =>
        Regex.IsMatch(
            block,
            @"class=""(?=[^""]*\bpriceToPay\b)(?=[^""]*\breinventMobileHeaderPrice\b)[^""]*""",
            RegexOptions.IgnoreCase);

    private static readonly Regex ModelNameKey = new(
        @"^\s*(?:model(?:\s*(?:name|number|no\.?))?|modell(?:name|nummer|bezeichnung)|nome\s*(?:del\s*)?modello|nom\s*(?:du\s*)?mod[eè]le|nombre\s*(?:del\s*)?modelo|型番|モデル名)\s*:?\s*$",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);

    private static string? ExtractModelName(
        IReadOnlyList<ProductDetailExtra> amazonDetails,
        IReadOnlyDictionary<string, string> specs)
    {
        foreach (var item in amazonDetails)
        {
            if (ModelNameKey.IsMatch(item.Name.Trim())
                && !string.IsNullOrWhiteSpace(item.Value))
            {
                return item.Value.Trim();
            }
        }

        foreach (var (key, value) in specs)
        {
            if (ModelNameKey.IsMatch(key.Trim())
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static ChargerInfo? ExtractCharger(
        string html,
        IReadOnlyList<string> description,
        IReadOnlyDictionary<string, string> specs,
        IReadOnlyList<ProductDetailExtra> amazonDetails)
    {
        bool? included = null;
        var input = AmazonPatterns.ChargerInput.Match(html);
        if (input.Success)
        {
            if (input.Value.Contains("value=\"false\"", StringComparison.OrdinalIgnoreCase))
            {
                included = false;
            }
            else if (input.Value.Contains("value=\"true\"", StringComparison.OrdinalIgnoreCase))
            {
                included = true;
            }
        }

        var label = string.Empty;
        var labelMatch = AmazonPatterns.ChargerLabel.Match(html);
        if (labelMatch.Success)
        {
            label = HtmlText.Clean(labelMatch.Groups[1].Value);
        }

        var note = description.FirstOrDefault(ContainsChargerKeyword);
        var boxContents = FindBoxContents(specs, amazonDetails);

        if (included is null && note is not null)
        {
            if (ContainsNotIncluded(note))
            {
                included = false;
            }
            else if (ContainsIncluded(note))
            {
                included = true;
            }
        }
        if (included is null && label.Length > 0)
        {
            if (ContainsNotIncluded(label))
            {
                included = false;
            }
            else if (ContainsIncluded(label))
            {
                included = true;
            }
        }

        if (included is null && label.Length == 0 && note is null)
        {
            return null;
        }

        return new ChargerInfo
        {
            Included = included,
            Label = string.IsNullOrWhiteSpace(label) ? null : label,
            Note = note,
            BoxContents = boxContents,
        };
    }

    private static bool ContainsChargerKeyword(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("caricatore")
            || lower.Contains("caricabatterie")
            || lower.Contains("carica batterie")
            || lower.Contains("alimentatore")
            || lower.Contains("charger")
            || lower.Contains("power adapter")
            || lower.Contains("netzteil")
            || lower.Contains("ladegerät")
            || lower.Contains("ladegeraet")
            || lower.Contains("chargeur")
            || lower.Contains("cargador")
            || lower.Contains("充電器")
            || lower.Contains("電源アダプター")
            || lower.Contains("acアダプター");
    }

    private static bool ContainsNotIncluded(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("non incluso")
            || lower.Contains("non inclusa")
            || lower.Contains("non è incluso")
            || lower.Contains("non e incluso")
            || lower.Contains("non è compreso")
            || lower.Contains("non è compresa")
            || lower.Contains("non sono inclusi")
            || lower.Contains("non compreso")
            || lower.Contains("not included")
            || lower.Contains("not supplied")
            || lower.Contains("nicht enthalten")
            || lower.Contains("nicht inklusive")
            || lower.Contains("non fourni")
            || lower.Contains("n'est pas inclus")
            || lower.Contains("non inclus")
            || lower.Contains("no incluido")
            || lower.Contains("no incluye")
            || lower.Contains("no está incluido")
            || lower.Contains("no esta incluido")
            || lower.Contains("付属していません")
            || lower.Contains("付属しません")
            || lower.Contains("含まれません")
            || lower.Contains("同梱されていません")
            || lower.Contains("別売");
    }

    private static bool ContainsIncluded(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("incluso")
            || lower.Contains("inclusa")
            || lower.Contains("included")
            || lower.Contains("enthalten")
            || lower.Contains("fourni")
            || lower.Contains("incluido")
            || lower.Contains("incluye")
            || lower.Contains("付属");
    }

    private static string? FindBoxContents(
        IReadOnlyDictionary<string, string> specs,
        IReadOnlyList<ProductDetailExtra> amazonDetails)
    {
        foreach (var (key, value) in specs)
        {
            if (IsBoxContentsKey(key) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        foreach (var item in amazonDetails)
        {
            if (IsBoxContentsKey(item.Name) && !string.IsNullOrWhiteSpace(item.Value))
            {
                return item.Value;
            }
        }
        return null;
    }

    private static bool IsBoxContentsKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("contenuto confezione")
            || lower.Contains("contenuto della confezione")
            || lower.Contains("included components")
            || lower.Contains("box contents")
            || lower.Contains("what's in the box")
            || lower.Contains("what is in the box")
            || lower.Contains("lieferumfang")
            || lower.Contains("contenu du coffret")
            || lower.Contains("contenu de la boîte")
            || lower.Contains("contenu de la boite")
            || lower.Contains("contenido de la caja")
            || lower.Contains("同梱内容")
            || lower.Contains("パッケージ内容")
            || lower.Contains("付属品");
    }

    private static DeliveryInfo? ExtractDelivery(string html)
    {
        var options = new List<DeliveryOption>();
        var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match span in AmazonPatterns.DeliverySpan.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match attribute in AmazonPatterns.DeliveryAttr.Matches(span.Groups[1].Value))
            {
                attributes[attribute.Groups[1].Value] = attribute.Groups[2].Value;
            }

            attributes.TryGetValue("type", out var type);
            attributes.TryGetValue("price", out var price);
            attributes.TryGetValue("time", out var time);
            attributes.TryGetValue("cutoff", out var cutoff);

            type = HtmlText.Clean(type ?? string.Empty);
            price = HtmlText.Clean(price ?? string.Empty);
            time = HtmlText.Clean(time ?? string.Empty);
            cutoff = HtmlText.Clean(cutoff ?? string.Empty);

            if (string.IsNullOrEmpty(type)
                && string.IsNullOrEmpty(price)
                && string.IsNullOrEmpty(time)
                && string.IsNullOrEmpty(cutoff))
            {
                continue;
            }

            var raw = HtmlText.Clean(string.Join(
                " ",
                new[] { type, price, time, cutoff }.Where(part => !string.IsNullOrEmpty(part))));
            var optionKey = $"{type}|{price}|{time}|{cutoff}";
            if (!seenOptions.Add(optionKey))
            {
                continue;
            }

            options.Add(new DeliveryOption
            {
                Type = type,
                Cost = price,
                Time = time,
                Cutoff = cutoff,
                Raw = raw,
            });
        }

        var fallbackCutoff = options.Any(option => !string.IsNullOrEmpty(option.Cutoff))
            ? string.Empty
            : ExtractDeliveryCutoffFallback(html);

        if (options.Count == 0 && string.IsNullOrEmpty(fallbackCutoff))
        {
            return null;
        }

        var primary = options.FirstOrDefault();
        var cutoffValue = options
            .Select(option => option.Cutoff)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value))
            ?? fallbackCutoff;

        var rawValue = options.Count > 0
            ? string.Join(" ", options.Select(option => option.Raw))
            : fallbackCutoff;

        return new DeliveryInfo
        {
            Location = ExtractDeliveryLocation(html),
            Type = primary?.Type ?? string.Empty,
            Cost = primary?.Cost ?? string.Empty,
            Time = primary?.Time ?? string.Empty,
            Cutoff = cutoffValue,
            Raw = rawValue,
            Options = options.Count > 1 ? options : null,
        };
    }

    private static string ExtractDeliveryCutoffFallback(string html)
    {
        var countdown = AmazonPatterns.DeliveryCountdownFallback.Match(html);
        if (countdown.Success)
        {
            var value = HtmlText.Clean(countdown.Groups[1].Value);
            var before = html[..countdown.Index];
            var prompt = AmazonPatterns.DeliveryCutoffPrompt.Matches(before)
                .Cast<Match>()
                .LastOrDefault();
            return prompt is null
                ? value
                : HtmlText.Clean($"{prompt.Groups[1].Value} {value}");
        }

        var fallback = AmazonPatterns.DeliveryCutoffPrompt.Match(html);
        return fallback.Success
            ? HtmlText.Clean(fallback.Value)
            : string.Empty;
    }

    private static string ExtractDeliveryLocation(string html)
    {
        var single = AmazonPatterns.DeliveryLocationSingle.Match(html);
        if (single.Success)
        {
            var value = CleanDeliveryLocation(
                HtmlText.Clean(single.Groups[1].Value));
            if (value.Length > 0)
            {
                return value;
            }
        }

        var line1 = AmazonPatterns.DeliveryLocationLine1.Match(html);
        if (line1.Success)
        {
            var text = HtmlText.Clean(line1.Groups[1].Value);
            var line2 = AmazonPatterns.DeliveryLocationLine2.Match(html);
            if (line2.Success)
            {
                var second = CleanDeliveryLocation(
                    HtmlText.Clean(line2.Groups[1].Value));
                if (second.Length > 0
                    && !IsLocationAction(second)
                    && !text.Contains(second, StringComparison.OrdinalIgnoreCase))
                {
                    text += " " + second;
                }
            }
            var value = CleanDeliveryLocation(text);
            if (value.Length > 0)
            {
                return value;
            }
        }

        var contextual = AmazonPatterns.DeliveryLocationContextual.Match(html);
        return contextual.Success
            ? CleanDeliveryLocation(HtmlText.Clean(contextual.Groups[1].Value))
            : string.Empty;
    }

    private static string CleanDeliveryLocation(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        foreach (var prefix in new[]
                 {
                     "Il tuo indirizzo di consegna:",
                     "Il tuo indirizzo  di consegna:",
                     "In consegna a",
                     "Consegna a",
                     "Enviar a",
                     "Enviar a:",
                     "Livrer à",
                     "Livrer a",
                     "Deliver to",
                     "Delivering to",
                     "Lieferung nach",
                     "お届け先",
                 })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        text = LocationActionSuffixRegex().Replace(text, string.Empty).Trim();
        if (IsLocationAction(text))
        {
            return string.Empty;
        }

        return text.Trim(' ', ':', '⌵', '\u200c', '\u00a0');
    }

    private static bool IsLocationAction(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return LocationActionRegex().IsMatch(normalized);
    }

    [GeneratedRegex(
        @"(?:aggiorna(?:\s+(?:posizione|luogo|località))?|update(?:\s+location)?|" +
        @"standort\s+aktualisieren|lieferort\s+ändern|actualizar(?:\s+ubicación)?|" +
        @"cambiar(?:\s+ubicación)?|mettre\s+à\s+jour(?:\s+l['’]emplacement)?|" +
        @"modifier\s+l['’]emplacement|changer\s+de\s+lieu|変更|更新|場所を更新|お届け先を変更)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationActionRegex();

    [GeneratedRegex(
        @"(?:\s*[:·–-]\s*)?(?:aggiorna(?:\s+(?:posizione|luogo|località))?|" +
        @"update(?:\s+location)?|standort\s+aktualisieren|lieferort\s+ändern|" +
        @"actualizar(?:\s+ubicación)?|cambiar(?:\s+ubicación)?|" +
        @"mettre\s+à\s+jour(?:\s+l['’]emplacement)?|modifier\s+l['’]emplacement|" +
        @"changer\s+de\s+lieu|更新|場所を更新|お届け先を変更)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationActionSuffixRegex();

    [GeneratedRegex(
        @"<div[^>]*class=""[^""]*dp-title-differentiators[^""]*""[^>]*>[\s\S]*?<span[^>]*>(?<subtitle>[\s\S]*?)</span>",
        RegexOptions.IgnoreCase)]
    private static partial Regex TitleSubtitleRegex();

    private static string Deduplicate(string text)
    {
        var half = text.Length / 2;
        if (half > 0 && text[..half].Trim() == text[half..].Trim())
        {
            text = text[..half].Trim();
        }

        return text.TrimEnd('.') + ".";
    }

    private static bool IsHigherThanCurrent(
        string original,
        string current,
        IMarketplaceFormat format)
    {
        return format.TryParsePrice(original, out var originalAmount)
            && format.TryParsePrice(current, out var currentAmount)
            && originalAmount > currentAmount;
    }

    private static bool TryComputeDiscount(
        string current,
        string original,
        IMarketplaceFormat format,
        out string discount)
    {
        discount = string.Empty;
        if (string.IsNullOrEmpty(current)
            || string.IsNullOrEmpty(original)
            || !format.TryParsePrice(current, out var currentAmount)
            || !format.TryParsePrice(original, out var originalAmount)
            || originalAmount <= currentAmount)
        {
            return false;
        }

        var percentage = (int)Math.Round(
            (1 - (currentAmount / originalAmount)) * 100,
            MidpointRounding.AwayFromZero);
        if (percentage <= 0)
        {
            return false;
        }

        discount = $"-{percentage}%";
        return true;
    }

    private sealed record DealInfo(
        string? Label,
        string? Id,
        string? EndsText,
        string? EndsOn,
        int? EndsInDays,
        bool? HasCountdown);

    private static DealInfo ExtractDealInfo(string html)
    {
        var blockMatch = AmazonPatterns.DealBadgeBlock.Match(html);
        var block = blockMatch.Success
            ? HtmlBlockExtractor.ExtractDiv(html, blockMatch.Index)
            : string.Empty;
        var badgeArea = string.IsNullOrEmpty(block) ? html : block;

        var badgeMatch = AmazonPatterns.DealBadgeText.Match(badgeArea);
        var label = badgeMatch.Success
            ? HtmlText.Clean(badgeMatch.Groups[1].Value)
            : null;
        if (string.IsNullOrEmpty(label) && badgeMatch.Success)
        {
            label = "Deal";
        }

        // Scope the deal id to the deal block. A product page also contains
        // recommendation carousels with their own amzn1.deal ids.
        string? id = null;
        if (!string.IsNullOrEmpty(block))
        {
            var idMatch = AmazonPatterns.DealId.Match(block);
            if (idMatch.Success)
            {
                id = idMatch.Value;
            }
        }

        if (string.IsNullOrEmpty(label) && string.IsNullOrEmpty(id))
        {
            return new DealInfo(null, null, null, null, null, null);
        }

        if (string.IsNullOrEmpty(label))
        {
            label = "Deal";
        }

        string? endsText = null;
        if (!string.IsNullOrEmpty(block))
        {
            var popover = AmazonPatterns.DealTermsPopover.Match(block);
            if (popover.Success)
            {
                endsText = ExtractDealTermsHeader(popover.Groups[1].Value);
            }
        }

        var endsOn = DealEndDateParser.Parse(endsText);
        var endsInDays = endsOn is null
            ? (int?)null
            : Math.Max(
                0,
                endsOn.Value.DayNumber
                - DateOnly.FromDateTime(DateTime.Now).DayNumber);

        bool? hasCountdown = null;
        if (!string.IsNullOrEmpty(block))
        {
            hasCountdown = AmazonPatterns.DealCountdown.IsMatch(block);
        }
        else if (label is not null)
        {
            hasCountdown = AmazonPatterns.DealCountdown.IsMatch(html);
        }

        return new DealInfo(
            label,
            id,
            endsText,
            endsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            endsInDays,
            hasCountdown);
    }

    private static string? ExtractDealTermsHeader(string escapedPopover)
    {
        try
        {
            var json = WebUtility.HtmlDecode(escapedPopover);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("header", out var header))
            {
                var text = HtmlText.Clean(header.GetString() ?? string.Empty);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw popover text when Amazon changes the shape.
        }

        var decoded = WebUtility.HtmlDecode(escapedPopover);
        var headerMatch = Regex.Match(
            decoded,
            "\"header\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        if (headerMatch.Success)
        {
            var text = HtmlText.Clean(headerMatch.Groups[1].Value);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }
}
