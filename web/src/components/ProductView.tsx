import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
} from 'react'
import type { ProductDetail, ProductResponse, ProductReview } from '../lib/api'
import {
  conditionGrade,
  conditionGradeFromToken,
  conditionGradeTranslationKey,
  conditionLabel,
  conditionTranslationKey,
  conditionTypeTranslationKey,
  formatPrice,
  isUsedCondition,
  parseRating,
} from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import type { Translate, TranslationKey } from '../i18n/types'
import { fulfilmentLabel } from '../lib/fulfilment'
import { Icon, RatingStars, Tag } from './ui'
import { ReviewLightbox, type ReviewImageLightbox } from './ReviewLightbox'
import { ProductGallery } from './ProductGallery'
import { ProductColumns } from './ProductColumns'
import {
  conditionGradeTone,
  OffersDialog,
  OffersPreview,
  PurchasePanel,
} from './ProductPurchase'
import { ReviewDialog, ReviewsSection } from './ProductReviews'
import { BuyBox, Stock, variantLabel } from './BuyBox'

function cacheStatus(
  product: ProductResponse | null,
  t: Translate,
): string {
  if (!product) {
    return ''
  }
  if (product.stale) {
    return t('product.stale')
  }

  const seconds = product.cache_age_seconds ?? 0
  if (seconds < 60) {
    return product.cached
      ? t('product.cachedJustNow')
      : t('product.updatedJustNow')
  }
  if (seconds < 3600) {
    const minutes = Math.floor(seconds / 60)
    return t(
      product.cached ? 'product.cachedMinutes' : 'product.minutesAgo',
      { count: minutes },
    )
  }

  const hours = Math.floor(seconds / 3600)
  return t(
    product.cached ? 'product.cachedHours' : 'product.hoursAgo',
    { count: hours },
  )
}

