import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { getVariantPrices } from '../lib/api'
import type {
  ProductDetail,
  ProductVariant,
  ProductVariants,
} from '../lib/api'
import { useI18n } from '../i18n/I18nProvider'
import type { Translate, TranslationKey } from '../i18n/types'
import { DetailDialog } from './DetailDialog'
import { Tag } from './ui'

type PriceProps = {
  detail: ProductDetail
  displayPrice: string
  priceIsFrom: boolean
  priceUnavailable: boolean
  condition: string | null
  conditionTone: 'rust' | 'neutral'
  gradeKey: TranslationKey | null
  gradeTone: 'gold' | 'blue' | 'neutral' | 'rust'
}

type BuyBoxProps = PriceProps & {
  variants: ProductVariants
  asin: string
  domain: string
  onSelect: (asin: string) => void
}

const COLOR_HEX: Record<string, string> = {
  nero: '#211f1c',
  black: '#211f1c',
  schwarz: '#211f1c',
  noir: '#211f1c',
  negro: '#211f1c',
  ブラック: '#211f1c',
  bianco: '#f2ede3',
  white: '#f2ede3',
  weiss: '#f2ede3',
  weiß: '#f2ede3',
  blanc: '#f2ede3',
  blanco: '#f2ede3',
  ホワイト: '#f2ede3',
  grafite: '#3b3f44',
  graphite: '#3b3f44',
  grafit: '#3b3f44',
  grafito: '#3b3f44',
  グラファイト: '#3b3f44',
  'grigio chiaro': '#c3c6c8',
  'light gray': '#c3c6c8',
  'light grey': '#c3c6c8',
  hellgrau: '#c3c6c8',
  'gris clair': '#c3c6c8',
  'gris claro': '#c3c6c8',
  ライトグレー: '#c3c6c8',
  grigio: '#8f959a',
  gray: '#8f959a',
  grey: '#8f959a',
  grau: '#8f959a',
  gris: '#8f959a',
  グレー: '#8f959a',
  argento: '#c9ccd0',
  silver: '#c9ccd0',
  silber: '#c9ccd0',
  argent: '#c9ccd0',
  plata: '#c9ccd0',
  シルバー: '#c9ccd0',
  blu: '#3d6583',
  blue: '#3d6583',
  blau: '#3d6583',
  bleu: '#3d6583',
  azul: '#3d6583',
  ブルー: '#3d6583',
  rosso: '#a8442a',
  red: '#a8442a',
  rot: '#a8442a',
  rouge: '#a8442a',
  rojo: '#a8442a',
  レッド: '#a8442a',
  verde: '#2f6b4f',
  green: '#2f6b4f',
  grün: '#2f6b4f',
  gruen: '#2f6b4f',
  vert: '#2f6b4f',
  グリーン: '#2f6b4f',
  rosa: '#d98ba5',
  pink: '#d98ba5',
  rose: '#d98ba5',
  ピンク: '#d98ba5',
}

function normalize(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim()
}

export function variantLabel(item: ProductVariant): string {
  return Object.values(item.attributes).filter(Boolean).join(' · ') || item.asin
}

function dimensionLabel(
  variants: ProductVariants,
  t: Translate,
): string {
  return (
    Object.values(variants.dimensions ?? {}).find(Boolean) ??
    t('product.variant')
  )
}

function colorDimension(variants: ProductVariants): string | null {
  const keys = new Set<string>()
  for (const item of variants.items) {
    Object.keys(item.attributes).forEach((key) => keys.add(key))
  }
  for (const key of keys) {
    if (/colou?r|colore|farbe|couleur|color|色/i.test(key)) {
      return key
    }
  }
  return null
}

function variantParts(item: ProductVariant, colorKey: string | null) {
  const entries = Object.entries(item.attributes).filter(([, value]) => value)
  const color = colorKey ? item.attributes[colorKey] : null
  const primary =
    entries
      .filter(([key]) => key !== colorKey)
      .map(([, value]) => value)
      .join(' · ') ||
    color ||
    item.asin
  return {
    primary,
    color,
    hex: color ? COLOR_HEX[normalize(color)] ?? null : null,
  }
}

function stockTone(text: string | null | undefined): 'in' | 'low' | 'out' {
  if (!text) {
    return 'in'
  }
  const value = normalize(text)
  if (
    /(out of stock|non disponibile|esaurito|derzeit nicht|indisponible|agotado|在庫切れ|入荷不可)/.test(
      value,
    )
  ) {
    return 'out'
  }
  if (/(solo|only|nur noch|plus que|quedan|残り)/.test(value)) {
    return 'low'
  }
  return 'in'
}

