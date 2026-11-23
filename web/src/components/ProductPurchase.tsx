import { useRef, useState, type MouseEvent } from 'react'
import type {
  BuyingOption,
  BuyingOptionsSummary,
  DeliveryInfo,
  ProductDetail,
} from '../lib/api'
import {
  conditionGrade,
  conditionGradeFromToken,
  conditionGradeTranslationKey,
  conditionLabel,
  conditionTranslationKey,
  conditionTypeTranslationKey,
  isUsedCondition,
  type ConditionGrade,
} from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import type { Translate } from '../i18n/types'
import { fulfilmentLabel } from '../lib/fulfilment'
import { DetailDialog } from './DetailDialog'
import { Icon, Tag } from './ui'

export function conditionGradeTone(
  grade: ConditionGrade | null,
): 'gold' | 'blue' | 'neutral' | 'rust' {
  switch (grade) {
    case 'premium':
      return 'gold'
    case 'excellent':
      return 'blue'
    case 'acceptable':
      return 'rust'
    default:
      return 'neutral'
  }
}

function offerLabels(option: BuyingOption, t: Translate) {
  const labelKey =
    conditionTypeTranslationKey(option.condition_type) ??
    conditionTranslationKey(option.condition)
  const label = labelKey ? t(labelKey) : conditionLabel(option.condition)
  const grade =
    conditionGradeFromToken(option.condition_grade) ??
    conditionGrade(option.condition)
  const gradeKey = conditionGradeTranslationKey(grade)
  const showRaw = !labelKey && !gradeKey && Boolean(option.condition)
  return { label, grade, gradeKey, showRaw }
}

function OfferRows({
  options,
  showHeader = false,
}: {
  options: BuyingOption[]
  showHeader?: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="offer-rows-wrap">
      {showHeader ? (
        <div className="offer-row offer-row-head" aria-hidden="true">
          <span>{t('product.condition')}</span>
          <span>{t('product.seller')}</span>
          <span>{t('product.delivery')}</span>
          <span>{t('product.price')}</span>
          <span />
        </div>
      ) : null}
      <ul className="offer-rows">
        {options.map((option, index) => {
          const { label, grade, gradeKey, showRaw } = offerLabels(option, t)
          return (
            <li
              className="offer-row"
              key={
                option.offer_listing_id ||
                `${option.seller}-${option.price}-${index}`
              }
            >
              <div className="offer-row-condition">
                {label ? (
                  <Tag
                    tone={
                      option.condition_type === 'used' ||
                      isUsedCondition(option.condition)
                        ? 'rust'
                        : 'neutral'
                    }
                  >
                    {label}
                  </Tag>
                ) : null}
                {gradeKey ? (
                  <Tag tone={conditionGradeTone(grade)}>{t(gradeKey)}</Tag>
                ) : null}
                {showRaw ? (
                  <span className="offer-raw">{option.condition}</span>
                ) : null}
              </div>
              <div className="offer-row-seller">
                {option.seller || t('product.sellerUnspecified')}
              </div>
              <div className="offer-row-delivery">
                {[option.shipping, option.delivery].filter(Boolean).join(' · ') ||
                  '—'}
              </div>
              <span className="offer-row-price">{option.price || '—'}</span>
              {option.detail_url ? (
                <a
                  className="text-button offer-row-link"
                  href={option.detail_url}
                  target="_blank"
                  rel="noreferrer"
                >
                  {t('product.viewOffer')}
                  <Icon name="external" />
                </a>
              ) : (
                <span />
              )}
            </li>
          )
        })}
      </ul>
    </div>
  )
}

