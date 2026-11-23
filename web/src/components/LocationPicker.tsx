import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import { COUNTRY_CODES } from '../lib/countries'
import {
  isValidPostalCode,
  normalizePostalCode,
  postalCodePlaceholder,
} from '../lib/postal'
import { Icon } from './ui'

type MenuPosition = {
  left: number
  top: number
  width: number
}

type CountryOption = {
  code: string
  label: string
  searchText: string
}

export function LocationPicker({
  country,
  postalCode,
  domesticCountry,
  countryOnlyDelivery,
  onApply,
}: {
  country: string
  postalCode: string
  domesticCountry: string
  countryOnlyDelivery: boolean
  onApply: (country: string, postalCode: string) => void
}) {
  const { t, languageLocale } = useI18n()
  const [open, setOpen] = useState(false)
  const [draftCountry, setDraftCountry] = useState(country)
  const [draftPostal, setDraftPostal] = useState(postalCode)
  const [countryQuery, setCountryQuery] = useState('')
  const [countryListOpen, setCountryListOpen] = useState(false)
  const [activeCountry, setActiveCountry] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [position, setPosition] = useState<MenuPosition | null>(null)
  const [isMobile, setIsMobile] = useState(
    () =>
      typeof window !== 'undefined' &&
      window.matchMedia('(max-width: 820px)').matches,
  )
  const triggerRef = useRef<HTMLButtonElement>(null)
  const postalInputRef = useRef<HTMLInputElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const countryBoxRef = useRef<HTMLDivElement>(null)

  const countryNames = useMemo(() => {
    try {
      return new Intl.DisplayNames([languageLocale], { type: 'region' })
    } catch {
      return null
    }
  }, [languageLocale])

  const italianCountryNames = useMemo(() => {
    try {
      return new Intl.DisplayNames(['it'], { type: 'region' })
    } catch {
      return null
    }
  }, [])

  const allCountries = useMemo<CountryOption[]>(
    () =>
      COUNTRY_CODES.map((code) => {
        const label = countryNames?.of(code) ?? code
        const italianLabel = italianCountryNames?.of(code) ?? ''
        return {
          code,
          label,
          searchText: `${label} ${italianLabel} ${code}`,
        }
      }).sort((left, right) =>
        left.label.localeCompare(right.label, languageLocale, {
          sensitivity: 'base',
        }),
      ),
    [countryNames, italianCountryNames, languageLocale],
  )

  const countryLabel = (code: string) =>
    code.length === 0
      ? t('location.automatic')
      : allCountries.find((item) => item.code === code)?.label ?? code

  const display =
    country.trim().length > 0
      ? postalCode.trim().length > 0
        ? `${countryLabel(country)} · ${postalCode}`
        : countryLabel(country)
      : t('location.automaticShort')

  const normalizedQuery = countryQuery
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase(languageLocale)
    .trim()
  const filteredCountries = useMemo(() => {
    if (normalizedQuery.length === 0) {
      return allCountries
    }
    const score = (item: CountryOption) => {
      const label = item.label
        .normalize('NFD')
        .replace(/\p{Diacritic}/gu, '')
        .toLocaleLowerCase(languageLocale)
      const code = item.code.toLocaleLowerCase()
      if (label === normalizedQuery || code === normalizedQuery) {
        return 0
      }
      if (
        label.startsWith(normalizedQuery) ||
        code.startsWith(normalizedQuery)
      ) {
        return 1
      }
      return 2
    }
    return allCountries
      .filter((item) => {
        const searchText = item.searchText
          .normalize('NFD')
          .replace(/\p{Diacritic}/gu, '')
          .toLocaleLowerCase(languageLocale)
        return searchText.includes(normalizedQuery)
      })
      .sort(
        (left, right) =>
          score(left) - score(right) ||
          left.label.localeCompare(right.label, languageLocale, {
            sensitivity: 'base',
          }),
      )
  }, [allCountries, languageLocale, normalizedQuery])

  const placePicker = () => {
    if (window.matchMedia('(max-width: 820px)').matches) {
      setPosition({
        left: 10,
        top: 10,
        width: Math.max(280, window.innerWidth - 20),
      })
      return
    }
    const trigger = triggerRef.current
    if (!trigger) {
      return
    }
    const rect = trigger.getBoundingClientRect()
    const width = Math.min(340, window.innerWidth - 20)
    setPosition({
      left: Math.max(10, Math.min(rect.left, window.innerWidth - width - 10)),
      top: rect.bottom + 8,
      width,
    })
  }

  const openPicker = () => {
    setDraftCountry(country)
    setDraftPostal(postalCode)
    setCountryQuery(country.length > 0 ? countryLabel(country) : '')
    setCountryListOpen(false)
    setActiveCountry(0)
    setError(null)
    placePicker()
    setOpen(true)
  }

  const closePicker = () => {
    setOpen(false)
    setCountryListOpen(false)
  }

  const selectCountry = (code: string) => {
    setDraftCountry(code)
    setCountryQuery(code.length > 0 ? countryLabel(code) : '')
    setCountryListOpen(false)
    setError(null)
    if (code !== draftCountry) {
      setDraftPostal('')
    }
    const nextCountryOnly =
      countryOnlyDelivery && code.length > 0 && code !== domesticCountry
    if (code.length > 0 && !nextCountryOnly) {
      window.requestAnimationFrame(() => postalInputRef.current?.focus())
    }
  }

  const apply = () => {
    if (draftCountry.length === 0) {
      if (draftPostal.trim().length === 0) {
        onApply('', '')
        closePicker()
        return
      }
      setError(t('location.countryRequired'))
      return
    }

    const foreignCountryOnly =
      countryOnlyDelivery &&
      draftCountry.length > 0 &&
      draftCountry !== domesticCountry
    if (foreignCountryOnly) {
      onApply(draftCountry, '')
      closePicker()
      return
    }

    const normalized = normalizePostalCode(draftCountry, draftPostal)
    if (normalized.length === 0) {
      setError(t('location.postalRequired'))
      return
    }
    if (!isValidPostalCode(draftCountry, normalized)) {
      setError(
        t('location.invalid', {
          country: countryLabel(draftCountry),
        }),
      )
      return
    }

    onApply(draftCountry, normalized)
    closePicker()
  }

  const onCountryKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      if (countryListOpen) {
        event.preventDefault()
        event.stopPropagation()
        setCountryListOpen(false)
        return
      }
    }
    if (event.key === 'Tab') {
      setCountryListOpen(false)
      return
    }
    if (
      event.key !== 'ArrowDown' &&
      event.key !== 'ArrowUp' &&
      event.key !== 'Home' &&
      event.key !== 'End' &&
      event.key !== 'Enter'
    ) {
      return
    }
    if (!countryListOpen) {
      setCountryListOpen(true)
      return
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveCountry((current) => {
        const direction = event.key === 'ArrowDown' ? 1 : -1
        const next = current + direction
        if (next < 0) {
          return filteredCountries.length - 1
        }
        if (next >= filteredCountries.length) {
          return 0
        }
        return next
      })
      return
    }
    if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault()
      setActiveCountry(
        event.key === 'Home' ? 0 : Math.max(0, filteredCountries.length - 1),
      )
      return
    }
    if (event.key === 'Enter') {
      event.preventDefault()
      const option = filteredCountries[activeCountry]
      if (option) {
        selectCountry(option.code)
      }
    }
  }

  useEffect(() => {
    const query = window.matchMedia('(max-width: 820px)')
    const update = () => setIsMobile(query.matches)
    update()
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])

  useEffect(() => {
    if (!open) {
      return
    }

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node | null
      if (
        target &&
        (triggerRef.current?.contains(target) ||
          countryBoxRef.current?.contains(target))
      ) {
        return
      }
      if (target && panelRef.current?.contains(target)) {
        // Clicking anywhere else in the popover collapses the country list
        // without closing the whole dialog.
        setCountryListOpen(false)
        return
      }
      closePicker()
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        closePicker()
        triggerRef.current?.focus()
      }
    }
    const onResize = () => {
      if (window.matchMedia('(max-width: 820px)').matches) {
        setPosition({
          left: 10,
          top: 10,
          width: Math.max(280, window.innerWidth - 20),
        })
        return
      }
      placePicker()
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('resize', onResize)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('resize', onResize)
    }
  }, [open])

  return (
    <>
      <button
        ref={triggerRef}
        className="search-location"
        type="button"
        onClick={() => (open ? closePicker() : openPicker())}
        title={t('location.change')}
        aria-label={t('location.change')}
        aria-haspopup="dialog"
        aria-expanded={open}
      >
        <Icon name="map-pin" />
        <span>{display}</span>
      </button>

      {open && position
        ? createPortal(
            <div
              ref={panelRef}
              className={`location-popover${
                isMobile ? ' is-mobile' : ''
              }`}
              style={{
                left: position.left,
                top: position.top,
                width: position.width,
              }}
              role="dialog"
              aria-modal="false"
              aria-label={t('location.title')}
            >
              <p className="location-popover-title">{t('location.title')}</p>

              <label className="location-field">
                <span>{t('location.country')}</span>
                <div className="location-combobox" ref={countryBoxRef}>
                  <input
                    type="search"
                    role="combobox"
                    aria-expanded={countryListOpen}
                    aria-controls="location-country-list"
                    aria-autocomplete="list"
                    value={countryQuery}
                    placeholder={t('location.searchCountry')}
                    autoComplete="off"
                    autoFocus
                    onFocus={(event) => {
                      event.currentTarget.select()
                    }}
                    onChange={(event) => {
                      const value = event.target.value
                      setCountryQuery(value)
                      setDraftCountry('')
                      setDraftPostal('')
                      setCountryListOpen(value.trim().length > 0)
                      setActiveCountry(0)
                      setError(null)
                    }}
                    onKeyDown={onCountryKeyDown}
                  />
                  <Icon
                    name="chevron-down"
                    className="location-combobox-chevron"
                  />
                  {countryListOpen ? (
                    <ul
                      className="location-country-list"
                      id="location-country-list"
                      role="listbox"
                    >
                      {filteredCountries.length === 0 ? (
                        <li className="location-no-results">
                          {t('location.noCountries')}
                        </li>
                      ) : (
                        filteredCountries.map((option, index) => (
                          <li key={option.code}>
                            <button
                              className={`location-country-option${option.code === draftCountry ? ' is-selected' : ''}${index === activeCountry ? ' is-active' : ''}`}
                              type="button"
                              role="option"
                              aria-selected={option.code === draftCountry}
                              onMouseDown={(event) =>
                                event.preventDefault()
                              }
                              onClick={() => selectCountry(option.code)}
                              onMouseEnter={() => setActiveCountry(index)}
                            >
                              <span>{option.label}</span>
                              <span className="location-country-code">
                                {option.code}
                              </span>
                            </button>
                          </li>
                        ))
                      )}
                    </ul>
                  ) : null}
                </div>
              </label>

              {countryOnlyDelivery &&
              draftCountry.length > 0 &&
              draftCountry !== domesticCountry ? (
                <p className="location-hint">
                  {t('location.countryOnlyHint')}
                </p>
              ) : (
                <>
                  <label className="location-field">
                    <span>
                      {draftCountry === 'US'
                        ? t('location.zipCode')
                        : t('location.postalCode')}
                    </span>
                    <input
                      ref={postalInputRef}
                      type="text"
                      value={draftPostal}
                      onChange={(event) => {
                        setDraftPostal(event.target.value)
                        setError(null)
                      }}
                      placeholder={postalCodePlaceholder(draftCountry)}
                      autoComplete="postal-code"
                      disabled={draftCountry.length === 0}
                      onFocus={(event) =>
                        event.currentTarget.scrollIntoView({
                          block: 'nearest',
                        })
                      }
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          event.preventDefault()
                          apply()
                        }
                      }}
                    />
                  </label>

                  <p className="location-hint">{t('location.hint')}</p>
                </>
              )}
              {error ? (
                <p className="location-error" role="alert">
                  {error}
                </p>
              ) : null}

              <div className="location-actions">
                <button
                  className="text-button"
                  type="button"
                  onClick={() => {
                    onApply('', '')
                    closePicker()
                  }}
                >
                  {t('location.reset')}
                </button>
                <button
                  className="text-button"
                  type="button"
                  onClick={closePicker}
                >
                  {t('location.cancel')}
                </button>
                <button
                  className="primary-button location-apply"
                  type="button"
                  onClick={apply}
                >
                  {t('location.apply')}
                </button>
              </div>
            </div>,
            document.body,
          )
        : null}
    </>
  )
}