export function Stock({ text }: { text?: string | null }) {
  if (!text) {
    return null
  }
  const tone = stockTone(text)
  return (
    <span className={`buybox-stock is-${tone}`}>
      <i aria-hidden="true" />
      {text}
    </span>
  )
}

function Swatch({ hex, label }: { hex: string; label?: string | null }) {
  return (
    <span
      className="buybox-swatch"
      style={{ backgroundColor: hex }}
      title={label ?? undefined}
      aria-hidden="true"
    />
  )
}

function BuyBoxPrice({
  detail,
  displayPrice,
  priceIsFrom,
  priceUnavailable,
  condition,
  conditionTone,
  gradeKey,
  gradeTone,
  t,
}: PriceProps & { t: Translate }) {
  const hasMeta =
    Boolean(condition) ||
    Boolean(gradeKey) ||
    (!priceUnavailable &&
      (Boolean(detail.original_price) || Boolean(detail.discount)))
  const compact = priceUnavailable || displayPrice.length > 16

  return (
    <div
      className={`buybox-price${priceUnavailable ? ' is-unavailable' : ''}${
        compact ? ' is-compact' : ''
      }`}
    >
      <span className="buybox-price-line">
        {!priceUnavailable && priceIsFrom ? (
          <span className="price-from">{t('product.from')}</span>
        ) : null}
        {priceUnavailable ? (
          <span className="buybox-price-unavailable">{displayPrice}</span>
        ) : (
          <span className="price price-large">{displayPrice}</span>
        )}
      </span>
      {detail.prime ? (
        <p className={`price-prime${detail.prime.only ? ' is-only' : ''}`}>
          <Tag tone={detail.prime.only ? 'rust' : 'blue'}>
            {detail.prime.only
              ? t('product.primeOnly')
              : t('product.primePrice')}
          </Tag>
          {detail.prime.only ? null : (
            <>
              <span className="price-prime-value">
                {detail.prime.price}
              </span>
              {detail.prime.discount ? (
                <span className="price-prime-discount">
                  {detail.prime.discount}
                </span>
              ) : null}
            </>
          )}
        </p>
      ) : null}
      {hasMeta ? (
        <span className="buybox-price-meta">
          {!priceUnavailable && detail.original_price ? (
            <span className="price-original">{detail.original_price}</span>
          ) : null}
          {!priceUnavailable && detail.discount ? (
            <span className="buybox-discount">{detail.discount}</span>
          ) : null}
          {condition ? <Tag tone={conditionTone}>{condition}</Tag> : null}
          {gradeKey ? <Tag tone={gradeTone}>{t(gradeKey)}</Tag> : null}
        </span>
      ) : null}
    </div>
  )
}

const VARIANT_CACHE_VERSION = 'v1'
const VARIANT_CACHE_TTL_MS = 30 * 60 * 1000

type VariantCacheEntry = {
  savedAt: number
  variants: ProductVariants
}

function variantCacheKey(
  domain: string,
  variants: ProductVariants,
  asin: string,
): string {
  const parent = variants.parent_asin || variants.selected_asin || asin
  return `amz-cli:variants:${VARIANT_CACHE_VERSION}:${domain}:${parent}`
}

function normalizeCachedVariants(
  cached: ProductVariants,
  current: ProductVariants,
): ProductVariants | null {
  const currentAsins = new Set(current.items.map((item) => item.asin))
  if (
    cached.items.length !== current.items.length ||
    cached.items.some((item) => !currentAsins.has(item.asin))
  ) {
    return null
  }
  const selectedAsin =
    current.selected_asin ||
    current.items.find((item) => item.selected)?.asin ||
    cached.selected_asin
  return {
    ...cached,
    parent_asin: cached.parent_asin || current.parent_asin,
    selected_asin: selectedAsin,
    dimensions: cached.dimensions ?? current.dimensions,
    items: cached.items.map((item) => ({
      ...item,
      selected: selectedAsin
        ? item.asin === selectedAsin
        : item.selected,
    })),
  }
}

