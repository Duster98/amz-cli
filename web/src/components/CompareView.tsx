import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import type {
  CompareItem,
  CompareState,
  ProductDetail,
  ProductResponse,
} from '../lib/api'
import { getProduct, refreshProductDetails } from '../lib/api'
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
import type { Translate } from '../i18n/types'
import { conditionGradeTone } from './ProductPurchase'
import { Icon, ProductImage, RatingStars, Tag } from './ui'
import { ConfirmDialog } from './SavedProductsView'

type CompareProductState = {
  product: ProductResponse | null
  loading: boolean
  refreshing: boolean
  error: string | null
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function deliveryText(product: ProductDetail): string {
  const delivery = product.delivery
  if (!delivery) {
    return ''
  }
  return [delivery.cost, delivery.time].filter(Boolean).join(' · ')
    || delivery.type
    || ''
}

function offerCondition(product: ProductDetail, t: Translate) {
  const offers = product.buying_options ?? null
  const option =
    offers?.options.find(
      (candidate) => product.seller && candidate.seller === product.seller,
    ) ?? offers?.options[0]
  const raw =
    option?.condition ||
    offers?.options.find((candidate) => candidate.condition)?.condition ||
    offers?.condition
  const typeKey =
    conditionTypeTranslationKey(option?.condition_type) ??
    conditionTranslationKey(raw)
  const label = typeKey ? t(typeKey) : conditionLabel(raw)
  const grade =
    conditionGradeFromToken(option?.condition_grade) ?? conditionGrade(raw)
  const gradeKey = conditionGradeTranslationKey(grade)
  return {
    label,
    used: option?.condition_type === 'used' || isUsedCondition(raw),
    grade,
    gradeKey,
    text: [label, gradeKey ? t(gradeKey) : null].filter(Boolean).join(' · '),
  }
}

function cacheLabel(product: ProductResponse, t: Translate): string {
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
    return t(
      product.cached ? 'product.cachedMinutes' : 'product.minutesAgo',
      { count: Math.floor(seconds / 60) },
    )
  }
  return t(
    product.cached ? 'product.cachedHours' : 'product.hoursAgo',
    { count: Math.floor(seconds / 3600) },
  )
}

function CompareFact({
  label,
  value,
  different,
  t,
}: {
  label: string
  value: string
  different: boolean
  t: Translate
}) {
  return (
    <div className={`compare-fact${different ? ' is-different' : ''}`}>
      <dt>
        {label}
        {different ? (
          <span
            className="compare-diff-dot"
            aria-label={t('compare.different')}
            title={t('compare.different')}
          />
        ) : null}
      </dt>
      <dd>{value || '—'}</dd>
    </div>
  )
}

function CompareSkeleton() {
  return (
    <div className="compare-skeleton" aria-hidden="true">
      <span className="skeleton compare-skeleton-image" />
      <span className="skeleton compare-skeleton-title" />
      <span className="skeleton compare-skeleton-line" />
      <span className="skeleton compare-skeleton-price" />
      <span className="skeleton compare-skeleton-line short" />
    </div>
  )
}

