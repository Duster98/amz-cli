using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Structural patterns shared by all Amazon marketplaces. Localized text
/// patterns live in <see cref="Marketplaces.IMarketplacePatterns"/>.
/// </summary>
public static partial class AmazonPatterns
{
    public static readonly string[] BlockMarkers =
    [
        "/_sec/verify",
        "/errors/validateCaptcha",
        "captchacharacters",
        "api-services-support@amazon.com",
    ];

    public static readonly string[] PriceBlockIds =
    [
        "corePriceDisplay_desktop_feature_div",
        "corePrice_desktop",
        "corePrice_feature_div",
        "apex_desktop",
    ];

    public static Regex Asin => AsinRegex();
    public static Regex SearchTitle => SearchTitleRegex();
    public static Regex SearchImage => SearchImageRegex();
    public static Regex SearchImageFallback => SearchImageFallbackRegex();
    public static Regex SearchPriceWholeFraction => SearchPriceWholeFractionRegex();
    public static Regex SearchOriginalPrice => SearchOriginalPriceRegex();
    public static Regex SearchOriginalPriceFallback => SearchOriginalPriceFallbackRegex();
    public static Regex DetailOriginalPriceFallback => DetailOriginalPriceFallbackRegex();
    public static Regex SearchAltTitle => SearchAltTitleRegex();
    public static Regex SearchCoupon => SearchCouponRegex();
    public static Regex SearchDeliveryMessage => SearchDeliveryMessageRegex();
    public static Regex SearchDeliveryCountry => SearchDeliveryCountryRegex();

    public static Regex DetailTitle => DetailTitleRegex();
    public static Regex DetailTitleFallback => DetailTitleFallbackRegex();
    public static Regex BrandPrimary => BrandPrimaryRegex();
    public static Regex BrandSecondary => BrandSecondaryRegex();
    public static Regex BrandFallback => BrandFallbackRegex();
    public static Regex BrandLogoFallback => BrandLogoFallbackRegex();
    public static Regex BrandStoreFallback => BrandStoreFallbackRegex();
    public static Regex Byline => BylineRegex();
    public static Regex PriceWhole => PriceWholeRegex();
    public static Regex PriceFraction => PriceFractionRegex();
    public static Regex NoFeaturedOffer => NoFeaturedOfferRegex();
    public static Regex MobilePrice => MobilePriceRegex();
    public static Regex MobilePriceContainer => MobilePriceContainerRegex();
    public static Regex Discount => DiscountRegex();
    public static Regex DetailCouponPercent => DetailCouponPercentRegex();
    public static Regex DealBadgeText => DealBadgeTextRegex();
    public static Regex DealBadgeBlock => DealBadgeBlockRegex();
    public static Regex DealId => DealIdRegex();
    public static Regex DealTermsPopover => DealTermsPopoverRegex();
    public static Regex DealCountdown => DealCountdownRegex();
    public static Regex SearchDealBadge => SearchDealBadgeRegex();
    public static Regex FeaturesSection => FeaturesSectionRegex();
    public static Regex Feature => FeatureRegex();
    public static Regex SellerFallback => SellerFallbackRegex();
    public static Regex Availability => AvailabilityRegex();
    public static Regex DeliveryPrimary => DeliveryPrimaryRegex();
    public static Regex DeliveryAttr => DeliveryAttrRegex();
    public static Regex DeliverySpan => DeliverySpanRegex();
    public static Regex DeliveryCountdownFallback => DeliveryCountdownFallbackRegex();
    public static Regex DeliveryCutoffPrompt => DeliveryCutoffPromptRegex();
    public static Regex DeliveryLocationSingle => DeliveryLocationSingleRegex();
    public static Regex DeliveryLocationLine1 => DeliveryLocationLine1Regex();
    public static Regex DeliveryLocationLine2 => DeliveryLocationLine2Regex();
    public static Regex DeliveryLocationContextual => DeliveryLocationContextualRegex();
    public static Regex GlowToken => GlowTokenRegex();
    public static Regex GlowCsrfToken => GlowCsrfTokenRegex();
    public static Regex ReturnsFallback => ReturnsFallbackRegex();
    public static Regex WarrantyReturns => WarrantyReturnsRegex();
    public static Regex GalleryStart => GalleryStartRegex();
    public static Regex FeatureBullets => FeatureBulletsRegex();
    public static Regex Bullet => BulletRegex();
    public static Regex ChargerInput => ChargerInputRegex();
    public static Regex ChargerLabel => ChargerLabelRegex();
    public static Regex SpecRow => SpecRowRegex();
    public static Regex SpecRowLoose => SpecRowLooseRegex();
    public static Regex FrequentlyReturned => FrequentlyReturnedRegex();
    public static Regex FrequentlyReturnedModern => FrequentlyReturnedModernRegex();
    public static Regex DetailReviewsFallback => DetailReviewsFallbackRegex();
    public static Regex DetailImageFallback => DetailImageFallbackRegex();
    public static Regex DetailHiResFallback => DetailHiResFallbackRegex();
    public static Regex AodOfferBlock => AodOfferBlockRegex();
    public static Regex AodOfferListingId => AodOfferListingIdRegex();
    public static Regex AodOfferHeading => AodOfferHeadingRegex();
    public static Regex AodPriceLabel => AodPriceLabelRegex();
    public static Regex AodIngressAnchor => AodIngressAnchorRegex();
    public static Regex OfferListingAnchor => OfferListingAnchorRegex();
    public static Regex FeaturedOfferInput => FeaturedOfferInputRegex();
    public static Regex FeaturedMerchantInput => FeaturedMerchantInputRegex();
    public static Regex HtmlValueAttribute => HtmlValueAttributeRegex();
    public static Regex SellerProfileAnchor => SellerProfileAnchorRegex();
    public static Regex HrefAttribute => HrefAttributeRegex();
    public static Regex FeaturedCondition => FeaturedConditionRegex();
    public static Regex BuyingOptionType => BuyingOptionTypeRegex();
    public static Regex BuyingOptionsLink => BuyingOptionsLinkRegex();
    public static Regex UsedOnlyBuybox => UsedOnlyBuyboxRegex();
    public static Regex UsedOfferInput => UsedOfferInputRegex();
    public static Regex UsedMerchantInput => UsedMerchantInputRegex();

