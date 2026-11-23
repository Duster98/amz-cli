import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type {
  HttpSettings,
  HttpSettingsDiagnostics,
  Library,
  Marketplace,
  Meta,
  ThemePreference,
  UpdateStatus,
} from '../lib/api'
import {
  clearHttpCooldown,
  defaultHttpSettings,
  getHttpSettings,
  saveHttpSettings,
} from '../lib/api'
import type { LanguageCode } from '../i18n/types'
import { languageNames } from '../i18n/types'
import { useI18n } from '../i18n/I18nProvider'
import {
  defaultSearchFilters,
  sortOptions,
  type SearchFilterState,
} from '../lib/searchFilters'
import type { ResultsLayout } from './ResultsList'
import { LocationPicker } from './LocationPicker'
import { Icon, Tag, type IconName } from './ui'

type SettingsSectionId =
  | 'appearance'
  | 'search'
  | 'delivery'
  | 'library'
  | 'system'
  | 'advanced'

type SettingsSection = {
  id: SettingsSectionId
  label: string
  icon: IconName
}

function formatDate(
  value: string | null | undefined,
  locale: string,
): string {
  if (!value) {
    return '—'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function SettingRow({
  label,
  help,
  control,
  block = false,
}: {
  label: string
  help?: string
  control: ReactNode
  block?: boolean
}) {
  return (
    <div className={`settings-row${block ? ' is-block' : ''}`}>
      <div className="settings-row-label">
        <strong>{label}</strong>
        {help ? <span>{help}</span> : null}
      </div>
      <div className="settings-row-control">{control}</div>
    </div>
  )
}

function Segmented({
  value,
  options,
  onChange,
  label,
  disabled = false,
}: {
  value: string
  options: Array<{ value: string; label: string }>
  onChange: (value: string) => void
  label: string
  disabled?: boolean
}) {
  return (
    <div className="settings-segmented" role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={value === option.value ? 'is-active' : ''}
          aria-pressed={value === option.value}
          disabled={disabled}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}

function SettingsSwitch({
  checked,
  onChange,
  label,
  disabled = false,
}: {
  checked: boolean
  onChange: (checked: boolean) => void
  label: string
  disabled?: boolean
}) {
  return (
    <button
      className={`settings-switch${checked ? ' is-on' : ''}`}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      <span aria-hidden="true" />
    </button>
  )
}

function SettingsSelect({
  value,
  label,
  options,
  onChange,
  disabled = false,
}: {
  value: string
  label: string
  options: Array<{ value: string; label: string }>
  onChange: (value: string) => void
  disabled?: boolean
}) {
  return (
    <select
      className="settings-select"
      value={value}
      aria-label={label}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    >
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  )
}

function SettingsInput({
  value,
  label,
  placeholder,
  onChange,
  disabled = false,
  wide = false,
  autoComplete = 'off',
}: {
  value: string
  label: string
  placeholder?: string
  onChange: (value: string) => void
  disabled?: boolean
  wide?: boolean
  autoComplete?: string
}) {
  return (
    <input
      className={`settings-input${wide ? ' is-wide' : ''}`}
      type="text"
      value={value}
      aria-label={label}
      placeholder={placeholder}
      disabled={disabled}
      autoComplete={autoComplete}
      spellCheck={false}
      onChange={(event) => onChange(event.target.value)}
    />
  )
}

function SettingsNumberField({
  value,
  label,
  min,
  max,
  suffix,
  onCommit,
  disabled = false,
}: {
  value: number
  label: string
  min: number
  max: number
  suffix: string
  onCommit: (value: number) => void
  disabled?: boolean
}) {
  const [draft, setDraft] = useState(String(value))

  useEffect(() => {
    setDraft(String(value))
  }, [value])

  const commit = () => {
    const parsed = Number(draft)
    const next = Number.isFinite(parsed)
      ? Math.min(max, Math.max(min, parsed))
      : min
    setDraft(String(next))
    onCommit(next)
  }

  return (
    <label className="settings-input settings-input--number">
      <input
        type="number"
        inputMode="decimal"
        value={draft}
        min={min}
        max={max}
        aria-label={label}
        disabled={disabled}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.currentTarget.blur()
          }
        }}
      />
      <span>{suffix}</span>
    </label>
  )
}