export function CompareView({
  compare,
  loading,
  error,
  deliveryCountry,
  deliveryPostalCode,
  onCloseItem,
  onClear,
  onOpenSearch,
  onOpenProduct,
}: {
  compare: CompareState
  loading: boolean
  error: string | null
  deliveryCountry: string
  deliveryPostalCode: string
  onCloseItem: (domain: string, asin: string) => Promise<void>
  onClear: () => void
  onOpenSearch: () => void
  onOpenProduct: (asin: string, domain: string) => void
}) {
  const { t, formatNumber } = useI18n()
  const [activeId, setActiveId] = useState('')
  const [products, setProducts] = useState<
    Record<string, CompareProductState>
  >({})
  const productsRef = useRef(products)
  const controllersRef = useRef(new Map<string, AbortController>())
  const previousIdsRef = useRef<Set<string>>(new Set())
  const initializedRef = useRef(false)
  const [confirmClear, setConfirmClear] = useState(false)

  const updateProduct = useCallback(
    (id: string, patch: Partial<CompareProductState>) => {
      setProducts((current) => {
        const next = {
          ...current,
          [id]: {
            ...(current[id] ?? {
              product: null,
              loading: false,
              refreshing: false,
              error: null,
            }),
            ...patch,
          },
        }
        productsRef.current = next
        return next
      })
    },
    [],
  )

  const loadProduct = useCallback(
    async (item: CompareItem, force = false) => {
      controllersRef.current.get(item.id)?.abort()
      const controller = new AbortController()
      controllersRef.current.set(item.id, controller)
      const previous = productsRef.current[item.id]
      updateProduct(item.id, {
        loading: !force,
        refreshing: force,
        error: null,
      })
      try {
        const product = force
          ? await refreshProductDetails(
              item.asin,
              item.domain,
              controller.signal,
              deliveryCountry,
              deliveryPostalCode,
            )
          : await getProduct(
              item.asin,
              item.domain,
              controller.signal,
              deliveryCountry,
              deliveryPostalCode,
            )
        if (!controller.signal.aborted) {
          updateProduct(item.id, {
            product,
            loading: false,
            refreshing: false,
            error: null,
          })
        }
      } catch (caught) {
        if (!controller.signal.aborted) {
          updateProduct(item.id, {
            product: force ? previous?.product ?? null : null,
            loading: false,
            refreshing: false,
            error: errorMessage(caught),
          })
        }
      } finally {
        if (controllersRef.current.get(item.id) === controller) {
          controllersRef.current.delete(item.id)
        }
      }
    },
    [deliveryCountry, deliveryPostalCode, updateProduct],
  )

  useEffect(() => {
    const ids = new Set(compare.items.map((item) => item.id))
    for (const [id, controller] of controllersRef.current) {
      if (!ids.has(id)) {
        controller.abort()
        controllersRef.current.delete(id)
      }
    }
    setProducts((current) => {
      let changed = false
      const next = { ...current }
      for (const id of Object.keys(next)) {
        if (!ids.has(id)) {
          delete next[id]
          changed = true
        }
      }
      if (changed) {
        productsRef.current = next
        return next
      }
      return current
    })

    const itemIds = compare.items.map((item) => item.id)
    const added = itemIds.filter((id) => !previousIdsRef.current.has(id))
    previousIdsRef.current = ids
    if (!initializedRef.current) {
      initializedRef.current = true
      setActiveId(itemIds[0] ?? '')
    } else if (added.length > 0) {
      setActiveId(added[added.length - 1])
      if (window.matchMedia('(max-width: 820px)').matches) {
        window.scrollTo({ top: 0, behavior: 'smooth' })
      }
    } else if (!itemIds.includes(activeId)) {
      setActiveId(itemIds[0] ?? '')
    }
  }, [activeId, compare.items])

  useEffect(() => {
    let cancelled = false
    const missing = compare.items.filter((item) => {
      const state = productsRef.current[item.id]
      return !state?.product && !state?.loading && !state?.error
    })
    if (missing.length === 0) {
      return
    }

    const queue = [...missing]
    const worker = async () => {
      while (!cancelled) {
        const item = queue.shift()
        if (!item) {
          return
        }
        await loadProduct(item)
      }
    }
    void Promise.all([worker(), worker()])
    return () => {
      cancelled = true
    }
  }, [compare.items, loadProduct])

  useEffect(
    () => () => {
      for (const controller of controllersRef.current.values()) {
        controller.abort()
      }
      controllersRef.current.clear()
    },
    [],
  )

  useEffect(() => {
    if (!activeId || !window.matchMedia('(min-width: 821px)').matches) {
      return
    }
    const element = document.getElementById(`compare-column-${activeId}`)
    element?.scrollIntoView({
      behavior: 'smooth',
      inline: 'nearest',
      block: 'nearest',
    })
  }, [activeId])

  const loaded = compare.items
    .map((item) => ({
      item,
      state: products[item.id],
      data: products[item.id]?.product ?? null,
    }))
    .filter((entry): entry is typeof entry & { data: ProductResponse } =>
      entry.data !== null)

  const sameCurrency =
    new Set(loaded.map((entry) => entry.data.currency)).size <= 1
  const priced = loaded.filter(
    (entry) => entry.data.price_amount != null
      && !entry.data.product.prime?.only,
  )
  const bestPrice =
    sameCurrency && priced.length > 1
      ? Math.min(
          ...priced.map((entry) => entry.data.price_amount as number),
        )
      : null

  const factValues = (pick: (product: ProductDetail) => string) =>
    loaded
      .map((entry) => pick(entry.data.product).trim())
      .filter(Boolean)
  const hasDifference = (values: string[]) =>
    values.length > 1 && new Set(values).size > 1

  const differences = {
    condition: hasDifference(
      loaded
        .map((entry) => offerCondition(entry.data.product, t).text)
        .filter(Boolean),
    ),
    availability: hasDifference(
      factValues((product) => product.availability),
    ),
    seller: hasDifference(factValues((product) => product.seller)),
    shippedBy: hasDifference(
      factValues((product) => product.shipped_by),
    ),
    delivery: hasDifference(
      factValues((product) => deliveryText(product)),
    ),
    returns: hasDifference(factValues((product) => product.returns)),
  }

  const specKeys = useMemo(() => {
    const counts = new Map<string, number>()
    for (const entry of loaded) {
      for (const key of Object.keys(entry.data.product.specs ?? {})) {
        counts.set(key, (counts.get(key) ?? 0) + 1)
      }
    }
    return [...counts.entries()]
      .sort((left, right) =>
        right[1] - left[1] || left[0].localeCompare(right[0]))
      .slice(0, 8)
      .map(([key]) => key)
  }, [loaded])

  const specDiff = new Map(
    specKeys.map((key) => [
      key,
      hasDifference(
        loaded
          .map((entry) => entry.data.product.specs?.[key]?.trim() ?? '')
          .filter(Boolean),
      ),
    ]),
  )

  const countLabel =
    compare.items.length === 1
      ? t('compare.countOne', {
          count: formatNumber(compare.items.length),
        })
      : t('compare.count', {
          count: formatNumber(compare.items.length),
        })

  return (
    <section className="compare-view">
      <header className="saved-header compare-header">
        <div className="compare-top-row">
          <div className="saved-heading">
            <h1>{t('nav.comparisons')}</h1>
            <p>{t('compare.subtitle')}</p>
          </div>
          <div className="compare-toolbar">
            <span className="compare-count">{countLabel}</span>
            <button
              className="history-clear"
              type="button"
              disabled={compare.items.length === 0}
              onClick={() => setConfirmClear(true)}
            >
              <Icon name="trash" />
              {t('compare.clear')}
            </button>
          </div>
        </div>
      </header>

      {error ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('compare.addFailed')}</strong>
            <p>{error}</p>
          </div>
        </div>
      ) : null}

      {compare.items.length === 0 ? (
        loading ? (
          <p className="compare-loading">{t('compare.loadingList')}</p>
        ) : (
          <div className="empty-state saved-empty compare-empty">
            <h3>{t('compare.emptyTitle')}</h3>
            <p>{t('compare.emptyBody')}</p>
            <button
              className="primary-button"
              type="button"
              onClick={onOpenSearch}
            >
              {t('compare.openSearch')}
            </button>
          </div>
        )
      ) : (
        <>
          <div
            className="compare-tabs"
            role="tablist"
            aria-label={t('compare.tabs')}
          >
            {compare.items.map((item) => {
              const title = products[item.id]?.product?.product.title
              return (
                <div
                  className={`compare-tab${
                    activeId === item.id ? ' is-active' : ''
                  }`}
                  key={item.id}
                >
                  <button
                    className="compare-tab-label"
                    type="button"
                    role="tab"
                    aria-selected={activeId === item.id}
                    aria-controls={`compare-column-${item.id}`}
                    onClick={() => {
                      setActiveId(item.id)
                      if (window.matchMedia('(max-width: 820px)').matches) {
                        window.scrollTo({ top: 0, behavior: 'smooth' })
                      }
                    }}
                  >
                    <span className="compare-tab-domain">
                      {item.domain.toUpperCase()}
                    </span>
                    <span
                      className="compare-tab-title"
                      title={title || item.asin}
                    >
                      {title || item.asin}
                    </span>
                  </button>
                  <button
                    className="compare-tab-close"
                    type="button"
                    aria-label={t('compare.closeTab')}
                    title={t('compare.closeTab')}
                    onClick={() =>
                      void onCloseItem(item.domain, item.asin)
                    }
                  >
                    <Icon name="close" />
                  </button>
                </div>
              )
            })}
          </div>

          <div
            className={`compare-board compare-board--${Math.min(
              compare.items.length,
              3,
            )}`}
          >
            {compare.items.map((item) => {
              const state = products[item.id]
              const active = activeId === item.id
              const fullTitle = state?.product?.product.title?.trim() ?? ''
              const brand = state?.product?.product.brand?.trim() ?? ''
              const titleId = `compare-column-title-${item.id}`
              return (
                <article
                  id={`compare-column-${item.id}`}
                  key={item.id}
                  className={`compare-column${active ? ' is-active' : ''}`}
                  aria-labelledby={titleId}
                >
                  <div className="compare-column-top">
                    <h2
                      className={`compare-column-title${
                        fullTitle ? '' : ' is-placeholder'
                      }`}
                      id={titleId}
                    >
                      {fullTitle ? (
                        <button
                          className="compare-column-title-button"
                          type="button"
                          title={fullTitle}
                          onClick={() =>
                            onOpenProduct(item.asin, item.domain)
                          }
                        >
                          {fullTitle}
                        </button>
                      ) : (
                        item.asin
                      )}
                    </h2>
                    <button
                      className="compare-column-close"
                      type="button"
                      aria-label={t('compare.closeTab')}
                      title={t('compare.closeTab')}
                      onClick={() =>
                        void onCloseItem(item.domain, item.asin)
                      }
                    >
                      <Icon name="close" />
                    </button>
                  </div>
                  <div className="compare-column-meta">
                    <span className="compare-column-domain">
                      {item.domain.toUpperCase()}
                    </span>
                    {brand ? (
                      <span className="compare-column-brand">{brand}</span>
                    ) : null}
                  </div>

                  {state?.loading && !state.product ? (
                    <CompareSkeleton />
                  ) : state?.error && !state.product ? (
                    <div className="compare-error">
                      <div className="notice notice-error" role="alert">
                        <Icon name="alert" />
                        <div>
                          <strong>{t('compare.loadError')}</strong>
                          <p>{state.error}</p>
                        </div>
                      </div>
                      <button
                        className="settings-action"
                        type="button"
                        onClick={() => void loadProduct(item)}
                      >
                        <Icon name="refresh" />
                        {t('compare.retry')}
                      </button>
                    </div>
                  ) : state?.product ? (
                    <CompareProduct
                      data={state.product}
                      refreshing={state.refreshing}
                      error={state.error}
                      bestPrice={bestPrice}
                      sameCurrency={sameCurrency}
                      differences={differences}
                      specKeys={specKeys}
                      specDiff={specDiff}
                      onOpenProduct={onOpenProduct}
                      onRefresh={() => void loadProduct(item, true)}
                      t={t}
                    />
                  ) : (
                    <CompareSkeleton />
                  )}
                </article>
              )
            })}
          </div>
        </>
      )}

      {confirmClear ? (
        <ConfirmDialog
          title={t('compare.clearTitle')}
          body={t('compare.clearBody')}
          confirmLabel={t('compare.clearConfirm')}
          onConfirm={() => {
            setConfirmClear(false)
            onClear()
          }}
          onClose={() => setConfirmClear(false)}
        />
      ) : null}
    </section>
  )
}

