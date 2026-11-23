import {
  useEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import {
  languageNames,
  type LanguageCode,
} from '../i18n/types'
import { Icon } from './ui'

const LANGUAGE_MENU_ID = 'language-menu'

type MenuPosition = {
  left: number
  bottom: number
  width: number
}

export function LanguagePicker({
  languages,
  pinned,
  onOpenChange,
}: {
  languages: LanguageCode[]
  pinned: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { language, setLanguage, t } = useI18n()
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const [position, setPosition] = useState<MenuPosition | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([])
  const selectedIndex = Math.max(0, languages.indexOf(language))

  const placeMenu = () => {
    const button = buttonRef.current
    if (!button) {
      return
    }
    const rect = button.getBoundingClientRect()
    const width = 210
    const left = Math.max(
      10,
      Math.min(rect.left, window.innerWidth - width - 10),
    )
    setPosition({
      left,
      bottom: window.innerHeight - rect.top + 8,
      width,
    })
  }

  const openMenu = () => {
    placeMenu()
    setActiveIndex(selectedIndex)
    setOpen(true)
  }

  const closeMenu = (returnFocus = false) => {
    setOpen(false)
    if (returnFocus) {
      window.requestAnimationFrame(() => buttonRef.current?.focus())
      return
    }
    const active = document.activeElement
    if (
      !pinned &&
      active instanceof HTMLElement &&
      (rootRef.current?.contains(active) || menuRef.current?.contains(active))
    ) {
      active.blur()
    }
  }

  const choose = (code: LanguageCode, restoreFocus = false) => {
    setLanguage(code)
    closeMenu(restoreFocus)
  }

  useEffect(() => {
    onOpenChange(open)
  }, [onOpenChange, open])

  useEffect(() => {
    if (!open) {
      return
    }

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node | null
      if (
        target &&
        (rootRef.current?.contains(target) ||
          menuRef.current?.contains(target))
      ) {
        return
      }
      closeMenu()
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        closeMenu(true)
      }
    }
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target as Node | null
      if (
        target &&
        (rootRef.current?.contains(target) ||
          menuRef.current?.contains(target))
      ) {
        return
      }
      closeMenu()
    }
    const onResize = () => closeMenu()

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('focusin', onFocusIn)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('resize', onResize)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('focusin', onFocusIn)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('resize', onResize)
    }
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }
    const frame = window.requestAnimationFrame(() => {
      optionRefs.current[activeIndex]?.focus()
    })
    return () => window.cancelAnimationFrame(frame)
  }, [activeIndex, open])

  const onMenuKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      const direction = event.key === 'ArrowDown' ? 1 : -1
      setActiveIndex((current) => {
        const next = current + direction
        if (next < 0) {
          return languages.length - 1
        }
        if (next >= languages.length) {
          return 0
        }
        return next
      })
      return
    }
    if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault()
      setActiveIndex(event.key === 'Home' ? 0 : languages.length - 1)
      return
    }
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      const code = languages[activeIndex]
      if (code) {
        choose(code, true)
      }
    }
  }

  return (
    <div className="rail-language" ref={rootRef}>
      <button
        ref={buttonRef}
        className="rail-item rail-control rail-language-button"
        type="button"
        title={t('nav.language')}
        aria-label={t('nav.language')}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? LANGUAGE_MENU_ID : undefined}
        onClick={(event) =>
          open
            ? closeMenu(pinned || event.detail === 0)
            : openMenu()
        }
        onKeyDown={(event) => {
          if (!open && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
            event.preventDefault()
            openMenu()
          }
        }}
      >
        <span className="rail-icon">
          <Icon name="globe" />
        </span>
        <span className="rail-label">{languageNames[language]}</span>
        <Icon name="chevron-down" className="rail-language-chevron" />
      </button>

      {open && position
        ? createPortal(
            <div
              id={LANGUAGE_MENU_ID}
              ref={menuRef}
              className="language-popover"
              style={{
                left: position.left,
                bottom: position.bottom,
                width: position.width,
              }}
              role="listbox"
              aria-label={t('nav.language')}
              onKeyDown={onMenuKeyDown}
            >
              <p className="language-popover-title">{t('nav.language')}</p>
              {languages.map((code, index) => {
                const selected = code === language
                return (
                  <button
                    key={code}
                    ref={(element) => {
                      optionRefs.current[index] = element
                    }}
                    className={`language-option${selected ? ' is-selected' : ''}${index === activeIndex ? ' is-active' : ''}`}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    tabIndex={-1}
                    onClick={() => choose(code)}
                    onMouseEnter={() => setActiveIndex(index)}
                  >
                    <span>{languageNames[code]}</span>
                    {selected ? <Icon name="check" /> : null}
                  </button>
                )
              })}
            </div>,
            document.body,
          )
        : null}
    </div>
  )
}