export function OffersPreview({
  offers,
  onOpenAll,
}: {
  offers: BuyingOptionsSummary
  onOpenAll: (trigger: HTMLElement) => void
}) {
  const { t } = useI18n()
  const featured = offers.options.find((option) => option.is_featured)
  const alternatives = offers.options.filter((option) => !option.is_featured)
  const count = Math.max(
    offers.count - (featured ? 1 : 0),
    alternatives.length,
  )
  const visible = alternatives.slice(0, 4)

  if (visible.length === 0 && count <= 0) {
    return null
  }

  return (
    <section className="detail-section offers-section">
      <div className="section-heading">
        <h2>{t('product.offers')}</h2>
        <button
          type="button"
          className="text-button offers-all"
          onClick={(event) => onOpenAll(event.currentTarget)}
          aria-haspopup="dialog"
        >
          {t('product.showAllOffers', { count })}
        </button>
      </div>
      {offers.lowest_price ? (
        <p className="offers-lowest">
          {t('product.fromPrice', { price: offers.lowest_price })}
        </p>
      ) : null}
      {visible.length > 0 ? <OfferRows options={visible} /> : null}
    </section>
  )
}

export function OffersDialog({
  offers,
  onClose,
  returnFocus,
}: {
  offers: BuyingOptionsSummary
  onClose: () => void
  returnFocus?: HTMLElement | null
}) {
  const { t } = useI18n()
  const featured = offers.options.find((option) => option.is_featured)
  const alternatives = offers.options.filter((option) => !option.is_featured)
  const count = Math.max(
    offers.count - (featured ? 1 : 0),
    alternatives.length,
  )

  return (
    <DetailDialog
      wide
      className="offers-dialog"
      title={t('product.offers')}
      subtitle={
        offers.lowest_price
          ? `${t('product.fromPrice', { price: offers.lowest_price })} · ${t(
              'product.offerCount',
              { count },
            )}`
          : t('product.offerCount', { count })
      }
      onClose={onClose}
      returnFocus={returnFocus}
      footer={
        <button type="button" className="text-button" onClick={onClose}>
          {t('nav.close')}
        </button>
      }
    >
      {alternatives.length > 0 ? (
        <OfferRows options={alternatives} showHeader />
      ) : null}
      {count > alternatives.length ? (
        <p className="offers-partial">
          {t('product.offersPartial', {
            total: count,
            shown: alternatives.length,
          })}
        </p>
      ) : null}
    </DetailDialog>
  )
}

type DeliveryOption = NonNullable<DeliveryInfo['options']>[number]

function isPriceLike(value: string | null | undefined): boolean {
  if (!value) {
    return false
  }
  if (/gratis|free|gratuita|gratuit|kostenlos/i.test(value)) {
    return false
  }
  return /[€$£¥]|\d/.test(value)
}

function fastestDelivery(
  delivery: ProductDetail['delivery'],
): DeliveryOption | null {
  if (!delivery) {
    return null
  }
  const options = delivery.options ?? []
  const distinct = options.filter(
    (option) =>
      option.time !== delivery.time || option.cost !== delivery.cost,
  )
  return (
    distinct.find((option) =>
      /pi[uù] rapid|fastest|prime|express|schnell|rapide|r[aá]pid|お急ぎ/i.test(
        `${option.type ?? ''} ${option.cost ?? ''} ${option.time ?? ''}`,
      ),
    ) ??
    distinct.find((option) => Boolean(option.time)) ??
    null
  )
}

function compactCutoff(value: string): string {
  const trimmed = value.trim()
  const unwrapped = trimmed
    .replace(/^[（(]\s*/, '')
    .replace(/\s*[)）]$/, '')
  const prefixes = [
    'Ordina entro le ',
    'Ordina entro ',
    'Order within ',
    'Bestellen Sie innerhalb von ',
    'Bestelle innerhalb von ',
    'Commandez dans les ',
    'Commandez avant ',
    'Commandez sous ',
    'Pídelo en ',
    'Pide antes de ',
    'Pide en ',
  ]
  const lower = unwrapped.toLocaleLowerCase()
  for (const prefix of prefixes) {
    if (lower.startsWith(prefix.toLocaleLowerCase())) {
      return unwrapped.slice(prefix.length).trim()
    }
  }
  const japanese = unwrapped.match(
    /^(.*?)以内に(?:ご注文|注文).*$/,
  )
  if (japanese) {
    return japanese[1].trim()
  }
  return unwrapped
}