function readVariantCache(
  current: ProductVariants,
  domain: string,
  asin: string,
): { variants: ProductVariants; fresh: boolean } | null {
  if (typeof window === 'undefined') {
    return null
  }
  try {
    const raw = window.localStorage.getItem(
      variantCacheKey(domain, current, asin),
    )
    if (!raw) {
      return null
    }
    const entry = JSON.parse(raw) as VariantCacheEntry
    if (
      !entry?.savedAt ||
      !entry.variants ||
      !Array.isArray(entry.variants.items)
    ) {
      return null
    }
    const variants = normalizeCachedVariants(entry.variants, current)
    if (!variants) {
      return null
    }
    return {
      variants,
      fresh: Date.now() - entry.savedAt < VARIANT_CACHE_TTL_MS,
    }
  } catch {
    return null
  }
}

function writeVariantCache(
  variants: ProductVariants,
  domain: string,
  asin: string,
) {
  if (typeof window === 'undefined') {
    return
  }
  try {
    window.localStorage.setItem(
      variantCacheKey(domain, variants, asin),
      JSON.stringify({ savedAt: Date.now(), variants }),
    )
  } catch {
    // A full or disabled localStorage must not block variant selection.
  }
}

function useVariantPricing(
  variants: ProductVariants,
  asin: string,
  domain: string,
) {
  const { t } = useI18n()
  const cachedRef = useRef<ReturnType<typeof readVariantCache> | null>(null)
  const [pricedVariants, setPricedVariants] =
    useState<ProductVariants | null>(() => {
      const cached = readVariantCache(variants, domain, asin)
      cachedRef.current = cached
      return cached?.variants ?? null
    })
  const [loading, setLoading] = useState(false)
  const [warning, setWarning] = useState<string | null>(null)
  const loadedRef = useRef(Boolean(cachedRef.current?.fresh))
  const requestRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const cached = readVariantCache(variants, domain, asin)
    cachedRef.current = cached
    setPricedVariants(cached?.variants ?? null)
    setLoading(false)
    setWarning(null)
    loadedRef.current = Boolean(cached?.fresh)
    requestRef.current?.abort()
  }, [asin])

  useEffect(() => () => requestRef.current?.abort(), [])

  const ensurePrices = useCallback(async () => {
    if (loadedRef.current) {
      return
    }
    loadedRef.current = true
    requestRef.current?.abort()
    const controller = new AbortController()
    requestRef.current = controller
    setLoading(true)
    setWarning(null)
    try {
      const result = await getVariantPrices(asin, domain, controller.signal)
      if (result.variants) {
        const selectedAsin =
          variants.selected_asin ||
          variants.items.find((item) => item.selected)?.asin ||
          asin
        const normalized: ProductVariants = {
          ...result.variants,
          selected_asin: selectedAsin,
          items: result.variants.items.map((item) => ({
            ...item,
            selected: item.asin === selectedAsin,
          })),
        }
        setPricedVariants(normalized)
        writeVariantCache(normalized, domain, asin)
      }
      if (result.warnings && result.warnings.length > 0) {
        setWarning(result.warnings[0])
      }
    } catch (error) {
      if ((error as Error).name !== 'AbortError') {
        loadedRef.current = false
        setWarning(
          error instanceof Error && error.message
            ? error.message
            : t('product.variantPricesFailed'),
        )
      }
    } finally {
      if (!controller.signal.aborted) {
        setLoading(false)
      }
    }
  }, [asin, domain, t])

  useEffect(() => {
    if (variants.items.length <= 1) {
      return
    }
    const timer = window.setTimeout(() => {
      void ensurePrices()
    }, 900)
    return () => window.clearTimeout(timer)
  }, [ensurePrices, variants])

  const current = pricedVariants ?? variants
  const items = current.items
  const selected = items.find((item) => item.selected) ?? items[0]
  return { current, items, selected, loading, warning, ensurePrices }
}