export function SettingsView({
  meta,
  themePreference,
  onThemePreferenceChange,
  language,
  languages,
  onLanguageChange,
  railPinned,
  onRailPinnedChange,
  resultsLayout,
  onResultsLayoutChange,
  monitoringLayout,
  onMonitoringLayoutChange,
  marketplaces,
  domains,
  onDomainsChange,
  onDomainToggle,
  dealDomain,
  onDealDomainChange,
  filters,
  onFiltersChange,
  deliveryCountry,
  deliveryPostalCode,
  onDeliveryLocationChange,
  library,
  historyCount,
  updateStatus,
  updateChecking,
  onCheckUpdates,
  onOpenStatus,
  onOpenMonitoring,
  onNetworkSettingsChange,
}: {
  meta: Meta
  themePreference: ThemePreference
  onThemePreferenceChange: (theme: ThemePreference) => void
  language: LanguageCode
  languages: LanguageCode[]
  onLanguageChange: (language: LanguageCode) => void
  railPinned: boolean
  onRailPinnedChange: (pinned: boolean) => void
  resultsLayout: ResultsLayout
  onResultsLayoutChange: (layout: ResultsLayout) => void
  monitoringLayout: ResultsLayout
  onMonitoringLayoutChange: (layout: ResultsLayout) => void
  marketplaces: Marketplace[]
  domains: string[]
  onDomainsChange: (domains: string[]) => void
  onDomainToggle: (domain: string) => void
  dealDomain: string
  onDealDomainChange: (domain: string) => void
  filters: SearchFilterState
  onFiltersChange: (patch: Partial<SearchFilterState>) => void
  deliveryCountry: string
  deliveryPostalCode: string
  onDeliveryLocationChange: (country: string, postalCode: string) => void
  library: Library
  historyCount: number
  updateStatus: UpdateStatus | null
  updateChecking: boolean
  onCheckUpdates: () => void
  onOpenStatus: () => void
  onOpenMonitoring: () => void
  onNetworkSettingsChange?: () => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const [activeSection, setActiveSection] =
    useState<SettingsSectionId>('appearance')
  const [httpSettings, setHttpSettings] =
    useState<HttpSettings>(defaultHttpSettings)
  const [httpDiagnostics, setHttpDiagnostics] =
    useState<HttpSettingsDiagnostics | null>(null)
  const [httpLoaded, setHttpLoaded] = useState(false)
  const [httpLoading, setHttpLoading] = useState(true)
  const [httpSaving, setHttpSaving] = useState(false)
  const [httpError, setHttpError] = useState('')
  const [clearingCooldown, setClearingCooldown] = useState<string | null>(null)
  const lastSavedHttpRef = useRef('')

  const loadHttpDiagnostics = useCallback(
    async (preserveDraft = false) => {
      setHttpLoading(true)
      try {
        const next = await getHttpSettings()
        setHttpDiagnostics(next)
        if (!preserveDraft) {
          lastSavedHttpRef.current = JSON.stringify(next.settings)
          setHttpSettings(next.settings)
          setHttpLoaded(true)
        }
        setHttpError('')
      } catch (error) {
        setHttpError(
          `${t(
            preserveDraft
              ? 'settings.advancedSaveError'
              : 'settings.advancedUnavailable',
          )} ${errorMessage(error)}`,
        )
      } finally {
        setHttpLoading(false)
      }
    },
    [t],
  )

  useEffect(() => {
    void loadHttpDiagnostics()
  }, [loadHttpDiagnostics])

  useEffect(() => {
    if (!httpLoaded) {
      return
    }
    const serialized = JSON.stringify(httpSettings)
    if (serialized === lastSavedHttpRef.current) {
      return
    }

    const timer = window.setTimeout(() => {
      setHttpSaving(true)
      void saveHttpSettings(httpSettings)
        .then((next) => {
          lastSavedHttpRef.current = serialized
          setHttpDiagnostics(next)
          setHttpError('')
          onNetworkSettingsChange?.()
        })
        .catch((error) => {
          setHttpError(
            `${t('settings.advancedSaveError')} ${errorMessage(error)}`,
          )
        })
        .finally(() => {
          setHttpSaving(false)
        })
    }, 450)
    return () => window.clearTimeout(timer)
  }, [
    httpSettings,
    httpLoaded,
    onNetworkSettingsChange,
    t,
  ])

  const patchHttpSettings = (patch: Partial<HttpSettings>) => {
    setHttpSettings((current) => ({ ...current, ...patch }))
  }

  const clearCooldown = async (domain: string) => {
    setClearingCooldown(domain)
    try {
      const next = await clearHttpCooldown(domain)
      setHttpDiagnostics(next)
      setHttpError('')
    } catch (error) {
      setHttpError(
        `${t('settings.advancedSaveError')} ${errorMessage(error)}`,
      )
    } finally {
      setClearingCooldown(null)
    }
  }

  const sections = useMemo<SettingsSection[]>(
    () => [
      {
        id: 'appearance',
        label: t('settings.appearanceTitle'),
        icon: 'sun',
      },
      {
        id: 'search',
        label: t('settings.searchTitle'),
        icon: 'search',
      },
      {
        id: 'delivery',
        label: t('settings.deliveryTitle'),
        icon: 'map-pin',
      },
      {
        id: 'library',
        label: t('settings.libraryTitle'),
        icon: 'bookmark',
      },
      {
        id: 'system',
        label: t('settings.systemTitle'),
        icon: 'settings',
      },
      {
        id: 'advanced',
        label: t('settings.advancedTitle'),
        icon: 'sliders',
      },
    ],
    [t],
  )

  useEffect(() => {
    const elements = Array.from(
      document.querySelectorAll<HTMLElement>('[data-settings-section]'),
    )
    if (elements.length === 0 || typeof IntersectionObserver === 'undefined') {
      return
    }
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort(
            (left, right) =>
              left.boundingClientRect.top - right.boundingClientRect.top,
          )[0]
        if (visible) {
          const id = visible.target.getAttribute('data-settings-section')
          if (id) {
            setActiveSection(id as SettingsSectionId)
          }
        }
      },
      { rootMargin: '-100px 0px -62% 0px', threshold: [0, 1] },
    )
    elements.forEach((element) => observer.observe(element))
    return () => observer.disconnect()
  }, [])

  const jumpTo = (id: SettingsSectionId) => {
    setActiveSection(id)
    document
      .getElementById(`settings-${id}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  const primaryDomain = domains[0] ?? marketplaces[0]?.id ?? ''
  const primaryMarketplace = marketplaces.find(
    (marketplace) => marketplace.id === primaryDomain,
  )
  const currency = primaryMarketplace?.currency ?? 'EUR'
  const dealMarketplace = marketplaces.some(
    (marketplace) => marketplace.id === dealDomain,
  )
    ? dealDomain
    : primaryDomain

  const formatMinutes = (value: number) => {
    if (value >= 1440 && value % 1440 === 0) {
      const count = value / 1440
      return t(count === 1 ? 'settings.dayOne' : 'settings.days', {
        count: formatNumber(count),
      })
    }
    if (value >= 60 && value % 60 === 0) {
      const count = value / 60
      return t(count === 1 ? 'settings.hourOne' : 'settings.hours', {
        count: formatNumber(count),
      })
    }
    return t('settings.minutes', { count: formatNumber(value) })
  }

  const formatSeconds = (value: number) => {
    if (value >= 3600 && value % 3600 === 0) {
      const count = value / 3600
      return t(count === 1 ? 'settings.hourOne' : 'settings.hours', {
        count: formatNumber(count),
      })
    }
    if (value >= 60 && value % 60 === 0) {
      return t('settings.minutes', { count: formatNumber(value / 60) })
    }
    return t('settings.seconds', { count: formatNumber(value) })
  }

  const libraryPolicy = meta.library
  const currentVersion = updateStatus?.current_version ?? meta.version
  const channel = updateStatus?.channel ?? meta.channel
  const latestVersion = updateStatus?.check?.latest_version ?? '—'
  const lastCheck = updateStatus?.auto_state?.last_check
  const autoUpdate = updateStatus?.auto_update

  const browserOptions = useMemo(() => {
    const profiles = httpDiagnostics?.profiles ?? []
    const options = [
      { value: 'auto', label: t('settings.advancedAuto') },
      ...profiles.map((profile) => ({ value: profile, label: profile })),
    ]
    if (
      httpSettings.browser !== 'auto' &&
      !profiles.includes(httpSettings.browser)
    ) {
      options.push({
        value: httpSettings.browser,
        label: httpSettings.browser,
      })
    }
    return options
  }, [httpDiagnostics?.profiles, httpSettings.browser, t])

  return (
    <section className="settings-view">
      <header className="settings-header">
        <div>
          <h1>{t('nav.settings')}</h1>
          <p>{t('settings.subtitle')}</p>
        </div>
        <Tag tone="quiet">{t('settings.autoSaved')}</Tag>
      </header>

      <div className="settings-shell">
        <nav
          className="settings-index"
          aria-label={t('settings.sectionsLabel')}
        >
          {sections.map((section) => (
            <button
              key={section.id}
              type="button"
              className={activeSection === section.id ? 'is-active' : ''}
              onClick={() => jumpTo(section.id)}
            >
              <span className="settings-index-label">
                <Icon name={section.icon} />
                {section.label}
              </span>
              <Icon name="arrow-right" />
            </button>
          ))}
        </nav>

        <div className="settings-content">
          <section
            id="settings-appearance"
            data-settings-section="appearance"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="sun" />
              </span>
              <div>
                <h2>{t('settings.appearanceTitle')}</h2>
                <p>{t('settings.appearanceBody')}</p>
              </div>
            </header>
            <div className="settings-rows">
              <SettingRow
                label={t('settings.theme')}
                control={
                  <Segmented
                    label={t('settings.theme')}
                    value={themePreference}
                    options={[
                      {
                        value: 'system',
                        label: t('settings.themeSystem'),
                      },
                      {
                        value: 'light',
                        label: t('settings.themeLight'),
                      },
                      {
                        value: 'dark',
                        label: t('settings.themeDark'),
                      },
                    ]}
                    onChange={(value) =>
                      onThemePreferenceChange(
                        value as ThemePreference,
                      )
                    }
                  />
                }
              />
              <SettingRow
                label={t('language.label')}
                control={
                  <SettingsSelect
                    value={language}
                    label={t('language.label')}
                    options={languages.map((code) => ({
                      value: code,
                      label: languageNames[code],
                    }))}
                    onChange={(value) =>
                      onLanguageChange(value as LanguageCode)
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.sidebarPinned')}
                control={
                  <SettingsSwitch
                    checked={railPinned}
                    label={t('settings.sidebarPinned')}
                    onChange={onRailPinnedChange}
                  />
                }
              />
              <SettingRow
                label={t('settings.resultsLayout')}
                control={
                  <Segmented
                    label={t('settings.resultsLayout')}
                    value={resultsLayout}
                    options={[
                      {
                        value: 'list',
                        label: t('saved.listView'),
                      },
                      {
                        value: 'grid',
                        label: t('saved.gridView'),
                      },
                    ]}
                    onChange={(value) =>
                      onResultsLayoutChange(value as ResultsLayout)
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.monitoringLayout')}
                control={
                  <Segmented
                    label={t('settings.monitoringLayout')}
                    value={monitoringLayout}
                    options={[
                      {
                        value: 'list',
                        label: t('saved.listView'),
                      },
                      {
                        value: 'grid',
                        label: t('saved.gridView'),
                      },
                    ]}
                    onChange={(value) =>
                      onMonitoringLayoutChange(value as ResultsLayout)
                    }
                  />
                }
              />
            </div>
          </section>

          <section
            id="settings-search"
            data-settings-section="search"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="search" />
              </span>
              <div>
                <h2>{t('settings.searchTitle')}</h2>
                <p>{t('settings.searchBody')}</p>
              </div>
            </header>
            <div className="settings-rows">
              <SettingRow
                label={t('settings.primaryMarketplace')}
                help={t('search.marketplaceHelp')}
                control={
                  <SettingsSelect
                    value={primaryDomain}
                    label={t('settings.primaryMarketplace')}
                    options={marketplaces.map((marketplace) => ({
                      value: marketplace.id,
                      label: marketplace.display_name,
                    }))}
                    onChange={(value) =>
                      onDomainsChange([
                        value,
                        ...domains.filter((domain) => domain !== value),
                      ])
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.activeMarketplaces')}
                help={t('search.searchingMarketplaces', {
                  count: formatNumber(domains.length),
                })}
                block
                control={
                  <div className="settings-marketplaces">
                    {marketplaces.map((marketplace) => {
                      const selected = domains.includes(marketplace.id)
                      const primary = marketplace.id === primaryDomain
                      return (
                        <label
                          key={marketplace.id}
                          className={`settings-marketplace${
                            selected ? ' is-selected' : ''
                          }`}
                        >
                          <input
                            type="checkbox"
                            checked={selected}
                            disabled={
                              selected && (primary || domains.length === 1)
                            }
                            onChange={() => onDomainToggle(marketplace.id)}
                          />
                          <span>{marketplace.display_name}</span>
                          {primary ? (
                            <span className="settings-marketplace-role">
                              {t('settings.primary')}
                            </span>
                          ) : null}
                        </label>
                      )
                    })}
                  </div>
                }
              />
              <SettingRow
                label={t('settings.dealsMarketplace')}
                control={
                  <SettingsSelect
                    value={dealMarketplace}
                    label={t('settings.dealsMarketplace')}
                    options={marketplaces.map((marketplace) => ({
                      value: marketplace.id,
                      label: marketplace.display_name,
                    }))}
                    onChange={onDealDomainChange}
                  />
                }
              />
              <SettingRow
                label={t('search.sortBy')}
                control={
                  <SettingsSelect
                    value={filters.sort}
                    label={t('search.sortBy')}
                    options={sortOptions.map((option) => ({
                      value: option.value,
                      label: t(option.labelKey),
                    }))}
                    onChange={(value) =>
                      onFiltersChange({
                        sort: value as SearchFilterState['sort'],
                      })
                    }
                  />
                }
              />
              <SettingRow
                label={t('search.minRating')}
                control={
                  <Segmented
                    label={t('search.minRating')}
                    value={String(filters.minRating)}
                    options={[
                      { value: '0', label: t('search.all') },
                      { value: '3', label: '3+' },
                      { value: '4', label: '4+' },
                      { value: '4.5', label: '4.5+' },
                    ]}
                    onChange={(value) =>
                      onFiltersChange({ minRating: Number(value) })
                    }
                  />
                }
              />
              <SettingRow
                label={t('search.filterResults')}
                block
                control={
                  <div className="settings-price">
                    <label>
                      <span>{t('search.minPrice')}</span>
                      <input
                        type="text"
                        inputMode="decimal"
                        value={filters.priceMin}
                        onChange={(event) =>
                          onFiltersChange({
                            priceMin: event.target.value,
                          })
                        }
                        placeholder="—"
                        aria-label={t('search.minPrice')}
                      />
                      <span>{currency}</span>
                    </label>
                    <label>
                      <span>{t('search.maxPrice')}</span>
                      <input
                        type="text"
                        inputMode="decimal"
                        value={filters.priceMax}
                        onChange={(event) =>
                          onFiltersChange({
                            priceMax: event.target.value,
                          })
                        }
                        placeholder="—"
                        aria-label={t('search.maxPrice')}
                      />
                      <span>{currency}</span>
                    </label>
                  </div>
                }
              />
              {(
                [
                  {
                    key: 'noAds',
                    label: t('search.noSponsored'),
                    help: t('search.noSponsoredHelp'),
                  },
                  {
                    key: 'delivery',
                    label: t('search.deliveryEstimate'),
                    help: t('search.deliveryHelp'),
                  },
                  {
                    key: 'enrich',
                    label: t('search.referencePrices'),
                    help: t('search.referenceHelp'),
                  },
                  {
                    key: 'onlyDeals',
                    label: t('search.onSaleOnly'),
                    help: t('search.onSaleHelp'),
                  },
                  {
                    key: 'freeShipping',
                    label: t('search.freeShipping'),
                    help: t('search.freeShippingHelp'),
                  },
                  {
                    key: 'amazonChoice',
                    label: t('search.amazonChoice'),
                    help: t('search.amazonChoiceHelp'),
                  },
                ] as const
              ).map((item) => (
                <SettingRow
                  key={item.key}
                  label={item.label}
                  help={item.help}
                  control={
                    <SettingsSwitch
                      checked={filters[item.key]}
                      label={item.label}
                      onChange={(checked) =>
                        onFiltersChange({ [item.key]: checked })
                      }
                    />
                  }
                />
              ))}
            </div>
            <div className="settings-section-actions">
              <button
                className="settings-action"
                type="button"
                onClick={() => onFiltersChange({ ...defaultSearchFilters })}
              >
                <Icon name="refresh" />
                {t('search.reset')}
              </button>
            </div>
          </section>

          <section
            id="settings-delivery"
            data-settings-section="delivery"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="map-pin" />
              </span>
              <div>
                <h2>{t('settings.deliveryTitle')}</h2>
                <p>{t('settings.deliveryBody')}</p>
              </div>
            </header>
            <div className="settings-rows">
              <SettingRow
                label={t('location.title')}
                help={t('location.hint')}
                block
                control={
                  <LocationPicker
                    country={deliveryCountry}
                    postalCode={deliveryPostalCode}
                    domesticCountry={primaryMarketplace?.country ?? 'IT'}
                    countryOnlyDelivery={
                      primaryMarketplace?.country_only_delivery ?? false
                    }
                    onApply={onDeliveryLocationChange}
                  />
                }
              />
            </div>
          </section>

          <section
            id="settings-library"
            data-settings-section="library"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="bookmark" />
              </span>
              <div>
                <h2>{t('settings.libraryTitle')}</h2>
                <p>{t('settings.libraryBody')}</p>
              </div>
            </header>
            <dl className="settings-facts settings-facts--three">
              <div>
                <dt>{t('settings.savedProducts')}</dt>
                <dd>{formatNumber(library.products.length)}</dd>
              </div>
              <div>
                <dt>{t('settings.groups')}</dt>
                <dd>{formatNumber(library.groups.length)}</dd>
              </div>
              <div>
                <dt>{t('settings.historyEntries')}</dt>
                <dd>{formatNumber(historyCount)}</dd>
              </div>
            </dl>
            <div className="settings-rows">
              <SettingRow
                label={t('settings.savedRefresh')}
                control={
                  <span className="settings-value">
                    {formatMinutes(
                      libraryPolicy?.auto_refresh_after_minutes ?? 0,
                    )}
                  </span>
                }
              />
              <SettingRow
                label={t('settings.groupCooldown')}
                control={
                  <span className="settings-value">
                    {formatMinutes(
                      libraryPolicy?.group_refresh_cooldown_minutes ?? 0,
                    )}
                  </span>
                }
              />
              <SettingRow
                label={t('settings.groupDelay')}
                control={
                  <span className="settings-value">
                    {formatSeconds(
                      libraryPolicy?.group_refresh_delay_seconds ?? 0,
                    )}
                  </span>
                }
              />
            </div>
            <p className="settings-note">{t('settings.readOnlyCli')}</p>
            <div className="settings-section-actions">
              <button
                className="settings-action"
                type="button"
                onClick={onOpenMonitoring}
              >
                <Icon name="activity" />
                {t('settings.openMonitoring')}
              </button>
            </div>
          </section>

          <section
            id="settings-system"
            data-settings-section="system"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="settings" />
              </span>
              <div>
                <h2>{t('settings.systemTitle')}</h2>
                <p>{t('settings.systemBody')}</p>
              </div>
            </header>
            <dl className="settings-facts">
              <div>
                <dt>{t('status.version')}</dt>
                <dd className="settings-mono">{currentVersion}</dd>
              </div>
              <div>
                <dt>{t('status.channel')}</dt>
                <dd className="settings-mono">{channel}</dd>
              </div>
              <div>
                <dt>{t('status.platform')}</dt>
                <dd className="settings-mono">
                  {updateStatus?.platform ?? meta.platform}
                </dd>
              </div>
              <div>
                <dt>{t('status.webPort')}</dt>
                <dd className="settings-mono">
                  {formatNumber(meta.web.port)}
                </dd>
              </div>
              <div>
                <dt>{t('status.uptime')}</dt>
                <dd>
                  {t('status.uptimeValue', {
                    minutes: formatNumber(
                      Math.max(
                        1,
                        Math.round(meta.web.uptime_seconds / 60),
                      ),
                    ),
                  })}
                </dd>
              </div>
              <div>
                <dt>{t('status.source')}</dt>
                <dd className="settings-mono">{meta.source}</dd>
              </div>
              <div className="is-wide">
                <dt>{t('status.installDir')}</dt>
                <dd className="settings-mono">
                  {updateStatus?.install_dir ?? meta.install_dir}
                </dd>
              </div>
              <div>
                <dt>{t('status.build')}</dt>
                <dd className="settings-mono">{meta.web.build}</dd>
              </div>
            </dl>

            <div className="settings-subsection">
              <h3>{t('settings.updates')}</h3>
              <dl className="settings-facts">
                <div>
                  <dt>{t('status.currentVersion')}</dt>
                  <dd className="settings-mono">{currentVersion}</dd>
                </div>
                <div>
                  <dt>{t('status.latestVersion')}</dt>
                  <dd className="settings-mono">{latestVersion}</dd>
                </div>
                <div>
                  <dt>{t('status.lastCheck')}</dt>
                  <dd>{formatDate(lastCheck, languageLocale)}</dd>
                </div>
                <div>
                  <dt>{t('status.autoUpdate')}</dt>
                  <dd>
                    {autoUpdate
                      ? autoUpdate.enabled
                        ? t('status.enabled')
                        : t('status.disabled')
                      : '—'}
                  </dd>
                </div>
                {autoUpdate ? (
                  <div>
                    <dt>{t('status.autoChannel')}</dt>
                    <dd className="settings-mono">
                      {autoUpdate.channel}
                    </dd>
                  </div>
                ) : null}
              </dl>
              <div className="settings-section-actions">
                <button
                  className="settings-action"
                  type="button"
                  onClick={onCheckUpdates}
                  disabled={updateChecking}
                >
                  <Icon name="refresh" />
                  {updateChecking
                    ? t('status.checking')
                    : t('status.check')}
                </button>
                <button
                  className="settings-action"
                  type="button"
                  onClick={onOpenStatus}
                >
                  <Icon name="info" />
                  {t('settings.openStatus')}
                </button>
              </div>
            </div>
          </section>

          <section
            id="settings-advanced"
            data-settings-section="advanced"
            className="settings-section"
          >
            <header className="settings-section-head">
              <span className="settings-section-icon">
                <Icon name="sliders" />
              </span>
              <div>
                <h2>{t('settings.advancedTitle')}</h2>
                <p>{t('settings.advancedBody')}</p>
              </div>
            </header>

            <p className="settings-warning">{t('settings.advancedWarning')}</p>

            <div className="settings-rows">
              <SettingRow
                label={t('settings.transport')}
                help={t('settings.transportHelp')}
                control={
                  <Segmented
                    label={t('settings.transport')}
                    value={httpSettings.transport}
                    disabled={!httpLoaded}
                    options={[
                      {
                        value: 'auto',
                        label: t('settings.advancedAuto'),
                      },
                      { value: 'curl', label: 'curl' },
                      { value: 'http', label: 'HTTP' },
                    ]}
                    onChange={(value) =>
                      patchHttpSettings({
                        transport: value as HttpSettings['transport'],
                      })
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.browserProfile')}
                help={t('settings.browserProfileHelp')}
                control={
                  <SettingsSelect
                    value={httpSettings.browser}
                    label={t('settings.browserProfile')}
                    disabled={!httpLoaded}
                    options={browserOptions}
                    onChange={(value) =>
                      patchHttpSettings({ browser: value })
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.userAgent')}
                help={t('settings.userAgentHelp')}
                block
                control={
                  <div className="settings-inline-control">
                    <SettingsInput
                      wide
                      value={httpSettings.user_agent}
                      label={t('settings.userAgent')}
                      placeholder={t('settings.userAgentPlaceholder')}
                      disabled={!httpLoaded}
                      onChange={(value) =>
                        patchHttpSettings({ user_agent: value })
                      }
                    />
                    <button
                      className="settings-action"
                      type="button"
                      disabled={!httpLoaded || !httpSettings.user_agent}
                      onClick={() =>
                        patchHttpSettings({ user_agent: '' })
                      }
                    >
                      {t('settings.resetUserAgent')}
                    </button>
                  </div>
                }
              />
              <SettingRow
                label={t('settings.browserHeaders')}
                help={t('settings.browserHeadersHelp')}
                control={
                  <SettingsSwitch
                    checked={httpSettings.browser_headers}
                    label={t('settings.browserHeaders')}
                    disabled={!httpLoaded}
                    onChange={(checked) =>
                      patchHttpSettings({ browser_headers: checked })
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.timeout')}
                help={t('settings.timeoutHelp')}
                control={
                  <SettingsNumberField
                    value={httpSettings.timeout_seconds}
                    label={t('settings.timeout')}
                    min={5}
                    max={120}
                    suffix={t('settings.secondsUnit')}
                    disabled={!httpLoaded}
                    onCommit={(value) =>
                      patchHttpSettings({ timeout_seconds: value })
                    }
                  />
                }
              />
              <SettingRow
                label={t('settings.fallback')}
                help={t('settings.fallbackHelp')}
                control={
                  <SettingsSwitch
                    checked={httpSettings.fallback_to_curl}
                    label={t('settings.fallback')}
                    disabled={!httpLoaded}
                    onChange={(checked) =>
                      patchHttpSettings({ fallback_to_curl: checked })
                    }
                  />
                }
              />
            </div>

            <p className="settings-note">
              {httpSaving
                ? t('settings.advancedSaving')
                : t('settings.advancedMonitorsNote')}
            </p>
            {httpError ? (
              <p className="settings-warning">{httpError}</p>
            ) : null}

            <div className="settings-section-actions">
              <button
                className="settings-action"
                type="button"
                disabled={!httpLoaded || httpSaving}
                onClick={() =>
                  setHttpSettings({ ...defaultHttpSettings })
                }
              >
                <Icon name="refresh" />
                {t('settings.resetAdvanced')}
              </button>
              <button
                className="settings-action"
                type="button"
                disabled={httpLoading}
                onClick={() => void loadHttpDiagnostics(true)}
              >
                <Icon name="activity" />
                {t('settings.refreshDiagnostics')}
              </button>
            </div>

            <div className="settings-subsection">
              <h3>{t('settings.diagnostics')}</h3>
              <p className="settings-note">
                {t('settings.diagnosticsBody')}
              </p>

              {httpLoading && !httpDiagnostics ? (
                <p className="settings-note">
                  {t('settings.advancedLoading')}
                </p>
              ) : null}

              {httpDiagnostics ? (
                <>
                  <dl className="settings-facts">
                    <div>
                      <dt>{t('settings.curlStatus')}</dt>
                      <dd>
                        {httpDiagnostics.curl.available
                          ? t('status.curlOk')
                          : t('status.curlUnavailable')}
                      </dd>
                    </div>
                    <div>
                      <dt>{t('settings.curlVersion')}</dt>
                      <dd className="settings-mono">
                        {httpDiagnostics.curl.version}
                      </dd>
                    </div>
                    <div>
                      <dt>{t('settings.curlMode')}</dt>
                      <dd className="settings-mono">
                        {httpDiagnostics.curl.mode}
                      </dd>
                    </div>
                    <div>
                      <dt>{t('settings.curlTransport')}</dt>
                      <dd className="settings-mono">
                        {httpDiagnostics.curl.transport}
                      </dd>
                    </div>
                    <div className="is-wide">
                      <dt>{t('settings.curlPath')}</dt>
                      <dd className="settings-mono">
                        {httpDiagnostics.curl.runtime}
                      </dd>
                    </div>
                  </dl>
                  {httpDiagnostics.curl.error ? (
                    <p className="settings-note">
                      {httpDiagnostics.curl.error}
                    </p>
                  ) : null}

                  <div className="settings-effective">
                    <div className="settings-effective-head">
                      <span>{t('settings.effectiveDomain')}</span>
                      <span>{t('settings.effectiveConfig')}</span>
                    </div>
                    {httpDiagnostics.effective.map((row) => (
                      <div
                        className="settings-effective-row"
                        key={row.domain}
                      >
                        <span className="settings-effective-domain">
                          {row.domain.toUpperCase()}
                        </span>
                        <div className="settings-effective-main">
                          <span>
                            {row.transport} · {row.browser} ·{' '}
                            {row.browser_headers
                              ? t('status.enabled')
                              : t('status.disabled')}{' '}
                            · {formatNumber(row.timeout_seconds)} s
                          </span>
                          <code title={row.user_agent ?? ''}>
                            {row.user_agent ||
                              t('settings.advancedAuto')}
                          </code>
                        </div>
                      </div>
                    ))}
                  </div>

                  {httpDiagnostics.cooldowns.length > 0 ? (
                    <div className="settings-cooldowns">
                      {httpDiagnostics.cooldowns.map((item) => (
                        <div
                          className="settings-cooldown"
                          key={item.domain}
                        >
                          <div>
                            <strong>
                              {item.domain.toUpperCase()}
                            </strong>
                            <span>
                              {t('settings.cooldownUntil', {
                                date: formatDate(
                                  item.until,
                                  languageLocale,
                                ),
                              })}
                            </span>
                            {item.reason ? (
                              <small>{item.reason}</small>
                            ) : null}
                          </div>
                          <button
                            className="settings-action"
                            type="button"
                            disabled={
                              clearingCooldown === item.domain
                            }
                            onClick={() =>
                              void clearCooldown(item.domain)
                            }
                          >
                            {t('settings.clearCooldown')}
                          </button>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="settings-note">
                      {t('settings.noCooldowns')}
                    </p>
                  )}

                  <p className="settings-note">
                    {t('settings.advancedRestartMonitors')}
                  </p>
                </>
              ) : null}
            </div>
          </section>
        </div>
      </div>
    </section>
  )
}
