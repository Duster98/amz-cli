import { useEffect, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import { Icon } from './ui'

export function DetailDialog({
  title,
  subtitle,
  children,
  footer,
  onClose,
  returnFocus,
  wide = false,
  className,
  suspendKeys = false,
}: {
  title: string
  subtitle?: ReactNode
  children: ReactNode
  footer?: ReactNode
  onClose: () => void
  returnFocus?: HTMLElement | null
  wide?: boolean
  className?: string
  suspendKeys?: boolean
}) {
  const { t } = useI18n()
  const panelRef = useRef<HTMLDivElement>(null)
  const restoreRef = useRef<Element | null>(null)

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
    // The page already reserves the scrollbar gutter. Adding the scrollbar
    // width again would shift the layout left when the dialog opens.
    if (!gutterStable && scrollbar > 0) {
      document.body.style.paddingRight = `${scrollbar}px`
    }

    return () => {
      document.body.style.overflow = previousOverflow
      document.body.style.paddingRight = previousPadding
      if (restoreRef.current instanceof HTMLElement) {
        restoreRef.current.focus()
      }
    }
  }, [returnFocus])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (suspendKeys) {
        return
      }
      event.stopPropagation()
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
        return
      }
      if (event.key !== 'Tab' || !panelRef.current) {
        return
      }

      const focusable = Array.from(
        panelRef.current.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
        ),
      )
      if (focusable.length === 0) {
        event.preventDefault()
        panelRef.current.focus()
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

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose, suspendKeys])

  return createPortal(
    <div
      className="detail-dialog-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <div
        ref={panelRef}
        className={`detail-dialog${wide ? ' is-wide' : ''}${className ? ` ${className}` : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
      >
        <header className="detail-dialog-head">
          <div>
            <h2>{title}</h2>
            {subtitle ? <p>{subtitle}</p> : null}
          </div>
          <button
            className="detail-dialog-close"
            type="button"
            onClick={onClose}
            aria-label={t('nav.close')}
          >
            <Icon name="close" />
          </button>
        </header>
        <div className="detail-dialog-body">{children}</div>
        {footer ? <footer className="detail-dialog-actions">{footer}</footer> : null}
      </div>
    </div>,
    document.body,
  )
}