function Plate({
  items,
  selected,
  loading,
  colorKey,
  label,
  t,
  price,
  fallbackAvailability,
  onSelect,
  onShowAll,
}: {
  items: ProductVariant[]
  selected: ProductVariant
  loading: boolean
  colorKey: string | null
  label: string
  t: Translate
  price: ReactNode
  fallbackAvailability?: string | null
  onSelect: (asin: string) => void
  onShowAll: () => void
}) {
  const parts = variantParts(selected, colorKey)
  const chipsRef = useRef<HTMLDivElement>(null)
  const [edges, setEdges] = useState({ left: false, right: false })
  const orderedItems = [
    ...items.filter((item) => item.asin === selected.asin),
    ...items.filter((item) => item.asin !== selected.asin),
  ]

  useEffect(() => {
    chipsRef.current?.scrollTo({ left: 0 })
  }, [selected.asin])

  useEffect(() => {
    const element = chipsRef.current
    if (!element) {
      return
    }

    let target = element.scrollLeft
    let frame = 0
    let animation = 0

    const maxScroll = () =>
      Math.max(0, element.scrollWidth - element.clientWidth)

    const updateEdges = () => {
      const max = maxScroll()
      setEdges({
        left: element.scrollLeft > 2,
        right: max > 2 && element.scrollLeft < max - 2,
      })
    }

    const reduceMotion = () =>
      window.matchMedia('(prefers-reduced-motion: reduce)').matches

    const step = (time: number) => {
      const distance = target - element.scrollLeft
      if (Math.abs(distance) < 0.5) {
        element.scrollLeft = target
        frame = 0
        updateEdges()
        return
      }
      const elapsed = Math.min(32, Math.max(8, time - animation))
      animation = time
      const easing = reduceMotion() ? 0.5 : 0.22
      element.scrollLeft += distance * Math.min(0.6, easing * (elapsed / 16.7))
      frame = requestAnimationFrame(step)
    }

    const onWheel = (event: WheelEvent) => {
      if (element.scrollWidth <= element.clientWidth + 1) {
        return
      }
      if (event.ctrlKey) {
        return
      }
      const delta =
        Math.abs(event.deltaY) >= Math.abs(event.deltaX)
          ? event.deltaY
          : event.deltaX
      if (!delta) {
        return
      }
      event.preventDefault()
      target = Math.max(
        0,
        Math.min(maxScroll(), target + Math.max(-240, Math.min(240, delta))),
      )
      if (!frame) {
        animation = performance.now()
        frame = requestAnimationFrame(step)
      }
    }

    const onScroll = () => {
      if (!frame) {
        target = element.scrollLeft
      }
      updateEdges()
    }

    updateEdges()
    element.addEventListener('wheel', onWheel, { passive: false })
    element.addEventListener('scroll', onScroll, { passive: true })
    const observer = new ResizeObserver(updateEdges)
    observer.observe(element)
    window.addEventListener('resize', updateEdges)

    return () => {
      element.removeEventListener('wheel', onWheel)
      element.removeEventListener('scroll', onScroll)
      observer.disconnect()
      window.removeEventListener('resize', updateEdges)
      if (frame) {
        cancelAnimationFrame(frame)
      }
    }
  }, [items.length])

  return (
    <section className="buybox buybox-plate">
      <div className="buybox-plate-main">
        <div className="buybox-selection">
          <span className="buybox-selection-name">
            <span className="buybox-selection-primary">{parts.primary}</span>
            {parts.color ? (
              <span className="buybox-selection-color">
                {parts.hex ? (
                  <Swatch hex={parts.hex} label={parts.color} />
                ) : null}
                {parts.color}
              </span>
            ) : null}
          </span>
          <Stock text={selected.availability || fallbackAvailability} />
        </div>
        {price}
      </div>

      <div className="buybox-chips-shell">
        <div
          ref={chipsRef}
          className="buybox-chips"
          role="radiogroup"
          aria-label={label}
          tabIndex={0}
          onKeyDown={(event) => {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
              return
            }
            event.preventDefault()
            chipsRef.current?.scrollBy({
              left: event.key === 'ArrowRight' ? 180 : -180,
              behavior: 'smooth',
            })
          }}
        >
          {orderedItems.map((item) => {
            const chip = variantParts(item, colorKey)
            return (
              <button
                key={item.asin}
                type="button"
                role="radio"
                aria-checked={item.selected}
                className={`buybox-chip${item.selected ? ' is-selected' : ''}`}
                style={{
                  flex: orderedItems.length <= 3 ? '1 1 0' : '0 0 168px',
                }}
                onClick={() => onSelect(item.asin)}
                title={variantLabel(item)}
              >
                <span className="buybox-chip-main">{chip.primary}</span>
                <span className="buybox-chip-sub">
                  {chip.color ? (
                    <span className="buybox-chip-color">
                      {chip.hex ? (
                        <Swatch hex={chip.hex} label={chip.color} />
                      ) : null}
                      {chip.color}
                    </span>
                  ) : null}
                  {item.price ? (
                    <span className="buybox-chip-price" title={item.price}>
                      {chip.color ? ' · ' : ''}
                      {item.price}
                    </span>
                  ) : loading ? (
                    <span className="buybox-chip-skeleton" aria-hidden="true" />
                  ) : (
                    <span
                      className="buybox-chip-price is-empty"
                      title={t('product.priceUnavailable')}
                    >
                      {chip.color ? ' · ' : ''}—
                    </span>
                  )}
                </span>
              </button>
            )
          })}
        </div>
        <span
          className={`buybox-chips-fade is-left${
            edges.left ? ' is-visible' : ''
          }`}
          aria-hidden="true"
        />
        <span
          className={`buybox-chips-fade is-right${
            edges.right ? ' is-visible' : ''
          }`}
          aria-hidden="true"
        />
      </div>

      {items.length > 3 ? (
        <div className="buybox-foot">
          <span className="buybox-foot-count">
            {t('product.variantCount', { count: items.length })}
          </span>
          <button type="button" className="buybox-all" onClick={onShowAll}>
            {t('product.showAllVariants')}
          </button>
        </div>
      ) : null}
    </section>
  )
}

