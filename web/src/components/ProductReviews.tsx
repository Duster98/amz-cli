import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { ProductReview, TopReviews } from '../lib/api'
import { parseRating } from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import { DetailDialog } from './DetailDialog'
import { Icon, ProductImage, RatingStars } from './ui'

export function ReviewCard({
  review,
  onOpen,
  onOpenImage,
}: {
  review: ProductReview
  onOpen: (review: ProductReview, trigger: HTMLElement) => void
  onOpenImage: (
    review: ProductReview,
    index: number,
    trigger: HTMLElement,
  ) => void
}) {
  const { t } = useI18n()
  const bodyRef = useRef<HTMLParagraphElement>(null)
  const [canExpand, setCanExpand] = useState(false)
  const images = review.images ?? []
  const visibleImages = images.slice(0, 4)
  const extraImages = images.length - visibleImages.length

  useLayoutEffect(() => {
    const element = bodyRef.current
    if (!element) {
      return
    }
    const measure = () => {
      setCanExpand(element.scrollHeight > element.clientHeight + 1)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [review.body])

  return (
    <article className="review">
      <header>
        <strong>
          {review.url ? (
            <a
              className="review-link"
              href={review.url}
              target="_blank"
              rel="noreferrer"
            >
              {review.title || t('product.reviewFallback')}
            </a>
          ) : (
            review.title || t('product.reviewFallback')
          )}
        </strong>
        <RatingStars
          value={review.rating_value ?? parseRating(review.rating)}
        />
      </header>
      <p className="review-meta">
        {review.author}
        {review.date ? ` · ${review.date}` : ''}
        {review.verified_purchase
          ? ` · ${t('product.verifiedPurchase')}`
          : ''}
      </p>

      {review.body ? (
        <>
          <p className="review-body is-clamped" ref={bodyRef}>
            {review.body}
          </p>
          {canExpand ? (
            <button
              type="button"
              className="text-button review-more"
              onClick={(event) => onOpen(review, event.currentTarget)}
              aria-haspopup="dialog"
            >
              {t('product.showMore')}
            </button>
          ) : null}
        </>
      ) : null}

      {visibleImages.length > 0 ? (
        <div
          className="review-images"
          aria-label={t('product.imagesFrom', { author: review.author })}
        >
          {visibleImages.map((image, index) => (
            <button
              className="review-image"
              type="button"
              onClick={(event) =>
                onOpenImage(review, index, event.currentTarget)
              }
              aria-label={t('product.enlargePhoto', {
                index: index + 1,
                author: review.author,
              })}
              aria-haspopup="dialog"
              key={`${image}-${index}`}
            >
              <ProductImage
                src={image}
                alt={t('product.photoAlt', {
                  index: index + 1,
                  author: review.author,
                })}
              />
              <span className="review-image-zoom" aria-hidden="true">
                <Icon name="expand" />
              </span>
            </button>
          ))}
          {extraImages > 0 ? (
            <button
              type="button"
              className="review-image review-image-more"
              onClick={(event) => onOpen(review, event.currentTarget)}
              aria-label={t('product.moreReviewPhotos', {
                count: extraImages,
              })}
              aria-haspopup="dialog"
            >
              +{extraImages}
            </button>
          ) : null}
        </div>
      ) : null}

      {review.variant ? (
        <p className="review-variant">{review.variant}</p>
      ) : null}
      {review.url ? (
        <a
          className="review-permalink text-button"
          href={review.url}
          target="_blank"
          rel="noreferrer"
        >
          {t('product.openReview')}
          <Icon name="external" />
        </a>
      ) : null}
    </article>
  )
}

export function ReviewDialog({
  review,
  onClose,
  onOpenImage,
  returnFocus,
  suspendKeys = false,
}: {
  review: ProductReview
  onClose: () => void
  onOpenImage: (
    review: ProductReview,
    index: number,
    trigger: HTMLElement,
  ) => void
  returnFocus?: HTMLElement | null
  suspendKeys?: boolean
}) {
  const { t } = useI18n()
  const images = review.images ?? []

  return (
    <DetailDialog
      wide
      title={review.title || t('product.reviewFallback')}
      subtitle={`${review.author}${review.date ? ` · ${review.date}` : ''}${
        review.verified_purchase ? ` · ${t('product.verifiedPurchase')}` : ''
      }`}
      onClose={onClose}
      returnFocus={returnFocus}
      suspendKeys={suspendKeys}
      footer={
        review.url ? (
          <a
            className="text-button"
            href={review.url}
            target="_blank"
            rel="noreferrer"
          >
            {t('product.openReview')}
            <Icon name="external" />
          </a>
        ) : undefined
      }
    >
      <div className="review-dialog">
        <div className="review-dialog-rating">
          <RatingStars
            value={review.rating_value ?? parseRating(review.rating)}
          />
          {review.helpful ? (
            <span className="review-meta">{review.helpful}</span>
          ) : null}
        </div>
        <p className="review-dialog-body">{review.body}</p>
        {images.length > 0 ? (
          <div
            className="review-images review-dialog-images"
            aria-label={t('product.imagesFrom', { author: review.author })}
          >
            {images.map((image, index) => (
              <button
                className="review-image"
                type="button"
                onClick={(event) =>
                  onOpenImage(review, index, event.currentTarget)
                }
                aria-label={t('product.enlargePhoto', {
                  index: index + 1,
                  author: review.author,
                })}
                aria-haspopup="dialog"
                key={`${image}-${index}`}
              >
                <ProductImage
                  src={image}
                  alt={t('product.photoAlt', {
                    index: index + 1,
                    author: review.author,
                  })}
                />
                <span className="review-image-zoom" aria-hidden="true">
                  <Icon name="expand" />
                </span>
              </button>
            ))}
          </div>
        ) : null}
        {review.variant ? (
          <p className="review-variant">{review.variant}</p>
        ) : null}
      </div>
    </DetailDialog>
  )
}

export function ReviewsSection({
  reviews,
  onOpen,
  onOpenImage,
}: {
  reviews: TopReviews
  onOpen: (review: ProductReview, trigger: HTMLElement) => void
  onOpenImage: (
    review: ProductReview,
    index: number,
    trigger: HTMLElement,
  ) => void
}) {
  const { t } = useI18n()
  const [limit, setLimit] = useState(4)

  useEffect(() => {
    setLimit(4)
  }, [reviews])

  if (reviews.items.length === 0) {
    return null
  }

  const visible = reviews.items.slice(0, limit)
  const remaining = reviews.items.length - limit

  return (
    <section className="detail-section reviews-section">
      <div className="section-heading">
        <h2>{t('product.reviews')}</h2>
        <span className="mono">
          {t('product.onPage', { count: reviews.count })}
        </span>
      </div>
      <div className="review-grid">
        {visible.map((review) => (
          <ReviewCard
            review={review}
            onOpen={onOpen}
            onOpenImage={onOpenImage}
            key={review.id || `${review.author}-${review.title}`}
          />
        ))}
      </div>
      {remaining > 0 ? (
        <button
          type="button"
          className="text-button reviews-more"
          onClick={() =>
            setLimit(Math.min(reviews.items.length, limit + 4))
          }
        >
          {t('product.moreReviews', { count: Math.min(4, remaining) })}
          <Icon name="arrow-right" />
        </button>
      ) : null}
      {limit > 4 ? (
        <button
          type="button"
          className="text-button reviews-less"
          onClick={() => setLimit(4)}
        >
          {t('product.showFewerReviews')}
        </button>
      ) : null}
    </section>
  )
}
