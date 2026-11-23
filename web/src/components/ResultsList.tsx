import type { SearchItem } from '../lib/api'
import {
  conditionTranslationKey,
  formatPrice,
  isUsedCondition,
  parseRating,
} from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import { Icon, ProductImage, RatingStars, Tag } from './ui'

export type ResultsLayout = 'list' | 'grid'

export function ResultsList({
  items,
  currency,
  layout = 'list',
  showDelivery = true,
  onOpen,
  isSaved,
  onToggleSave,
}: {
  items: SearchItem[]
  currency: string
  layout?: ResultsLayout
  showDelivery?: boolean
  onOpen: (asin: string, marketplaceId?: string | null) => void
  isSaved?: (item: SearchItem) => boolean
  onToggleSave?: (item: SearchItem) => void
}) {
  const { t, languageLocale } = useI18n()
  if (layout === 'grid') {
    return (
      <ul className="result-grid">
        {items.map((item, index) => {
          const rating = parseRating(item.rating)
          const conditionKey = conditionTranslationKey(item.condition)
          return (
            <li className="result-grid-cell" key={item.asin}>
              {onToggleSave ? (
                <button
                  className={`result-bookmark${isSaved?.(item) ? ' is-saved' : ''}`}
                  type="button"
                  aria-pressed={isSaved?.(item) ?? false}
                  aria-label={
                    isSaved?.(item)
                      ? t('saved.removeBookmark')
                      : t('saved.addBookmark')
                  }
                  title={
                    isSaved?.(item)
                      ? t('saved.removeBookmark')
                      : t('saved.addBookmark')
                  }
                  onClick={(event) => {
                    event.preventDefault()
                    event.stopPropagation()
                    onToggleSave(item)
                  }}
                >
                  <Icon
                    name={isSaved?.(item) ? 'bookmark-filled' : 'bookmark'}
                  />
                </button>
              ) : null}
              <button
                className="result-card"
                type="button"
                onClick={() => onOpen(item.asin, item.marketplace_id)}
                aria-label={t('results.openProduct', { title: item.title })}
              >
                <span className="result-card-image">
                  <ProductImage
                    src={item.image}
                    alt={item.title}
                    eager={index < 12}
                  />
                </span>
                <span className="result-card-body">
                  <span className="result-tags">
                    {conditionKey ? (
                      <Tag
                        tone={
                          isUsedCondition(item.condition) ? 'rust' : 'neutral'
                        }
                      >
                        <span title={item.condition ?? undefined}>
                          {t(conditionKey)}
                        </span>
                      </Tag>
                    ) : null}
                    {item.deal ? <Tag tone="accent">{item.deal}</Tag> : null}
                    {item.amazon_choice ? (
                      <Tag tone="gold">
                        {item.amazon_choice_text ?? t('results.amazonChoice')}
                      </Tag>
                    ) : null}
                    {item.discount ? (
                      <Tag tone="blue">{item.discount}</Tag>
                    ) : null}
                  </span>

                  <span className="result-card-title">{item.title}</span>

                  <span className="result-meta">
                    {rating > 0 ? (
                      <RatingStars value={rating} count={item.reviews} />
                    ) : null}
                    {showDelivery && item.delivery_text ? (
                      <span className="delivery-line">
                        <Icon name="package" />
                        {item.delivery_text}
                      </span>
                    ) : null}
                  </span>

                  <span className="result-card-foot">
                    <span className="result-card-price">
                      {item.original_price ? (
                        <span className="price-original">
                          {item.original_price}
                        </span>
                      ) : null}
                      {item.price || item.price_amount != null ? (
                        <span className="price">
                          {formatPrice(
                            item.price_amount,
                            item.currency ?? currency,
                            item.price,
                            languageLocale,
                          )}
                        </span>
                      ) : (
                        <span className="price-unavailable">
                          {t('results.seeOptions')}
                        </span>
                      )}
                    </span>
                    {item.coupon ? (
                      <Tag tone="blue">{item.coupon}</Tag>
                    ) : null}
                    {item.sponsored ? (
                      <Tag tone="quiet">{t('results.sponsored')}</Tag>
                    ) : null}
                  </span>
                </span>
              </button>
            </li>
          )
        })}
      </ul>
    )
  }

  return (
    <ol className="result-list">
      {items.map((item, index) => {
        const rating = parseRating(item.rating)
        const conditionKey = conditionTranslationKey(item.condition)
        return (
          <li className="result-list-item" key={item.asin}>
            <button
              className="result-row"
              type="button"
              onClick={() => onOpen(item.asin, item.marketplace_id)}
              aria-label={t('results.openProduct', { title: item.title })}
            >
              <span className="result-index" aria-hidden="true">
                {String(index + 1).padStart(2, '0')}
              </span>
              <span className="result-image">
                <ProductImage
                  src={item.image}
                  alt={item.title}
                  eager={index < 10}
                />
              </span>
              <span className="result-copy">
                <span className="result-tags">
                  {conditionKey ? (
                    <Tag
                      tone={isUsedCondition(item.condition) ? 'rust' : 'neutral'}
                    >
                      <span title={item.condition ?? undefined}>
                        {t(conditionKey)}
                      </span>
                    </Tag>
                  ) : null}
                  {item.sponsored ? (
                    <Tag tone="quiet">{t('results.sponsored')}</Tag>
                  ) : null}
                  {item.deal ? <Tag tone="accent">{item.deal}</Tag> : null}
                  {item.amazon_choice ? (
                    <Tag tone="gold">
                      {item.amazon_choice_text ?? t('results.amazonChoice')}
                    </Tag>
                  ) : null}
                  {item.discount ? (
                    <Tag tone="blue">{item.discount}</Tag>
                  ) : null}
                </span>
                <span className="result-title">{item.title}</span>
                <span className="result-meta">
                  {rating > 0 ? (
                    <RatingStars value={rating} count={item.reviews} />
                  ) : null}
                  {showDelivery && item.delivery_text ? (
                    <span className="delivery-line">
                      <Icon name="package" />
                      {item.delivery_text}
                    </span>
                  ) : null}
                  {item.coupon ? <Tag tone="blue">{item.coupon}</Tag> : null}
                </span>
              </span>
              <span className="result-price-block">
                {item.original_price ? (
                  <span className="price-original">{item.original_price}</span>
                ) : null}
                {item.price || item.price_amount != null ? (
                  <span className="price">
                    {formatPrice(
                      item.price_amount,
                      item.currency ?? currency,
                      item.price,
                      languageLocale,
                    )}
                  </span>
                ) : (
                  <span className="price-unavailable">
                    {t('results.seeOptions')}
                  </span>
                )}
              </span>
            </button>
            {onToggleSave ? (
              <button
                className={`result-bookmark result-bookmark-list${isSaved?.(item) ? ' is-saved' : ''}`}
                type="button"
                aria-pressed={isSaved?.(item) ?? false}
                aria-label={
                  isSaved?.(item)
                    ? t('saved.removeBookmark')
                    : t('saved.addBookmark')
                }
                title={
                  isSaved?.(item)
                    ? t('saved.removeBookmark')
                    : t('saved.addBookmark')
                }
                onClick={(event) => {
                  event.preventDefault()
                  event.stopPropagation()
                  onToggleSave(item)
                }}
              >
                <Icon
                  name={isSaved?.(item) ? 'bookmark-filled' : 'bookmark'}
                />
              </button>
            ) : null}
          </li>
        )
      })}
    </ol>
  )
}