export function BuyBox({
  variants,
  asin,
  domain,
  onSelect,
  ...priceProps
}: BuyBoxProps) {
  const { t } = useI18n()
  const { current, items, selected, loading, warning, ensurePrices } =
    useVariantPricing(variants, asin, domain)
  const [dialogOpen, setDialogOpen] = useState(false)
  const returnFocusRef = useRef<HTMLElement | null>(null)

  if (!selected) {
    return null
  }

  const colorKey = colorDimension(current)
  const label = dimensionLabel(current, t)
  const price = <BuyBoxPrice {...priceProps} t={t} />

  const openAll = () => {
    returnFocusRef.current = document.activeElement as HTMLElement | null
    setDialogOpen(true)
    void ensurePrices()
  }
  const choose = (nextAsin: string) => {
    void ensurePrices()
    onSelect(nextAsin)
  }

  return (
    <>
      <Plate
        items={items}
        selected={selected}
        loading={loading}
        colorKey={colorKey}
        label={label}
        t={t}
        price={price}
        fallbackAvailability={priceProps.detail.availability}
        onSelect={choose}
        onShowAll={openAll}
      />

      {warning ? <p className="buybox-note buybox-warning">{warning}</p> : null}
      {priceProps.detail.coupon ? (
        <p className="coupon-line buybox-coupon">{priceProps.detail.coupon}</p>
      ) : null}

      {dialogOpen ? (
        <VariantsDialog
          variants={current}
          loading={loading}
          warning={warning}
          onSelect={(nextAsin) => {
            choose(nextAsin)
            setDialogOpen(false)
          }}
          onClose={() => setDialogOpen(false)}
          returnFocus={returnFocusRef.current}
        />
      ) : null}
    </>
  )
}

function VariantPrice({ item }: { item: ProductVariant }) {
  if (!item.price) {
    return <span className="variant-option-price is-empty">—</span>
  }
  return (
    <span className="variant-option-price">
      <strong>{item.price}</strong>
      {item.original_price ? (
        <span className="variant-option-original">{item.original_price}</span>
      ) : null}
      {item.discount ? <Tag tone="blue">{item.discount}</Tag> : null}
    </span>
  )
}

function VariantsDialog({
  variants,
  loading,
  warning,
  onSelect,
  onClose,
  returnFocus,
}: {
  variants: ProductVariants
  loading: boolean
  warning: string | null
  onSelect: (asin: string) => void
  onClose: () => void
  returnFocus?: HTMLElement | null
}) {
  const { t } = useI18n()
  const items = [...variants.items].sort(
    (left, right) =>
      Number(right.selected) - Number(left.selected) ||
      variantLabel(left).localeCompare(variantLabel(right)),
  )

  return (
    <DetailDialog
      wide
      title={t('product.variantsTitle')}
      subtitle={`${dimensionLabel(variants, t)} · ${t('product.variantCount', {
        count: items.length,
      })}`}
      onClose={onClose}
      returnFocus={returnFocus}
    >
      {loading ? (
        <p className="variant-popover-note">
          {t('product.loadingVariantPrices')}
        </p>
      ) : null}
      {warning ? <p className="variant-popover-note">{warning}</p> : null}
      <div className="variant-dialog-list">
        {items.map((item) => (
          <button
            type="button"
            className={`variant-dialog-row${item.selected ? ' is-selected' : ''}`}
            aria-pressed={item.selected}
            key={item.asin}
            onClick={() => {
              onSelect(item.asin)
              onClose()
            }}
          >
            <span className="variant-dialog-copy">
              <strong>{variantLabel(item)}</strong>
              {item.availability ? <span>{item.availability}</span> : null}
            </span>
            <VariantPrice item={item} />
          </button>
        ))}
      </div>
    </DetailDialog>
  )
}