function DeliveryOptionsDialog({
  options,
  location,
  onClose,
  returnFocus,
}: {
  options: DeliveryOption[]
  location?: string | null
  onClose: () => void
  returnFocus?: HTMLElement | null
}) {
  const { t } = useI18n()
  return (
    <DetailDialog
      title={t('product.deliveryOptions')}
      subtitle={location || undefined}
      onClose={onClose}
      returnFocus={returnFocus}
    >
      <div className="delivery-dialog-list">
        {options.map((option, index) => (
          <div className="delivery-dialog-row" key={`${option.type}-${index}`}>
            <div className="delivery-dialog-copy">
              <strong>
                {option.type ?? t('product.deliveryFallback')}
              </strong>
              {option.cutoff ? <span>{option.cutoff}</span> : null}
            </div>
            <span className="delivery-dialog-cost">{option.cost}</span>
            <span className="delivery-dialog-time">{option.time}</span>
          </div>
        ))}
      </div>
    </DetailDialog>
  )
}

export function PurchasePanel({
  detail,
  displaySeller,
  showAvailability = true,
}: {
  detail: ProductDetail
  displaySeller: string
  showAvailability?: boolean
}) {
  const { t } = useI18n()
  const [deliveryOpen, setDeliveryOpen] = useState(false)
  const deliveryButtonRef = useRef<HTMLButtonElement>(null)
  const delivery = detail.delivery
  const deliveryLocation = delivery?.location?.trim() ?? ''
  const deliveryPostalCode = delivery?.postal_code?.trim() ?? ''
  const deliveryPlace =
    deliveryPostalCode
    || deliveryLocation
    || delivery?.country?.trim()
    || ''
  const deliveryPlaceFull =
    deliveryLocation
    && deliveryPostalCode
    && !deliveryLocation.includes(deliveryPostalCode)
      ? `${deliveryLocation} ${deliveryPostalCode}`
      : deliveryPlace
  const options: DeliveryOption[] = delivery?.options ?? []
  const fastest = fastestDelivery(delivery)
  const deliveryLine = delivery
    ? [delivery.cost, delivery.time].filter(Boolean).join(' · ') ||
      delivery.type ||
      '—'
    : ''
  const fastestValue = fastest
    ? [
        isPriceLike(fastest.cost) ? fastest.cost : '',
        fastest.time,
      ]
        .filter(Boolean)
        .join(' · ')
    : ''
  const showDeliveryOptions = options.length > 2
  const hasDetails = Boolean(deliveryLine || detail.returns)
  const deliveryCost = delivery?.cost || delivery?.time || delivery?.type || ''
  const deliveryTime = delivery?.time || ''
  const fulfilment = fulfilmentLabel(detail, displaySeller, t)
  const openDelivery = (event: MouseEvent<HTMLButtonElement>) => {
    deliveryButtonRef.current = event.currentTarget
    setDeliveryOpen(true)
  }
  const meta = (
    <div className={`purchase-meta${hasDetails ? '' : ' is-standalone'}`}>
      {detail.brand ? (
        <span>
          <span className="purchase-origin-label">{t('product.brand')}</span>
          {detail.brand}
        </span>
      ) : null}
      {fulfilment ? (
        <span className="purchase-fulfilment">
          <Icon name="package" />
          <span>{fulfilment}</span>
        </span>
      ) : null}
      {showAvailability && detail.availability ? (
        <span>
          <span className="purchase-origin-label">
            {t('product.availability')}
          </span>
          {detail.availability}
        </span>
      ) : null}
    </div>
  )

  return (
    <aside className="purchase-panel">
      {hasDetails ? (
        <section className="purchase-service">
          <div className="purchase-service-head">
            <span className="purchase-service-head-main">
              <span className="purchase-service-title">
                {t('product.deliveryAndReturns')}
              </span>
              {deliveryPlace ? (
                <span
                  className="purchase-location"
                  title={t('product.deliverTo', {
                    location: deliveryPlaceFull,
                  })}
                >
                  <Icon name="map-pin" />
                  <span>{deliveryPlace}</span>
                </span>
              ) : null}
            </span>
            {showDeliveryOptions ? (
              <button
                type="button"
                className="purchase-service-link"
                aria-haspopup="dialog"
                onClick={openDelivery}
              >
                {t('product.deliveryOptions')}
              </button>
            ) : null}
          </div>
          <div className="purchase-service-grid">
            {deliveryLine ? (
              <div className="purchase-service-item">
                <div className="purchase-service-label-row">
                  <span className="purchase-service-label-group">
                    <span className="purchase-service-label">
                      {t('product.delivery')}
                    </span>
                    {showDeliveryOptions ? (
                      <span className="purchase-service-inline-options">
                        <span
                          className="purchase-service-inline-dot"
                          aria-hidden="true"
                        >
                          ·
                        </span>
                        <button
                          type="button"
                          className="purchase-service-inline-link"
                          aria-haspopup="dialog"
                          title={t('product.deliveryOptions')}
                          onClick={openDelivery}
                        >
                          {t('product.deliveryOptionCount', {
                            count: options.length,
                          })}
                        </button>
                      </span>
                    ) : null}
                  </span>
                  <span className="purchase-service-label-tools">
                    {deliveryPlace ? (
                      <span
                        className="purchase-location purchase-location-inline"
                        title={t('product.deliverTo', {
                          location: deliveryPlaceFull,
                        })}
                      >
                        <Icon name="map-pin" />
                        <span>{deliveryPlace}</span>
                      </span>
                    ) : null}
                    {delivery?.cutoff ? (
                      <span
                        className="purchase-service-cutoff"
                        title={delivery.cutoff}
                      >
                        <Icon name="clock" />
                        <span>{compactCutoff(delivery.cutoff)}</span>
                      </span>
                    ) : null}
                  </span>
                </div>
                <span className="purchase-service-value is-delivery">
                  <strong>{deliveryCost}</strong>
                  {deliveryTime && deliveryTime !== deliveryCost ? (
                    <>
                      <span
                        className="purchase-delivery-separator"
                        aria-hidden="true"
                      >
                        ·
                      </span>
                      <span>{deliveryTime}</span>
                    </>
                  ) : null}
                </span>
              </div>
            ) : null}
            {fastestValue ? (
              <div className="purchase-service-item">
                <span className="purchase-service-label">
                  {t('product.fastestDelivery')}
                </span>
                <span className="purchase-service-value">
                  {fastestValue}
                </span>
              </div>
            ) : null}
            {detail.returns ? (
              <div className="purchase-service-item">
                <span className="purchase-service-label">
                  {t('product.returns')}
                </span>
                <span className="purchase-service-value">
                  {detail.returns}
                </span>
              </div>
            ) : null}
          </div>
          {meta}
        </section>
      ) : (
        meta
      )}

      {detail.frequently_returned ? (
        <p className="purchase-note purchase-note-warning">
          <span>{t('product.frequentlyReturned')}</span>
          {detail.frequently_returned}
        </p>
      ) : null}

      {detail.charger && detail.charger.included !== false ? (
        <p className="purchase-note">
          <span>{t('product.charger')}</span>
          {detail.charger.included === true
            ? t('product.chargerIncluded')
            : detail.charger.label || '-'}
        </p>
      ) : null}

      {deliveryOpen && delivery ? (
        <DeliveryOptionsDialog
          options={options}
          location={delivery.location}
          onClose={() => setDeliveryOpen(false)}
          returnFocus={deliveryButtonRef.current}
        />
      ) : null}
    </aside>
  )
}