    [GeneratedRegex(@"data-asin=""([A-Z0-9]{10})""")]
    private static partial Regex AsinRegex();

    [GeneratedRegex(@"<h2[^>]*>(.*?)</h2>", RegexOptions.Singleline)]
    private static partial Regex SearchTitleRegex();

    [GeneratedRegex(@"<img(?=[^>]*class=""[^""]*\bs-image\b)(?=[^>]*src=""(https://m\.media-amazon\.com/images/[^""]+\.(?:jpg|jpeg|png|webp))"")[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex SearchImageRegex();

    [GeneratedRegex(@"<img[^>]*src=""(https://m\.media-amazon\.com/images/I/[^""]+\.(?:jpg|jpeg|png|webp))""", RegexOptions.IgnoreCase)]
    private static partial Regex SearchImageFallbackRegex();

    [GeneratedRegex(@"a-price-whole[^>]*>([\d.,]+)(?:<[^>]*>[^<]*)*<span[^>]*a-price-fraction[^>]*>(\d+)")]
    private static partial Regex SearchPriceWholeFractionRegex();

    [GeneratedRegex(@"<span(?=[^>]*class=""[^""]*a-text-price)(?=[^>]*data-a-strike=""true"")[^>]*>[\s\S]*?<span[^>]*class=""a-offscreen""[^>]*>\s*(?:(?:List(?: Price)?|リスト|参考価格|通常価格|RRP|Prix conseillé)\s*[:：]\s*)?([^<]+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex SearchOriginalPriceRegex();

    [GeneratedRegex(@"<span(?=[^>]*class=""[^""]*a-text-price)(?=[^>]*data-a-strike=""true"")[^>]*>[\s\S]*?<span[^>]*aria-hidden=""true""[^>]*>\s*(?:(?:List(?: Price)?|リスト|参考価格|通常価格|RRP|Prix conseillé|Was)\s*[:：]\s*)?[£€$¥]?\s*([\d.,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchOriginalPriceFallbackRegex();

    [GeneratedRegex(@"<span[^>]*data-a-strike=""true""[^>]*>[\s\S]*?<span[^>]*aria-hidden=""true""[^>]*>\s*(?:Was:|RRP:|List Price:)?\s*[£€$¥]?\s*([\d.,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailOriginalPriceFallbackRegex();

    [GeneratedRegex(@"alt=""([^""]+)""")]
    private static partial Regex SearchAltTitleRegex();

    [GeneratedRegex(@"s-coupon-(?:un)?clipped[^>]*>[\s\S]{0,200}?s-coupon-highlight-color[^>]*>([^<]+)")]
    private static partial Regex SearchCouponRegex();

    [GeneratedRegex(@"<div[^>]*class=""[^""]*udm-primary-delivery-message[^""]*""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex SearchDeliveryMessageRegex();

    [GeneratedRegex(
        @"<span[^>]*class=""[^""]*a-size-small a-color-base[^""]*""[^>]*>([^<]*(?:Delivers to|Ships to|spedisce|Consegna a|Envío a|Enviar a|Livraison|Versand|Lieferung|liefert|発送)[^<]*)</span>",
        RegexOptions.IgnoreCase)]
    private static partial Regex SearchDeliveryCountryRegex();

    [GeneratedRegex(@"product-title-word-break"">\s*([^<]+)")]
    private static partial Regex DetailTitleRegex();

    [GeneratedRegex(@"id=""title""[^>]*>\s*([^<]+)")]
    private static partial Regex DetailTitleFallbackRegex();

    [GeneratedRegex(@"po-brand[^>]*role=""listitem"">[\s\S]*?po-break-word"">([^<]+)")]
    private static partial Regex BrandPrimaryRegex();

    [GeneratedRegex(@"ProductGroupTileBaseBrandTileWrapper[\s\S]*?po-break-word"">([^<]+)")]
    private static partial Regex BrandSecondaryRegex();

    [GeneratedRegex(@"id=""brandLogoBylineLinkMobile""[\s\S]{0,800}?<img[^>]*alt=""([^""]+)""")]
    private static partial Regex BrandFallbackRegex();

    [GeneratedRegex(@"id=""brandLogoBylineLink""[\s\S]{0,800}?<img[^>]*alt=""([^""]+)""")]
    private static partial Regex BrandLogoFallbackRegex();

    [GeneratedRegex(@"amznStoresBylineLogoTextContainer[\s\S]{0,1200}?<a[^>]*class=""[^""]*a-text-bold[^""]*""[^>]*>([^<]+)")]
    private static partial Regex BrandStoreFallbackRegex();

    [GeneratedRegex(@"<a[^>]*\sid=""bylineInfo""[^>]*>([^<]+)")]
    private static partial Regex BylineRegex();

    [GeneratedRegex(@"a-price-whole[^>]*>([\d.,]+)")]
    private static partial Regex PriceWholeRegex();

    [GeneratedRegex(@"a-price-fraction[^>]*>(\d+)")]
    private static partial Regex PriceFractionRegex();

    // No featured offer: the buybox only links to the offer list. The page
    // still contains prices in recommendation carousels, which must not be
    // used as the product price.
    [GeneratedRegex(
        @"buybox-see-all-buying-choices|unqualifiedBuyBox_feature_div|seeAllBuyingOptionsSmallFont|dp_olp_unknown_mbc|unqualified-aodingress-summary-enabled|id=""outOfStock""|outOfStock_feature_div",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoFeaturedOfferRegex();

    // Main price on mobile detail pages. Sponsored carousels use
    // `apex-price-to-pay-value` (with hyphens) instead, so `priceToPay` is a
    // reliable discriminator for the product's own price.
    [GeneratedRegex(@"<[a-z0-9]+[^>]*class=""[^""]*\bpriceToPay\b[^""]*""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MobilePriceRegex();

    [GeneratedRegex(@"id=""corePrice_mobile_feature_div""", RegexOptions.IgnoreCase)]
    private static partial Regex MobilePriceContainerRegex();

    // Some locales render "-72 %" with a space before the percent sign.
    [GeneratedRegex(@"savingsPercentage[^>]*>\s*-\s*(\d+)\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex DiscountRegex();

    // Cross-locale coupon percentage fallback. Scoped to the coupon message
    // blocks so descriptions/CSS ("80% of noise", "#ff9900") are ignored.
    [GeneratedRegex(
        @"(?:class=""[^""]*\bcouponLabelText\b[^""]*""|id=""pqv-price-coupon-message"")[\s\S]{0,400}?(\d+)(?:&nbsp;|\s)*%",
        RegexOptions.IgnoreCase)]
    private static partial Regex DetailCouponPercentRegex();

    // Deal badge supporting text, e.g. "Black Friday anticipado" or
    // "Limited time deal".
    [GeneratedRegex(
        @"id=""dealBadgeSupportingText""[^>]*>([\s\S]{0,400}?)</span>\s*</span>",
        RegexOptions.IgnoreCase)]
    private static partial Regex DealBadgeTextRegex();

    [GeneratedRegex(@"id=""dealBadge_feature_div""", RegexOptions.IgnoreCase)]
    private static partial Regex DealBadgeBlockRegex();

    [GeneratedRegex(@"amzn1\.deal\.[A-Za-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex DealIdRegex();

    // The popover attribute contains HTML-escaped JSON with a
    // "dealTermsMessage" name and a localized end-date header.
    [GeneratedRegex(
        @"data-a-popover=""([^""]*dealTermsMessage[^""]*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex DealTermsPopoverRegex();

    [GeneratedRegex(@"id=""deals_countdown_timer_", RegexOptions.IgnoreCase)]
    private static partial Regex DealCountdownRegex();

    // Search result deal badge: a-data-badge-type="deal" followed by the
    // visible a-badge-text span.
    [GeneratedRegex(
        @"data-a-badge-type=""deal""[\s\S]{0,1500}?<span[^>]*class=""[^""]*\ba-badge-text\b[^""]*""[^>]*>([\s\S]*?)</span>",
        RegexOptions.IgnoreCase)]
    private static partial Regex SearchDealBadgeRegex();

    [GeneratedRegex(@"offer-display-features-container[^>]*>[\s\S]{0,15000}")]
    private static partial Regex FeaturesSectionRegex();

    [GeneratedRegex(@"offer-display-feature-label[^>]*>([\s\S]*?)</div>\s*<div[^>]*class=""[^""]*offer-display-feature-text[^""]*""[^>]*>([\s\S]*?)</div>")]
    private static partial Regex FeatureRegex();

    [GeneratedRegex(@"\sid\s*=\s*['""]sellerProfileTriggerId['""][^>]*>([^<]+)")]
    private static partial Regex SellerFallbackRegex();

    [GeneratedRegex(@"primary-availability-message[^>]*>\s*([^<]+)")]
    private static partial Regex AvailabilityRegex();

    [GeneratedRegex(@"id=""mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE""[^>]*>\s*<span([^>]*)>")]
    private static partial Regex DeliveryPrimaryRegex();

    [GeneratedRegex(@"data-csa-c-delivery-(type|price|time|cutoff|destination|origin-country)=""([^""]*)""")]
    private static partial Regex DeliveryAttrRegex();

    [GeneratedRegex(@"<span([^>]*data-csa-c-delivery-[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex DeliverySpanRegex();

    [GeneratedRegex(@"(?:id=""ftCountdown""|class=""[^""]*ftCountdownClass[^""]*"")[^>]*>([^<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryCountdownFallbackRegex();

    [GeneratedRegex(@"(?i)(order within|ordina entro|commandez(?:\s+sous|\s+dans|\s+avant)?|bestellen sie innerhalb|pide(?:lo)? antes de|entrega en|注文期限|あと\s*[0-9０-９]+\s*(?:時間|分)|[0-9０-９]+\s*分以内にご注文)[^<]{0,90}")]
    private static partial Regex DeliveryCutoffPromptRegex();

    [GeneratedRegex(@"id=""glow-ingress-single-line""[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryLocationSingleRegex();

    [GeneratedRegex(@"id=""glow-ingress-line1""[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryLocationLine1Regex();

    [GeneratedRegex(@"id=""glow-ingress-line2""[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryLocationLine2Regex();

    [GeneratedRegex(@"id=""contextualIngressPtLabel_deliveryShortLine""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryLocationContextualRegex();

    [GeneratedRegex(@"id=""glowValidationToken""[^>]*value=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex GlowTokenRegex();

    [GeneratedRegex(@"CSRF_TOKEN\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex GlowCsrfTokenRegex();

    [GeneratedRegex(@"creturns-policy-anchor-text[^>]*>\s*([^<]+)")]
    private static partial Regex ReturnsFallbackRegex();

    // The Japanese detail page puts the return policy in the
    // "保証とサポート" table instead of the offer feature block.
    [GeneratedRegex(
        @"id=""productDetails_warranty_support_sections""[\s\S]{0,4000}?<span>\s*(?:返品|Returns?)\s*</span>\s*:\s*<span[^>]*class=""[^""]*a-letter-space[^""]*""[^>]*>\s*</span>\s*<span[^>]*>([\s\S]*?)</span>",
        RegexOptions.IgnoreCase)]
    private static partial Regex WarrantyReturnsRegex();

    [GeneratedRegex("""['"]colorImages['"]\s*:\s*\{\s*['"]initial['"]\s*:\s*(?:A\.\$\.parseJSON\(\s*')?\[""")]
    private static partial Regex GalleryStartRegex();

    [GeneratedRegex(@"id=""feature-bullets""[\s\S]{0,20000}")]
    private static partial Regex FeatureBulletsRegex();

    [GeneratedRegex(@"<span[^>]*class=""a-list-item""[^>]*>([\s\S]*?)</span>")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"<input[^>]*id=""buffet-red-atf-is-charger-included""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ChargerInputRegex();

    [GeneratedRegex(@"id=""buffet-red-atf-content""[\s\S]*?<a[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex ChargerLabelRegex();

    [GeneratedRegex(@"<th[^>]*class=""[^""]*prodDetSectionEntry[^""]*""[^>]*>((?:(?!</th>)[\s\S])*?)</th>\s*<td[^>]*class=""[^""]*prodDetAttrValue[^""]*""[^>]*>([\s\S]*?)</td>")]
    private static partial Regex SpecRowRegex();

    // Secondary product-detail tables can use a plain <td> without the
    // prodDetAttrValue class (for example the sales-rank block).
    [GeneratedRegex(@"<th[^>]*class=""[^""]*prodDetSectionEntry[^""]*""[^>]*>((?:(?!</th>)[\s\S])*?)</th>\s*<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase)]
    private static partial Regex SpecRowLooseRegex();

    [GeneratedRegex(@"id=""[^""]*nostos_badge[^""]*""[\s\S]{0,500}?hrrv-badge-(T[0-9])[\s\S]{0,200}?a-text-bold[^>]*>([^<]+)")]
    private static partial Regex FrequentlyReturnedRegex();

    [GeneratedRegex(@"<h2[^>]*class=""[^""]*ripers-hrr-badge-title[^""]*""[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase)]
    private static partial Regex FrequentlyReturnedModernRegex();

    [GeneratedRegex(@"aria-label=""([\d.,]+)\s+(?:Reviews|ratings|レーティング|評価|レビュー)""")]
    private static partial Regex DetailReviewsFallbackRegex();

    [GeneratedRegex(@"""landingImageUrl"":""([^""]+)""")]
    private static partial Regex DetailImageFallbackRegex();

    [GeneratedRegex(@"""hiRes"":""([^""]+)""")]
    private static partial Regex DetailHiResFallbackRegex();

    // All offers display (AOD): one block per buying option.
    [GeneratedRegex(@"<div[^>]*\bid=""aod-offer""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AodOfferBlockRegex();

    [GeneratedRegex(
        @"name=""items\[0\.base\]\[offerListingId\]""\s+value=""([^""]*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex AodOfferListingIdRegex();

    [GeneratedRegex(
        @"id=""aod-offer-heading""[^>]*>([\s\S]*?)</div>",
        RegexOptions.IgnoreCase)]
    private static partial Regex AodOfferHeadingRegex();

    [GeneratedRegex(
        @"apex-pricetopay-accessibility-label[^>]*>\s*([^<]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AodPriceLabelRegex();

    // Product page summary anchor. The desktop buybox uses aod-ingress-link
    // ("Nuovo (7) da"); unqualified/mobile buyboxes use the summary-enabled
    // content id ("7 opzioni a partire da").
    [GeneratedRegex(
        @"<a[^>]*(?:id=""aod-ingress-link""|data-csa-c-content-id=""unqualified-aodingress-summary-enabled"")[^>]*>([\s\S]*?)</a>",
        RegexOptions.IgnoreCase)]
    private static partial Regex AodIngressAnchorRegex();

    // Mobile/table variant: the offer summary is a touch link to the
    // offer-listing page (dp_olp_ALL_mbc) instead of aod-ingress-link.
    [GeneratedRegex(
        @"<a[^>]*\bhref=""[^""]*gp/offer-listing/[^""]*""[^>]*>([\s\S]*?)</a>",
        RegexOptions.IgnoreCase)]
    private static partial Regex OfferListingAnchorRegex();

    // Featured buybox offer: the product page carries the same offer listing
    // id used by the add-to-cart form and the merchant id. AOD lists the
    // *other* offers when a featured offer exists, so this fills the gap.
    [GeneratedRegex(@"<input[^>]*\bid=""offerListingID""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedOfferInputRegex();

    [GeneratedRegex(@"<input[^>]*\bid=""merchantID""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedMerchantInputRegex();

    [GeneratedRegex(@"\bvalue=[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlValueAttributeRegex();

    [GeneratedRegex(
        @"<a[^>]*\bid=['""]sellerProfileTriggerId['""][^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex SellerProfileAnchorRegex();

    [GeneratedRegex(@"\bhref=['""]([^'""]*)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex HrefAttributeRegex();

    // Buybox condition from the active new/used accordion row. Needed when
    // the summary is mixed ("新品＆中古品 (50)") but the featured offer is new.
    [GeneratedRegex(
        @"data-csa-c-slot-id=""(?:new|used)AccordionRow[^""]*""[^>]*data-csa-c-is-in-initial-active-row=""true""[^>]*>[\s\S]{0,1200}?<span[^>]*class=""[^""]*a-text-bold[^""]*""[^>]*>\s*([^<]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedConditionRegex();

    [GeneratedRegex(
        @"""buyingOptionType""\s*:\s*""(NEW|USED)""|buyingOptionType=(NEW|USED)",
        RegexOptions.IgnoreCase)]
    private static partial Regex BuyingOptionTypeRegex();

    // Used-only buybox on Amazon.fr and similar layouts. The page has no
    // new-offer ingress link, but carries the featured used offer and its
    // condition in this block.
    [GeneratedRegex(
        @"<div[^>]*\bid\s*=\s*[""']usedOnlyBuybox[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex UsedOnlyBuyboxRegex();

    [GeneratedRegex(
        @"<input[^>]*\bid\s*=\s*[""']usedOfferListingID[""'][^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex UsedOfferInputRegex();

    [GeneratedRegex(
        @"<input[^>]*\bid\s*=\s*[""']usedMerchantID[""'][^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex UsedMerchantInputRegex();

    // Any link that opens the full offer list. Used to trigger AOD even when
    // the summary anchor text is not recognized (other locales/AB variants).
    [GeneratedRegex(
        @"aod-ingress-link|unqualified-aodingress-summary-enabled|buybox-see-all-buying-choices|seeAllBuyingOptionsSmallFont|dp_olp_(?:NEW|ALL|unknown)_mbc",
        RegexOptions.IgnoreCase)]
    private static partial Regex BuyingOptionsLinkRegex();
}
