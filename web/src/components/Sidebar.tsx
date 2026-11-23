import { useEffect, useRef, useState } from 'react'
import type { Meta } from '../lib/api'
import { useI18n } from '../i18n/I18nProvider'
import type { LanguageCode, TranslationKey } from '../i18n/types'
import { LanguagePicker } from './LanguagePicker'
import { Icon, type IconName } from './ui'

type Theme = 'light' | 'dark'

type NavItem = {
  label: TranslationKey
  icon: IconName
  action?:
    | 'search'
    | 'deals'
    | 'compare'
    | 'saved'
    | 'history'
    | 'monitoring'
    | 'status'
    | 'settings'
  future?: boolean
}

const groups: Array<{ title: TranslationKey; items: NavItem[] }> = [
  {
    title: 'nav.find',
    items: [
      { label: 'nav.search', icon: 'search', action: 'search' },
      { label: 'nav.deals', icon: 'tag', action: 'deals' },
      { label: 'nav.comparisons', icon: 'compare', action: 'compare' },
    ],
  },
  {
    title: 'nav.library',
    items: [
      { label: 'nav.savedProducts', icon: 'bookmark', action: 'saved' },
      { label: 'nav.history', icon: 'clock', action: 'history' },
      { label: 'nav.monitoring', icon: 'activity', action: 'monitoring' },
    ],
  },
  {
    title: 'nav.system',
    items: [
      { label: 'nav.status', icon: 'info', action: 'status' },
      { label: 'nav.settings', icon: 'settings', action: 'settings' },
    ],
  },
]