function CompareProduct({
  data,
  refreshing,
  error,
  bestPrice,
  sameCurrency,
  differences,
  specKeys,
  specDiff,
  onOpenProduct,
  onRefresh,
  t,
}: {
  data: ProductResponse
  refreshing: boolean
  error: string | null
  bestPrice: number | null
  sameCurrency: boolean
  differences: Record<string, boolean>
  specKeys: string[]
  specDiff: Map<string, boolean>
  onOpenProduct: (asin: string, domain: string) => void
  onRefresh: () => void
  t: Translate
}) {
  const { languageLocale } = useI18n()
  const detail = data.product
  const rating =
    detail.rating_details?.average ?? parseRating(detail.rating)
  const isBestPrice =
    bestPrice !== null
    && data.price_amount != null
    && data.price_amount === bestPrice
  const delivery = deliveryText(detail) || '—'
  const condition = offerCondition(detail, t)
  const cacheTone = data.stale
    ? 'is-stale'
    : data.cached
      ? 'is-cached'
      : 'is-fresh'

  return (
    <div className="compare-product">
      <div className="compare-identity">
        <div className="compare-rating-row">
          <RatingStars value={rating} count={detail.reviews} />
          {detail.amazon_choice ? (
            <Tag tone="gold">
              {detail.amazon_choice_text ?? t('results.amazonChoice')}
            </Tag>
          ) : null}
          {detail.deal ? (
            <Tag tone="accent">{detail.deal}</Tag>
          ) : null}
        </div>
      </div>

      <button
        className="compare-media"
        type="button"
        onClick={() => onOpenProduct(detail.asin, data.marketplace.id)}
        aria-label={t('compare.openDetail')}
      >
        <ProductImage
          src={detail.image}
          alt={detail.title}
          className="compare-image"
        />
      </button>

      <div className="compare-price">
        <span className="compare-price-value">
          {formatPrice(
            data.price_amount,
            data.currency,
            detail.price,
            languageLocale,
          )}
        </span>
        <span className="compare-price-meta">
          {detail.original_price ? (
            <span className="price-original">
              {detail.original_price}
            </span>
          ) : null}
          {detail.discount ? (
            <Tag tone="blue">{detail.discount}</Tag>
          ) : null}
          {condition.label ? (
            <Tag tone={condition.used ? 'rust' : 'neutral'}>
              {condition.label}
            </Tag>
          ) : null}
          {condition.gradeKey ? (
            <Tag tone={conditionGradeTone(condition.grade)}>
              {t(condition.gradeKey)}
            </Tag>
          ) : null}
          {detail.prime?.only ? (
            <Tag tone="rust">{t('product.primeOnly')}</Tag>
          ) : null}
          {isBestPrice ? (
            <Tag tone="accent">{t('compare.bestPrice')}</Tag>
          ) : null}
        </span>
        {detail.prime && !detail.prime.only ? (
          <span className="compare-prime">
            <Tag tone="blue">{t('product.primePrice')}</Tag>
            <span className="compare-prime-value">{detail.prime.price}</span>
            {detail.prime.discount ? (
              <span className="compare-prime-discount">
                {detail.prime.discount}
              </span>
            ) : null}
          </span>
        ) : null}
        {detail.coupon ? (
          <span className="compare-coupon">{detail.coupon}</span>
        ) : null}
        {!sameCurrency ? (
          <span className="compare-currency-note">
            {t('compare.mixedCurrency')}
          </span>
        ) : null}
      </div>

      <dl className="compare-facts">
        <CompareFact
          label={t('product.condition')}
          value={condition.text}
          different={differences.condition}
          t={t}
        />
        <CompareFact
          label={t('compare.availability')}
          value={detail.availability}
          different={differences.availability}
          t={t}
        />
        <CompareFact
          label={t('compare.seller')}
          value={detail.seller}
          different={differences.seller}
          t={t}
        />
        <CompareFact
          label={t('compare.shippedBy')}
          value={detail.shipped_by}
          different={differences.shippedBy}
          t={t}
        />
        <CompareFact
          label={t('compare.delivery')}
          value={delivery}
          different={differences.delivery}
          t={t}
        />
        <CompareFact
          label={t('compare.returns')}
          value={detail.returns}
          different={differences.returns}
          t={t}
        />
      </dl>

      {specKeys.length > 0 ? (
        <section className="compare-specs">
          <h3>{t('compare.specs')}</h3>
          <dl>
            {specKeys.map((key) => (
              <div
                className={`compare-spec-row${
                  specDiff.get(key) ? ' is-different' : ''
                }`}
                key={key}
              >
                <dt>
                  {key}
                  {specDiff.get(key) ? (
                    <span
                      className="compare-diff-dot"
                      aria-label={t('compare.different')}
                      title={t('compare.different')}
                    />
                  ) : null}
                </dt>
                <dd>{detail.specs?.[key]?.trim() || '—'}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : null}

      {error ? (
        <p className="compare-inline-error" role="status">
          {error}
        </p>
      ) : null}

      <footer className="compare-footer">
        <div className="compare-status-row">
          <span className={`cache-status ${cacheTone}`}>
            <span className="cache-dot" aria-hidden="true" />
            <span className="cache-label">{cacheLabel(data, t)}</span>
          </span>
          <button
            className={`compare-refresh-button${
              refreshing ? ' is-refreshing' : ''
            }`}
            type="button"
            disabled={refreshing}
            onClick={onRefresh}
          >
            <Icon name="refresh" />
            {refreshing ? t('product.refreshing') : t('compare.refresh')}
          </button>
        </div>
        <div className="compare-cta-row">
          <button
            className="primary-button compare-open-button"
            type="button"
            onClick={() => onOpenProduct(detail.asin, data.marketplace.id)}
          >
            {t('compare.openDetail')}
          </button>
          <a
            className="compare-amazon-link"
            href={detail.url}
            target="_blank"
            rel="noreferrer"
          >
            {t('compare.openAmazon')}
            <Icon name="external" />
          </a>
        </div>
      </footer>
    </div>
  )
}