function ProductTitle({ title }: { title: string }) {
  const { t } = useI18n()
  const [expanded, setExpanded] = useState(false)
  const [canExpand, setCanExpand] = useState(false)
  const titleRef = useRef<HTMLHeadingElement>(null)

  useLayoutEffect(() => {
    setExpanded(false)
  }, [title])

  useLayoutEffect(() => {
    const element = titleRef.current
    if (!element) {
      return
    }
    if (expanded) {
      return
    }
    const measure = () => {
      setCanExpand(element.scrollHeight > element.clientHeight + 1)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [title, expanded])

  return (
    <>
      <h1
        ref={titleRef}
        className={expanded ? 'is-expanded' : 'is-clamped'}
      >
        {title}
      </h1>
      {canExpand || expanded ? (
        <button
          type="button"
          className="text-button title-toggle"
          aria-expanded={expanded}
          onClick={() => setExpanded((value) => !value)}
        >
          {expanded ? t('product.showLess') : t('product.showMore')}
        </button>
      ) : null}
    </>
  )
}

function BestSellerBadge({ detail }: { detail: ProductDetail }) {
  const { t } = useI18n()
  const category = detail.best_seller_category?.trim()
  const rank = detail.best_seller_rank

  if (!rank && !category && !detail.best_seller_label) {
    return null
  }

  const label =
    rank === 1 && category
      ? t('product.bestSellerIn', { category })
      : rank && category
        ? t('product.bestSellerRankIn', { rank, category })
        : t('product.bestSeller')
  const tag = <Tag tone="gold">{label}</Tag>
  const title = detail.best_seller_label ?? category ?? undefined

  return detail.best_seller_url ? (
    <a
      className="best-seller-badge"
      href={detail.best_seller_url}
      target="_blank"
      rel="noreferrer"
      title={title}
    >
      {tag}
    </a>
  ) : (
    <span className="best-seller-badge" title={title}>
      {tag}
    </span>
  )
}

function ProductIdentity({
  detail,
  showFulfilment = false,
  fulfilment = '',
}: {
  detail: ProductDetail
  showFulfilment?: boolean
  fulfilment?: string
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const dealEndsOn = detail.deal_ends_on
    ? new Date(`${detail.deal_ends_on}T00:00:00`).toLocaleDateString(
        languageLocale,
        { day: 'numeric', month: 'long', year: 'numeric' },
      )
    : null
  const dealTitle =
    detail.deal_ends_text?.trim() ||
    (dealEndsOn ? t('product.dealEndsOn', { date: dealEndsOn }) : null) ||
    (detail.deal_has_countdown ? t('product.dealEndsSoon') : null) ||
    detail.deal
  const hasBadges = Boolean(
    detail.amazon_choice ||
      detail.best_seller_label ||
      detail.best_seller_rank ||
      detail.best_seller_category ||
      detail.deal,
  )

  return (
    <div className="product-identity">
      <ProductTitle title={detail.title} />
      {detail.subtitle ? (
        <p className="product-subtitle">{detail.subtitle}</p>
      ) : null}

      <div className="dossier-rating">
        <RatingStars
          value={parseRating(detail.rating)}
          count={detail.reviews}
        />
        {detail.rating_details?.average ? (
          <span className="rating-detail">
            {detail.rating_details.average.toFixed(1)} / 5
            {detail.rating_details.count
              ? ` · ${t('product.ratingsCount', {
                  count: formatNumber(detail.rating_details.count),
                })}`
              : ''}
          </span>
        ) : null}
        {hasBadges ? (
          <span className="dossier-badges">
            {detail.amazon_choice ? (
              <span className="dossier-choice">
                <Tag tone="gold">
                  <span title={detail.amazon_choice_for ?? undefined}>
                    {detail.amazon_choice_text ?? t('results.amazonChoice')}
                  </span>
                </Tag>
              </span>
            ) : null}
            <BestSellerBadge detail={detail} />
            {detail.deal ? (
              <span className="dossier-deal" title={dealTitle ?? undefined}>
                <Tag tone="accent">{detail.deal}</Tag>
              </span>
            ) : null}
          </span>
        ) : null}
      </div>

      {showFulfilment && fulfilment ? (
        <p className="product-mobile-fulfilment">
          <Icon name="package" />
          <span>{fulfilment}</span>
        </p>
      ) : null}
    </div>
  )
}

function SafetyWarnings({ warnings }: { warnings: string[] }) {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  if (warnings.length === 0) {
    return null
  }
  const visible = open ? warnings : warnings.slice(0, 1)

  return (
    <section className="detail-section safety-warnings-section">
      <div className="notice notice-warning safety-notice" role="note">
        <Icon name="alert" />
        <div className="safety-notice-copy">
          <div className="safety-notice-head">
            <strong>{t('product.safetyWarnings')}</strong>
            {warnings.length > 1 ? (
              <button
                type="button"
                className="text-button"
                aria-expanded={open}
                onClick={() => setOpen((value) => !value)}
              >
                {open
                  ? t('product.showLess')
                  : t('product.showAllWarnings', {
                      count: warnings.length,
                    })}
              </button>
            ) : null}
          </div>
          <ul className="safety-warning-list">
            {visible.map((warning, index) => (
              <li key={`${warning}-${index}`}>{warning}</li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  )
}

function PriceBlock({
  detail,
  displayPrice,
  priceIsFrom,
  condition,
  conditionTone,
  gradeKey,
  gradeTone,
  t,
}: {
  detail: ProductDetail
  displayPrice: string
  priceIsFrom: boolean
  condition: string | null
  conditionTone: 'rust' | 'neutral'
  gradeKey: TranslationKey | null
  gradeTone: 'gold' | 'blue' | 'neutral' | 'rust'
  t: Translate
}) {
  return (
    <>
      <div className="purchase-price">
        <span className="price-line">
          {priceIsFrom ? (
            <span className="price-from">{t('product.from')}</span>
          ) : null}
          <span className="price price-large">{displayPrice}</span>
        </span>
        {detail.original_price ||
        detail.discount ||
        condition ||
        gradeKey ||
        detail.prime?.only ? (
          <span className="price-meta">
            {detail.original_price ? (
              <span className="price-original">{detail.original_price}</span>
            ) : null}
            {detail.discount ? <Tag tone="blue">{detail.discount}</Tag> : null}
            {condition ? (
              <Tag tone={conditionTone}>{condition}</Tag>
            ) : null}
            {gradeKey ? <Tag tone={gradeTone}>{t(gradeKey)}</Tag> : null}
            {detail.prime?.only ? (
              <Tag tone="rust">{t('product.primeOnly')}</Tag>
            ) : null}
          </span>
        ) : null}
      </div>
      {detail.prime && !detail.prime.only ? (
        <p className="price-prime">
          <Tag tone="blue">{t('product.primePrice')}</Tag>
          <span className="price-prime-value">{detail.prime.price}</span>
          {detail.prime.discount ? (
            <span className="price-prime-discount">
              {detail.prime.discount}
            </span>
          ) : null}
        </p>
      ) : null}
      {detail.coupon ? <p className="coupon-line">{detail.coupon}</p> : null}
    </>
  )
}

export function ProductView({
  product,
  loading,
  error,
  refreshing,
  refreshError,
  onRefresh,
  onBack,
  onRetry,
  onSelectVariant,
  saved,
  onToggleSave,
  compared,
  onToggleCompare,
  compareError,
  backLabel,
}: {
  product: ProductResponse | null
  loading: boolean
  error: string | null
  refreshing: boolean
  refreshError: string | null
  onRefresh: () => void
  onBack: () => void
  onRetry: () => void
  onSelectVariant: (asin: string) => void
  saved: boolean
  onToggleSave: () => void
  compared: boolean
  onToggleCompare: () => void
  compareError: string | null
  backLabel: string
}) {
  const { t, languageLocale } = useI18n()
  const detail = product?.product
  const singleVariant =
    detail?.variants?.items.length === 1 ? detail.variants.items[0] : null
  const variantName = singleVariant ? variantLabel(singleVariant) : null
  const singleVariantLabel =
    variantName && variantName !== singleVariant?.asin
      ? variantName
      : detail?.model_name?.trim() || null
  const showSingleVariantIdentity = Boolean(
    singleVariantLabel || detail?.availability,
  )
  const images = detail
    ? Array.from(new Set([detail.image, ...(detail.images ?? [])].filter(Boolean)))
    : []
  const offers = detail?.buying_options ?? null
  const featuredOption =
    offers?.options.find(
      (option) =>
        detail?.seller &&
        option.seller === detail.seller &&
        (!detail.price || option.price === detail.price),
    ) ??
    offers?.options.find(
      (option) => detail?.seller && option.seller === detail.seller,
    ) ??
    offers?.options[0]
  const featuredCondition =
    featuredOption?.condition ??
    offers?.options.find((option) => option.condition)?.condition ??
    offers?.condition
  const conditionKey =
    conditionTypeTranslationKey(featuredOption?.condition_type) ??
    conditionTranslationKey(featuredCondition)
  const condition = conditionKey
    ? t(conditionKey)
    : conditionLabel(featuredCondition)
  const featuredGrade =
    conditionGradeFromToken(featuredOption?.condition_grade) ??
    conditionGrade(featuredCondition)
  const featuredGradeKey = conditionGradeTranslationKey(featuredGrade)
  const used =
    featuredOption?.condition_type === 'used' ||
    isUsedCondition(featuredCondition)
  const hasDirectPrice =
    Boolean(detail?.price) || product?.price_amount != null
  const displayPrice = hasDirectPrice
    ? formatPrice(
        product?.price_amount,
        product?.currency,
        detail?.price ?? '',
        languageLocale,
      )
    : t('product.priceUnavailable')
  const priceIsFrom = !hasDirectPrice && Boolean(offers?.lowest_price)
  const displaySeller =
    detail?.seller ||
    (offers?.options.length === 1 ? offers.options[0].seller : '')
  const fulfilment = detail
    ? fulfilmentLabel(detail, displaySeller, t)
    : ''
  const cacheTone = product?.stale
    ? 'is-stale'
    : product?.cached
      ? 'is-cached'
      : 'is-fresh'

  const [activeImage, setActiveImage] = useState(0)
  const [galleryWidth, setGalleryWidth] = useState(580)
  const [stageImageSize, setStageImageSize] = useState(580)
  const [stageHeight, setStageHeight] = useState(580)
  const stageRef = useRef<HTMLDivElement>(null)
  const railRef = useRef<HTMLDivElement>(null)
  const [lightbox, setLightbox] = useState<ReviewImageLightbox | null>(null)
  const [reviewDialog, setReviewDialog] = useState<ProductReview | null>(null)
  const [offersOpen, setOffersOpen] = useState(false)
  const lightboxTrigger = useRef<HTMLElement | null>(null)
  const reviewTrigger = useRef<HTMLElement | null>(null)
  const offersTrigger = useRef<HTMLElement | null>(null)

  useEffect(() => {
    setActiveImage(0)
    setLightbox(null)
    setReviewDialog(null)
    setOffersOpen(false)
  }, [detail?.asin, images.length])

  useLayoutEffect(() => {
    const stage = stageRef.current
    if (!stage) {
      return
    }
    const measure = () => {
      const width = stage.clientWidth
      const wide = window.matchMedia('(min-width: 1200px)').matches
      const gallery = wide
        ? Math.round(Math.min(580, Math.max(420, width - 666)))
        : Math.round(Math.min(560, Math.max(280, width - 74)))
      setGalleryWidth(gallery)
      if (!wide) {
        setStageHeight(0)
        setStageImageSize(gallery)
        return
      }

      const rail = railRef.current
      const railHeight = rail
        ? Math.round(rail.getBoundingClientRect().height)
        : gallery
      const height = Math.max(1, railHeight)
      setStageHeight(height)
      setStageImageSize(Math.max(1, Math.min(gallery, height)))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    if (railRef.current) {
      observer.observe(railRef.current)
    }
    return () => observer.disconnect()
  }, [detail?.asin])

  const openReview = (review: ProductReview, trigger: HTMLElement) => {
    reviewTrigger.current = trigger
    setReviewDialog(review)
  }

  const openReviewImage = (
    review: ProductReview,
    index: number,
    trigger: HTMLElement,
  ) => {
    lightboxTrigger.current = trigger
    setLightbox({
      images: review.images ?? [],
      index,
      author: review.author,
      title: review.title,
    })
  }

  const openOffers = (trigger: HTMLElement) => {
    offersTrigger.current = trigger
    setOffersOpen(true)
  }

  return (
    <section className="product-view">
      <div className="product-nav">
        <button className="back-button" type="button" onClick={onBack}>
          <Icon name="arrow-left" />
          {backLabel}
        </button>
        {detail ? (
          <div className="product-nav-tools">
            <div className="product-nav-meta">
              <span
                className={`cache-status ${cacheTone}`}
                title={cacheStatus(product, t)}
              >
                <span className="cache-dot" aria-hidden="true" />
                <span className="cache-label">{cacheStatus(product, t)}</span>
              </span>
              <span
                className="product-meta-chip"
                title={`${product.marketplace.display_name} · ${detail.asin}`}
              >
                <Icon name="globe" />
                <span>{product.marketplace.display_name}</span>
                <span className="product-meta-sep" aria-hidden="true">
                  ·
                </span>
                <span className="mono">{detail.asin}</span>
              </span>
            </div>
            <div className="product-nav-actions">
              <button
                className={`compare-detail-button${compared ? ' is-compared' : ''}`}
                type="button"
                aria-pressed={compared}
                onClick={onToggleCompare}
                title={
                  compared
                    ? t('compare.inCompare')
                    : t('compare.addAction')
                }
                data-tooltip={
                  compared
                    ? t('compare.inCompare')
                    : t('compare.addAction')
                }
              >
                <Icon name="compare" />
                <span>
                  {compared
                    ? t('compare.inCompare')
                    : t('compare.addAction')}
                </span>
              </button>
              <button
                className={`icon-button${refreshing ? ' is-refreshing' : ''}`}
                type="button"
                onClick={onRefresh}
                disabled={refreshing}
                aria-label={
                  refreshing
                    ? t('product.refreshing')
                    : t('product.refreshDetails')
                }
                title={
                  refreshing
                    ? t('product.refreshing')
                    : t('product.refreshDetails')
                }
                data-tooltip={
                  refreshing
                    ? t('product.refreshing')
                    : t('product.refreshDetails')
                }
              >
                <Icon name="refresh" />
              </button>
              <button
                className={`icon-button${saved ? ' is-saved' : ''}`}
                type="button"
                onClick={onToggleSave}
                aria-pressed={saved}
                aria-label={
                  saved ? t('saved.removeBookmark') : t('saved.addBookmark')
                }
                title={
                  saved ? t('saved.removeBookmark') : t('saved.addBookmark')
                }
                data-tooltip={
                  saved ? t('saved.removeBookmark') : t('saved.addBookmark')
                }
              >
                <Icon name={saved ? 'bookmark-filled' : 'bookmark'} />
              </button>
            </div>
          </div>
        ) : null}
      </div>

      {compareError ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('compare.addFailed')}</strong>
            <p>{compareError}</p>
          </div>
        </div>
      ) : null}

      {refreshError ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('product.refreshFailed')}</strong>
            <p>{refreshError}</p>
          </div>
        </div>
      ) : null}

      {loading ? (
        <div className="product-loading">
          <div className="skeleton skeleton-gallery" />
          <div className="skeleton-copy">
            <span className="skeleton skeleton-title" />
            <span className="skeleton skeleton-line" />
            <span className="skeleton skeleton-line short" />
            <span className="skeleton skeleton-price" />
            <span className="skeleton skeleton-purchase" />
          </div>
        </div>
      ) : null}

      {error ? (
        <div className="notice notice-error" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('product.unableToLoad')}</strong>
            <p>{error}</p>
            <button className="text-button" type="button" onClick={onRetry}>
              {t('product.tryAgain')}
            </button>
          </div>
        </div>
      ) : null}

      {product?.warnings && product.warnings.length > 0 ? (
        <div className="notice notice-warning">
          <Icon name="alert" />
          <div>
            <strong>{t('product.incomplete')}</strong>
            {product.warnings.map((warning) => (
              <p key={warning}>{warning}</p>
            ))}
          </div>
        </div>
      ) : null}

      {detail && product ? (
        <>
          <div className="product-mobile-identity">
            <ProductIdentity
              detail={detail}
              showFulfilment
              fulfilment={fulfilment}
            />
          </div>

          <div
            className="product-stage"
            ref={stageRef}
            style={
              {
                '--gallery-width': `${galleryWidth}px`,
                '--stage-image-size': `${stageImageSize}px`,
                '--stage-height': `${stageHeight}px`,
              } as CSSProperties
            }
          >
            <ProductGallery
              images={images}
              title={detail.title}
              activeIndex={activeImage}
              onSelect={setActiveImage}
            />

            <div className="product-rail" ref={railRef}>
              <div className="product-rail-body">
                <ProductIdentity detail={detail} />

                {detail.variants && detail.variants.items.length > 1 ? (
                  <BuyBox
                    key={detail.asin}
                    variants={detail.variants}
                    detail={detail}
                    asin={detail.asin}
                    domain={product.marketplace.id}
                    displayPrice={displayPrice}
                    priceIsFrom={priceIsFrom}
                    priceUnavailable={!hasDirectPrice}
                    condition={condition}
                    conditionTone={used ? 'rust' : 'neutral'}
                    gradeKey={featuredGradeKey}
                    gradeTone={conditionGradeTone(featuredGrade)}
                    onSelect={onSelectVariant}
                  />
                ) : (
                  <div className="product-price-row is-single">
                    {showSingleVariantIdentity ? (
                      <div className="single-variant-identity">
                        {singleVariantLabel ? (
                          <span className="buybox-selection-name">
                            <span className="buybox-selection-primary">
                              {singleVariantLabel}
                            </span>
                          </span>
                        ) : null}
                        <Stock text={detail.availability} />
                      </div>
                    ) : null}
                    <div className="price-block">
                      <PriceBlock
                        detail={detail}
                        displayPrice={displayPrice}
                        priceIsFrom={priceIsFrom}
                        condition={condition}
                        conditionTone={used ? 'rust' : 'neutral'}
                        gradeKey={featuredGradeKey}
                        gradeTone={conditionGradeTone(featuredGrade)}
                        t={t}
                      />
                    </div>
                  </div>
                )}

                <PurchasePanel
                  detail={detail}
                  displaySeller={displaySeller}
                  showAvailability={
                    !(detail.variants && detail.variants.items.length > 1)
                    && !showSingleVariantIdentity
                  }
                />
              </div>

              <div className="product-rail-footer">
                <a
                  className="primary-button purchase-cta"
                  href={detail.url}
                  target="_blank"
                  rel="noreferrer"
                >
                  {t('product.openOnAmazon')}
                  <Icon name="external" />
                </a>
              </div>
            </div>
          </div>

          {detail.charger?.included === false ||
          (detail.safety_warnings?.length ?? 0) > 0 ? (
            <div className="product-notices">
              {detail.charger?.included === false ? (
                <div className="notice notice-warning" role="alert">
                  <Icon name="alert" />
                  <div>
                    <strong>{t('product.chargerNotIncluded')}</strong>
                    {detail.charger.note ? (
                      <p>{detail.charger.note}</p>
                    ) : null}
                    {detail.charger.box_contents ? (
                      <p>
                        {t('product.boxContents', {
                          contents: detail.charger.box_contents,
                        })}
                      </p>
                    ) : null}
                  </div>
                </div>
              ) : null}
              <SafetyWarnings warnings={detail.safety_warnings ?? []} />
            </div>
          ) : null}

          {offers && (offers.options.length > 0 || offers.count > 0) ? (
            <OffersPreview offers={offers} onOpenAll={openOffers} />
          ) : null}

          <ProductColumns
            key={detail.asin}
            description={detail.description}
            specs={detail.specs}
          />

          {detail.amazon_details && detail.amazon_details.length > 0 ? (
            <section className="detail-section amazon-details-section">
              <h2>{t('product.amazonDetails')}</h2>
              <dl className="spec-list">
                {detail.amazon_details.map((item, index) => (
                  <div key={`${item.name}-${index}`}>
                    <dt>{item.name}</dt>
                    <dd>{item.value}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ) : null}

          {detail.top_reviews ? (
            <ReviewsSection
              reviews={detail.top_reviews}
              onOpen={openReview}
              onOpenImage={openReviewImage}
            />
          ) : null}
        </>
      ) : null}

      {reviewDialog ? (
        <ReviewDialog
          review={reviewDialog}
          onClose={() => setReviewDialog(null)}
          onOpenImage={openReviewImage}
          returnFocus={reviewTrigger.current}
          suspendKeys={Boolean(lightbox)}
        />
      ) : null}

      {offersOpen && offers ? (
        <OffersDialog
          offers={offers}
          onClose={() => setOffersOpen(false)}
          returnFocus={offersTrigger.current}
        />
      ) : null}

      {lightbox ? (
        <ReviewLightbox
          {...lightbox}
          returnFocus={lightboxTrigger.current}
          onClose={() => setLightbox(null)}
        />
      ) : null}
    </section>
  )
}