export function Sidebar({
  meta,
  theme,
  languages,
  pinned,
  mobileOpen,
  view,
  onTogglePin,
  onToggleTheme,
  onRequestClose,
  onHome,
  onOpenDeals,
  onOpenCompare,
  onOpenSaved,
  onOpenHistory,
  onOpenMonitoring,
  onOpenStatus,
  onOpenSettings,
  onMobileClose,
  savedBadge,
  compareBadge,
  updateBadge,
}: {
  meta: Meta
  theme: Theme
  languages: LanguageCode[]
  pinned: boolean
  mobileOpen: boolean
  view:
    | 'search'
    | 'detail'
    | 'deals'
    | 'compare'
    | 'saved'
    | 'history'
    | 'monitoring'
    | 'status'
    | 'settings'
  onTogglePin: () => void
  onToggleTheme: () => void
  onRequestClose: () => void
  onHome: () => void
  onOpenDeals: () => void
  onOpenCompare: () => void
  onOpenSaved: () => void
  onOpenHistory: () => void
  onOpenMonitoring: () => void
  onOpenStatus: () => void
  onOpenSettings: () => void
  onMobileClose: () => void
  savedBadge: boolean
  compareBadge: boolean
  updateBadge: boolean
}) {
  const { t } = useI18n()
  const [languageOpen, setLanguageOpen] = useState(false)
  const themeLabel = theme === 'dark' ? t('nav.lightTheme') : t('nav.darkTheme')
  const asideRef = useRef<HTMLElement>(null)
  const [suppressHover, setSuppressHover] = useState(false)

  useEffect(() => {
    if (!mobileOpen) {
      return
    }

    const aside = asideRef.current
    const previousFocus = document.activeElement
    const focusable = aside
      ? Array.from(
          aside.querySelectorAll<HTMLElement>(
            'button:not(:disabled), a[href]',
          ),
        )
      : []
    focusable[0]?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !aside || focusable.length === 0) {
        return
      }
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      if (event.shiftKey && active === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      if (previousFocus instanceof HTMLElement) {
        previousFocus.focus()
      }
    }
  }, [mobileOpen])

  return (
    <>
      <div
        className={`rail-scrim${mobileOpen ? ' is-visible' : ''}`}
        aria-hidden="true"
        onClick={onMobileClose}
      />
      <aside
        ref={asideRef}
        className={`rail${pinned ? ' is-pinned' : ''}${mobileOpen ? ' is-mobile-open' : ''}${languageOpen ? ' is-language-open' : ''}${suppressHover ? ' is-suppressed' : ''}`}
        aria-label={t('nav.mainNavigation')}
        onMouseEnter={() => setSuppressHover(false)}
        onMouseLeave={() => setSuppressHover(false)}
        onFocusCapture={() => setSuppressHover(false)}
      >
        <div className="rail-brand">
          <button
            className="rail-brand-button"
            type="button"
            onClick={(event) => {
              onHome()
              onMobileClose()
              if (mobileOpen) {
                event.currentTarget.blur()
              } else if (!pinned) {
                setSuppressHover(true)
                event.currentTarget.blur()
              }
            }}
            aria-label={t('nav.backToSearch')}
          >
            <span className="brand-mark" aria-hidden="true">
              A
            </span>
            <span className="rail-brand-copy">
              <span className="brand-name">AMZ CLI</span>
              <span className="brand-sub">{t('nav.brandSubtitle')}</span>
            </span>
          </button>
          <button
            className="rail-pin"
            type="button"
            onClick={(event) => {
              const wasPinned = pinned
              onTogglePin()
              if (wasPinned && event.detail !== 0) {
                event.currentTarget.blur()
              }
            }}
            aria-pressed={pinned}
            aria-label={pinned ? t('nav.unpin') : t('nav.pinMenu')}
            title={pinned ? t('nav.unpin') : t('nav.pinMenu')}
          >
            <Icon name="pin" />
            <span>{t('nav.pin')}</span>
          </button>
        </div>

        <nav className="rail-nav">
          {groups.map((group) => (
            <div className="rail-group" key={group.title}>
              <p className="rail-group-title">{t(group.title)}</p>
              {group.items.map((item) => {
                if (!item.action) {
                  return (
                    <div
                      className="rail-item is-future"
                      key={item.label}
                      aria-disabled="true"
                      title={t('nav.comingSoon')}
                    >
                      <span className="rail-icon">
                        <Icon name={item.icon} />
                      </span>
                      <span className="rail-label">{t(item.label)}</span>
                      <span className="rail-soon">{t('nav.soon')}</span>
                    </div>
                  )
                }

                const active =
                  item.action === 'deals'
                    ? view === 'deals'
                    : item.action === 'compare'
                      ? view === 'compare'
                      : item.action === 'saved'
                        ? view === 'saved'
                        : item.action === 'history'
                          ? view === 'history'
                          : item.action === 'monitoring'
                            ? view === 'monitoring'
                            : item.action === 'status'
                              ? view === 'status'
                              : item.action === 'settings'
                                ? view === 'settings'
                                : view === 'search' || view === 'detail'
                return (
                  <button
                    className={`rail-item${active ? ' active' : ''}`}
                    type="button"
                    key={item.label}
                    aria-current={active ? 'page' : undefined}
                    onClick={(event) => {
                      if (item.action === 'deals') {
                        onOpenDeals()
                      } else if (item.action === 'compare') {
                        onOpenCompare()
                      } else if (item.action === 'saved') {
                        onOpenSaved()
                      } else if (item.action === 'history') {
                        onOpenHistory()
                      } else if (item.action === 'monitoring') {
                        onOpenMonitoring()
                      } else if (item.action === 'status') {
                        onOpenStatus()
                      } else if (item.action === 'settings') {
                        onOpenSettings()
                      } else {
                        onHome()
                      }
                      onMobileClose()
                      if (mobileOpen) {
                        event.currentTarget.blur()
                      } else if (!pinned) {
                        setSuppressHover(true)
                        event.currentTarget.blur()
                      }
                    }}
                  >
                    <span className="rail-icon">
                      <Icon name={item.icon} />
                      {(item.action === 'saved' && savedBadge)
                      || (item.action === 'compare' && compareBadge)
                      || (item.action === 'status' && updateBadge) ? (
                        <span
                          className="rail-badge-dot"
                          aria-hidden="true"
                        />
                      ) : null}
                    </span>
                    <span className="rail-label">{t(item.label)}</span>
                  </button>
                )
              })}
            </div>
          ))}
        </nav>

        <div className="rail-footer">
          <div className="rail-version">v{meta.version}</div>
          <LanguagePicker
            languages={languages}
            pinned={pinned}
            onOpenChange={setLanguageOpen}
          />
          <button
            className="rail-item rail-control"
            type="button"
            onClick={(event) => {
              onToggleTheme()
              if (mobileOpen) {
                onMobileClose()
                event.currentTarget.blur()
              } else if (!pinned) {
                setSuppressHover(true)
                event.currentTarget.blur()
              }
            }}
            aria-label={themeLabel}
            title={themeLabel}
          >
            <span className="rail-icon">
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} />
            </span>
            <span className="rail-label">{themeLabel}</span>
          </button>
          <button
            className="rail-item rail-control"
            type="button"
            onClick={onRequestClose}
            aria-label={t('nav.closeInterface')}
          >
            <span className="rail-icon">
              <Icon name="close" />
            </span>
            <span className="rail-label">{t('nav.close')}</span>
          </button>
        </div>
      </aside>
    </>
  )
}
