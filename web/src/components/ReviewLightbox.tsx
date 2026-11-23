import { useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import { Icon, ProductImage } from './ui'

export type ReviewImageLightbox = {
  images: string[]
  index: number
  author: string
  title?: string
}

const EXIT_MS = 180

export function ReviewLightbox({
  images,
  index: initialIndex,
  author,
  title,
  returnFocus,
  onClose,
}: ReviewImageLightbox & {
  returnFocus?: HTMLElement | null
  onClose: () => void
}) {
  const count = images.length
  const [current, setCurrent] = useState(() =>
    Math.min(Math.max(initialIndex, 0), Math.max(count - 1, 0)),
  )
  const [closing, setClosing] = useState(false)
  const { t } = useI18n()
  const panelRef = useRef<HTMLDivElement>(null)
  const restoreRef = useRef<Element | null>(null)
  const closeTimer = useRef<number | null>(null)

  const requestClose = useCallback(() => {
    if (closing) {
      return
    }
    setClosing(true)
    closeTimer.current = window.setTimeout(() => {
      closeTimer.current = null
      onClose()
    }, EXIT_MS)
  }, [closing, onClose])

  const goTo = useCallback(
    (delta: number) => {
      if (count < 2) {
        return
      }
      setCurrent((value) => (value + delta + count) % count)
    },
    [count],
  )

  useEffect(() => {
    restoreRef.current = returnFocus ?? document.activeElement
    panelRef.current?.focus()

    const previousOverflow = document.body.style.overflow
    const previousPadding = document.body.style.paddingRight
    const scrollbar = window.innerWidth - document.documentElement.clientWidth
    const gutterStable =
      typeof CSS !== 'undefined'
      && typeof CSS.supports === 'function'
      && CSS.supports('scrollbar-gutter', 'stable')
    document.body.style.overflow = 'hidden'
    if (!gutterStable && scrollbar > 0) {
      document.body.style.paddingRight = `${scrollbar}px`
    }

    return () => {
      document.body.style.overflow = previousOverflow
      document.body.style.paddingRight = previousPadding
      if (closeTimer.current !== null) {
        window.clearTimeout(closeTimer.current)
      }
      if (restoreRef.current instanceof HTMLElement) {
        restoreRef.current.focus()
      }
    }
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      event.stopPropagation()
      if (event.key === 'Escape') {
        event.preventDefault()
        requestClose()
        return
      }
      if (event.key === 'ArrowLeft') {
        event.preventDefault()
        goTo(-1)
        return
      }
      if (event.key === 'ArrowRight') {
        event.preventDefault()
        goTo(1)
        return
      }
      if (event.key === 'Tab' && panelRef.current) {
        const focusable = Array.from(
          panelRef.current.querySelectorAll<HTMLElement>(
            'button:not(:disabled), [href], input, select, textarea',
          ),
        )
        if (focusable.length === 0) {
          return
        }
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        const active = document.activeElement
        const onPanel = active === panelRef.current
        if (
          event.shiftKey &&
          (active === first || onPanel || !panelRef.current.contains(active))
        ) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && (active === last || onPanel)) {
          event.preventDefault()
          first.focus()
        }
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [goTo, requestClose])

  return createPortal(
    <div
      className={`lightbox${closing ? ' is-closing' : ''}`}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          requestClose()
        }
      }}
    >
      <div
        className="lightbox-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="review-lightbox-title"
        ref={panelRef}
        tabIndex={-1}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="lightbox-head">
          <div className="lightbox-copy">
            <p className="lightbox-kicker">{t('review.photo')}</p>
            <h2 id="review-lightbox-title">
              {title || t('review.byAuthor', { author })}
            </h2>
            <p className="lightbox-meta">
              {author}
              {count > 1
                ? ` · ${t('review.photoOf', {
                    current: current + 1,
                    count,
                  })}`
                : ''}
            </p>
          </div>
          <button
            className="lightbox-close"
            type="button"
            onClick={requestClose}
            aria-label={t('review.closeImage')}
          >
            <Icon name="close" />
          </button>
        </header>

        <div className={`lightbox-stage${count < 2 ? ' single' : ''}`}>
          {count > 1 ? (
            <button
              className="lightbox-nav"
              type="button"
              onClick={() => goTo(-1)}
              aria-label={t('review.previousPhoto')}
            >
              <Icon name="arrow-left" />
            </button>
          ) : null}
          <div className="lightbox-frame">
            <ProductImage
              key={images[current]}
              className="lightbox-image"
              src={images[current]}
              alt={t('product.photoAlt', {
                index: current + 1,
                author,
              })}
              eager
            />
          </div>
          {count > 1 ? (
            <button
              className="lightbox-nav"
              type="button"
              onClick={() => goTo(1)}
              aria-label={t('review.nextPhoto')}
            >
              <Icon name="arrow-right" />
            </button>
          ) : null}
        </div>

        <footer className="lightbox-foot">
          <span className="mono">
            {current + 1} / {count}
          </span>
          <span className="lightbox-hint">
            {t('review.escToClose')}
            {count > 1 ? ` · ${t('review.arrowKeys')}` : ''}
          </span>
        </footer>
      </div>
    </div>,
    document.body,
  )
}
