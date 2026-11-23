import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
  type RefObject,
} from 'react'
import { createPortal } from 'react-dom'
import { getSuggestions, type Marketplace } from '../lib/api'
import { looksLikeAsin } from '../lib/asin'
import {
  activeFilterCount,
  defaultSearchFilters,
  sortOptions,
  type SearchFilterState,
} from '../lib/searchFilters'
import { useI18n } from '../i18n/I18nProvider'
import { LocationPicker } from './LocationPicker'
import { Icon } from './ui'

const DEBOUNCE_MS = 280
const CLIENT_CACHE_MS = 5 * 60 * 1000
const SUGGESTIONS_ID = 'search-suggestions'
const FILTERS_ID = 'search-filters'

type CachedSuggestions = {
  at: number
  values: string[]
}

function minimumLength(domain: string): number {
  return domain === 'jp' ? 1 : 2
}

export function SearchForm({
  query,
  onQueryChange,
  domain,
  domains,
  onDomainsChange,
  onDomainToggle,
  filters,
  onFiltersChange,
  marketplaces,
  deliveryCountry,
  deliveryPostalCode,
  onDeliveryLocationChange,
  running,
  onSubmit,
  onSearch,
  onOpenDeals,
  searchRef,
  hasResults = false,
  refreshRequired = false,
  locationRefreshRequired = false,
  pendingDomains = [],
}: {
  query: string
  onQueryChange: (value: string) => void
  domain: string
  domains: string[]
  onDomainsChange: (value: string[]) => void
  onDomainToggle: (id: string) => void
  filters: SearchFilterState
  onFiltersChange: (patch: Partial<SearchFilterState>) => void
  marketplaces: Marketplace[]
  deliveryCountry: string
  deliveryPostalCode: string
  onDeliveryLocationChange: (country: string, postalCode: string) => void
  running: boolean
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  onSearch: (query: string) => void
  onOpenDeals: () => void
  searchRef: RefObject<HTMLInputElement | null>
  hasResults?: boolean
  refreshRequired?: boolean
  locationRefreshRequired?: boolean
  pendingDomains?: string[]
}) {
  const { t } = useI18n()
  const [suggestions, setSuggestions] = useState<string[]>([])
  const [activeIndex, setActiveIndex] = useState(-1)
  const [open, setOpen] = useState(false)
  const [focused, setFocused] = useState(false)
  const [loading, setLoading] = useState(false)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const abortRef = useRef<AbortController | null>(null)
  const skipFetchRef = useRef(false)
  const suppressedRef = useRef(false)
  const cacheRef = useRef(new Map<string, CachedSuggestions>())
  const filterButtonRef = useRef<HTMLButtonElement>(null)
  const filterPanelRef = useRef<HTMLElement>(null)
  const filterCloseRef = useRef<HTMLButtonElement>(null)

  const selectedMarketplace = marketplaces.find(
    (marketplace) => marketplace.id === domain,
  )
  const selectedMarketplaces = domains
    .map((id) => marketplaces.find((marketplace) => marketplace.id === id))
    .filter((marketplace): marketplace is Marketplace => Boolean(marketplace))
  const selectedNames = selectedMarketplaces.map(
    (marketplace) => marketplace.display_name,
  )
  const pendingNames = pendingDomains.map(
    (id) =>
      marketplaces.find((marketplace) => marketplace.id === id)
        ?.display_name ?? id.toUpperCase(),
  )
  const activeFilters = activeFilterCount(filters, domains)
  const selectedCurrency =
    selectedMarketplaces.length === 1
      ? selectedMarketplaces[0].currency
      : selectedMarketplaces.length > 1 &&
          selectedMarketplaces.every(
            (marketplace) =>
              marketplace.currency === selectedMarketplaces[0].currency,
          )
        ? selectedMarketplaces[0].currency
        : null
  const currencyLabel = selectedCurrency ?? t('search.localCurrency')

  useEffect(() => {
    abortRef.current?.abort()
    abortRef.current = null

    if (!focused) {
      setOpen(false)
      setActiveIndex(-1)
      setLoading(false)
      return
    }

    if (skipFetchRef.current) {
      skipFetchRef.current = false
      setLoading(false)
      return
    }

    const trimmed = query.trim()
    if (
      trimmed.length < minimumLength(domain) ||
      looksLikeAsin(trimmed)
    ) {
      setSuggestions([])
      setOpen(false)
      setActiveIndex(-1)
      setLoading(false)
      return
    }

    const key = `${domain}:${trimmed.toLowerCase()}`
    const cached = cacheRef.current.get(key)
    if (cached && Date.now() - cached.at < CLIENT_CACHE_MS) {
      setSuggestions(cached.values)
      setOpen(cached.values.length > 0)
      setActiveIndex(-1)
      setLoading(false)
      return
    }

    const controller = new AbortController()
    abortRef.current = controller
    setActiveIndex(-1)
    setLoading(true)

    const timer = window.setTimeout(async () => {
      try {
        const data = await getSuggestions(trimmed, domain, controller.signal)
        if (controller.signal.aborted) {
          return
        }

        const values = data.suggestions ?? []
        cacheRef.current.set(key, { at: Date.now(), values })
        setSuggestions(values)
        if (!suppressedRef.current) {
          setOpen(values.length > 0)
        }
        setActiveIndex(-1)
      } catch {
        if (!controller.signal.aborted) {
          setSuggestions([])
          setOpen(false)
          setActiveIndex(-1)
        }
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      }
    }, DEBOUNCE_MS)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [query, domain, focused])

  const closeFilters = () => {
    filterButtonRef.current?.focus()
    setFiltersOpen(false)
  }

  useEffect(() => {
    if (!filtersOpen) {
      return
    }

    const root = document.documentElement
    const body = document.body
    const previous = {
      overflow: root.style.overflow,
      scrollbarGutter: root.style.scrollbarGutter,
      bodyOverflow: body.style.overflow,
      paddingRight: body.style.paddingRight,
    }
    const gutter = Math.max(
      0,
      window.innerWidth - root.getBoundingClientRect().width,
    )
    root.style.overflow = 'hidden'
    root.style.scrollbarGutter = 'auto'
    body.style.overflow = 'hidden'
    if (gutter > 0) {
      body.style.paddingRight = previous.paddingRight
        ? `calc(${previous.paddingRight} + ${gutter}px)`
        : `${gutter}px`
    }
    filterCloseRef.current?.focus()

    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        filterButtonRef.current?.focus()
        setFiltersOpen(false)
        return
      }
      if (event.key !== 'Tab' || !filterPanelRef.current) {
        return
      }

      const focusable = Array.from(
        filterPanelRef.current.querySelectorAll<HTMLElement>(
          'button:not(:disabled), select, input:not(:disabled)',
        ),
      )
      if (focusable.length === 0) {
        return
      }
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      if (event.shiftKey && (active === first || active === filterPanelRef.current)) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      root.style.overflow = previous.overflow
      root.style.scrollbarGutter = previous.scrollbarGutter
      body.style.overflow = previous.bodyOverflow
      body.style.paddingRight = previous.paddingRight
      document.removeEventListener('keydown', onKeyDown)
      filterButtonRef.current?.focus()
    }
  }, [filtersOpen])

  const closeSuggestions = () => {
    abortRef.current?.abort()
    abortRef.current = null
    setLoading(false)
    setOpen(false)
    setActiveIndex(-1)
  }

  const choose = (value: string) => {
    suppressedRef.current = true
    closeSuggestions()
    if (value !== query) {
      skipFetchRef.current = true
      onQueryChange(value)
    }
    onSearch(value)
  }

  const preview = (index: number) => {
    const value = suggestions[index]
    if (value === undefined) {
      return
    }

    setActiveIndex(index)
    if (value !== query) {
      skipFetchRef.current = true
      onQueryChange(value)
    }
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      if (suggestions.length === 0) {
        return
      }
      event.preventDefault()
      suppressedRef.current = false
      setOpen(true)
      const next =
        activeIndex < 0
          ? 0
          : Math.min(activeIndex + 1, suggestions.length - 1)
      preview(next)
      return
    }

    if (event.key === 'ArrowUp') {
      if (suggestions.length === 0) {
        return
      }
      event.preventDefault()
      suppressedRef.current = false
      setOpen(true)
      const next = activeIndex < 0 ? 0 : Math.max(activeIndex - 1, 0)
      preview(next)
      return
    }

    if (event.key === 'Enter' && open && activeIndex >= 0) {
      const value = suggestions[activeIndex]
      if (value) {
        event.preventDefault()
        choose(value)
      }
      return
    }

    if (event.key === 'Escape' && open) {
      event.preventDefault()
      event.stopPropagation()
      closeSuggestions()
    }
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    suppressedRef.current = true
    closeSuggestions()
    onSubmit(event)
  }

  const openFilters = () => {
    suppressedRef.current = true
    closeSuggestions()
    setFiltersOpen(true)
  }

  const resetFilters = () => {
    onFiltersChange({ ...defaultSearchFilters })
    onDomainsChange([domain])
  }

  const applyFilters = () => {
    closeFilters()
    if (
      query.trim().length > 0 &&
      (refreshRequired || !hasResults)
    ) {
      onSearch(query)
    }
  }

  const filterActionLabel = running
    ? t('search.searching')
    : refreshRequired
      ? t('search.refresh')
      : hasResults
        ? t('search.apply')
        : t('search.search')

  const activeOptionId =
    open && activeIndex >= 0
      ? `${SUGGESTIONS_ID}-${activeIndex}`
      : undefined

  const normalizedQuery = query.trim().toLowerCase()

  return (
    <>
      <form className="search-form" onSubmit={handleSubmit}>
        <div className="search-shell">
          <Icon name="search" className="search-shell-icon" />
          <input
            ref={searchRef}
            id="search-query"
            type="search"
            className="search-input"
            value={query}
            onChange={(event) => {
              suppressedRef.current = false
              setActiveIndex(-1)
              onQueryChange(event.target.value)
            }}
            onFocus={() => {
              suppressedRef.current = false
              setFocused(true)
            }}
            onBlur={() => setFocused(false)}
            onKeyDown={handleKeyDown}
            placeholder={t('search.placeholder')}
            autoComplete="off"
            spellCheck={false}
            aria-label={t('search.searchAmazon')}
            role="combobox"
            aria-autocomplete="list"
            aria-haspopup="listbox"
            aria-expanded={open}
            aria-controls={open ? SUGGESTIONS_ID : undefined}
            aria-activedescendant={activeOptionId}
            aria-busy={loading}
          />
          <div className="search-shell-actions">
            <button
              ref={filterButtonRef}
              className="search-filter-button"
              type="button"
              onClick={openFilters}
              aria-expanded={filtersOpen}
              aria-controls={FILTERS_ID}
            >
              <Icon name="filter" />
              {t('search.filters')}
              <span className="filter-count">{activeFilters}</span>
            </button>
            <button
              className="search-submit"
              type="submit"
              disabled={running}
            >
              {running ? t('search.searching') : t('search.search')}
              <Icon name="arrow-right" />
            </button>
          </div>

          {open && suggestions.length > 0 ? (
            <ul
              className="search-suggestions"
              id={SUGGESTIONS_ID}
              role="listbox"
              aria-label={t('search.suggestions')}
            >
              {suggestions.map((suggestion, index) => {
                const matches = suggestion
                  .toLowerCase()
                  .startsWith(normalizedQuery)
                const prefixLength = matches ? normalizedQuery.length : 0
                return (
                  <li
                    className={
                      index === activeIndex
                        ? 'search-suggestion is-active'
                        : 'search-suggestion'
                    }
                    id={`${SUGGESTIONS_ID}-${index}`}
                    key={suggestion}
                    role="option"
                    aria-selected={index === activeIndex}
                    title={suggestion}
                    onMouseDown={(event) => event.preventDefault()}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => choose(suggestion)}
                  >
                    <Icon name="search" />
                    <span className="search-suggestion-text">
                      {prefixLength > 0 ? (
                        <>
                          <strong>{suggestion.slice(0, prefixLength)}</strong>
                          {suggestion.slice(prefixLength)}
                        </>
                      ) : (
                        suggestion
                      )}
                    </span>
                  </li>
                )
              })}
            </ul>
          ) : null}
        </div>

        <div className="search-market-row">
          <span
            className="search-marketplace"
            title={selectedNames.join(' · ')}
          >
            <span aria-hidden="true" />
            {domains.length > 1
              ? `${selectedNames[0] ?? domain.toUpperCase()} +${domains.length - 1}`
              : selectedMarketplace?.display_name ?? domain.toUpperCase()}
          </span>
          {domains.length > 1 ? (
            <span className="search-market-note">
              {t('search.searchingMarketplaces', { count: domains.length })}
            </span>
          ) : null}
          <LocationPicker
            country={deliveryCountry}
            postalCode={deliveryPostalCode}
            domesticCountry={selectedMarketplace?.country ?? 'US'}
            countryOnlyDelivery={
              selectedMarketplaces.length > 0 &&
              selectedMarketplaces.every(
                (marketplace) => marketplace.country_only_delivery,
              )
            }
            onApply={onDeliveryLocationChange}
          />
          <button
            className="search-deals-link"
            type="button"
            onClick={onOpenDeals}
          >
            <Icon name="tag" />
            {t('search.todayDeals')}
            <Icon name="arrow-right" />
          </button>
        </div>
      </form>

      {createPortal(
        <>
          <div
            className={`filter-scrim${filtersOpen ? ' is-open' : ''}`}
            aria-hidden="true"
            onClick={closeFilters}
          />
          <aside
            ref={filterPanelRef}
            id={FILTERS_ID}
            className={`filter-panel${filtersOpen ? ' is-open' : ''}`}
            role="dialog"
            aria-modal="true"
            aria-labelledby="search-filters-title"
            aria-hidden={!filtersOpen}
          >
            <header className="filter-head">
              <div className="filter-heading">
                <h2 id="search-filters-title">{t('search.filters')}</h2>
                <p className="filter-subtitle">
                  {t('search.refine')}
                  {activeFilters > 0
                    ? ` · ${t('search.activeCount', { count: activeFilters })}`
                    : ''}
                </p>
              </div>
              <button
                ref={filterCloseRef}
                className="filter-close"
                type="button"
                onClick={closeFilters}
                aria-label={t('search.closeFilters')}
              >
                <Icon name="close" />
              </button>
            </header>

            <div className="filter-body">
              <section className="filter-block">
                <div className="filter-block-head">
                  <h3>{t('search.marketplace')}</h3>
                  <span>
                    {t('search.selectedCount', { count: domains.length })}
                  </span>
                </div>
                <p className="filter-help">{t('search.marketplaceHelp')}</p>
                {pendingNames.length > 0 ? (
                  <p className="filter-refresh-note" role="status">
                    <Icon name="alert" />
                    <span>
                      {t(
                        pendingNames.length === 1
                          ? 'search.pendingOne'
                          : 'search.pendingMany',
                        { name: pendingNames[0] ?? '', names: pendingNames.join(', ') },
                      )}
                    </span>
                  </p>
                ) : locationRefreshRequired ? (
                  <p className="filter-refresh-note" role="status">
                    <Icon name="alert" />
                    <span>{t('search.locationRefresh')}</span>
                  </p>
                ) : refreshRequired ? (
                  <p className="filter-refresh-note" role="status">
                    <Icon name="alert" />
                    <span>{t('search.refreshRequired')}</span>
                  </p>
                ) : null}
                <div
                  className="marketplace-grid"
                  role="group"
                  aria-label={t('search.searchMarketplaces')}
                >
                  {marketplaces.map((marketplace) => {
                    const selected = domains.includes(marketplace.id)
                    const primary = selected && domains[0] === marketplace.id
                    return (
                      <button
                        className={`marketplace-option${selected ? ' is-selected' : ''}`}
                        type="button"
                        key={marketplace.id}
                        aria-pressed={selected}
                        onClick={() => onDomainToggle(marketplace.id)}
                        title={
                          primary
                            ? t('search.primaryMarketplace', {
                                marketplace: marketplace.display_name,
                              })
                            : marketplace.display_name
                        }
                      >
                        <span className="marketplace-option-top">
                          <span className="marketplace-option-name">
                            {marketplace.display_name}
                          </span>
                          <span
                            className="marketplace-option-check"
                            aria-hidden="true"
                          >
                            {selected ? <Icon name="check" /> : null}
                          </span>
                        </span>
                        <span className="marketplace-option-meta">
                          {marketplace.currency} · {marketplace.country}
                        </span>
                        {primary ? (
                          <span className="marketplace-option-primary">
                            {t('search.primary')}
                          </span>
                        ) : null}
                      </button>
                    )
                  })}
                </div>
              </section>

              <section className="filter-block">
                <div className="filter-block-head">
                  <h3>{t('search.sortBy')}</h3>
                </div>
                <div
                  className="filter-choice-grid"
                  role="group"
                  aria-label={t('search.resultSorting')}
                >
                  {sortOptions.map((option) => (
                    <button
                      className={`filter-choice${filters.sort === option.value ? ' is-selected' : ''}`}
                      type="button"
                      key={option.value}
                      aria-pressed={filters.sort === option.value}
                      onClick={() => onFiltersChange({ sort: option.value })}
                    >
                      {t(option.labelKey)}
                    </button>
                  ))}
                </div>
              </section>

              <section className="filter-block">
                <div className="filter-block-head">
                  <h3>{t('search.filterResults')}</h3>
                  <span>{t('search.instant')}</span>
                </div>
                <div className="filter-price-grid">
                  <label className="filter-field">
                    <span>{t('search.minPrice')}</span>
                    <span className="filter-price-control">
                      <input
                        type="text"
                        inputMode="decimal"
                        value={filters.priceMin}
                        onChange={(event) =>
                          onFiltersChange({ priceMin: event.target.value })
                        }
                        placeholder="—"
                        autoComplete="off"
                      />
                      <span>{currencyLabel}</span>
                    </span>
                  </label>
                  <label className="filter-field">
                    <span>{t('search.maxPrice')}</span>
                    <span className="filter-price-control">
                      <input
                        type="text"
                        inputMode="decimal"
                        value={filters.priceMax}
                        onChange={(event) =>
                          onFiltersChange({ priceMax: event.target.value })
                        }
                        placeholder="—"
                        autoComplete="off"
                      />
                      <span>{currencyLabel}</span>
                    </span>
                  </label>
                </div>
                {domains.length > 1 ? (
                  <p className="filter-help">
                    {t('search.pricesCompared')}
                  </p>
                ) : null}

                <span className="filter-sublabel">{t('search.minRating')}</span>
                <div
                  className="filter-choice-row"
                  role="group"
                  aria-label={t('search.minRating')}
                >
                  {[0, 3, 4, 4.5].map((value) => (
                    <button
                      className={`filter-choice filter-rating-option${filters.minRating === value ? ' is-selected' : ''}`}
                      type="button"
                      key={value}
                      aria-pressed={filters.minRating === value}
                      onClick={() => onFiltersChange({ minRating: value })}
                    >
                      {value > 0 ? <Icon name="star" /> : null}
                      {value === 0
                        ? t('search.all')
                        : `${String(value)}+`}
                    </button>
                  ))}
                </div>

                <div className="filter-switches">
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.onSaleOnly')}</span>
                      <small>{t('search.onSaleHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.onlyDeals}
                      onChange={(event) =>
                        onFiltersChange({ onlyDeals: event.target.checked })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.freeShipping')}</span>
                      <small>{t('search.freeShippingHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.freeShipping}
                      onChange={(event) =>
                        onFiltersChange({
                          freeShipping: event.target.checked,
                          ...(event.target.checked
                            ? { delivery: true }
                            : {}),
                        })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.amazonChoice')}</span>
                      <small>{t('search.amazonChoiceHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.amazonChoice}
                      onChange={(event) =>
                        onFiltersChange({ amazonChoice: event.target.checked })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                </div>
              </section>

              <section className="filter-block">
                <div className="filter-block-head">
                  <h3>{t('search.searchOptions')}</h3>
                  <span>{t('search.withSearch')}</span>
                </div>
                <div className="filter-switches">
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.noSponsored')}</span>
                      <small>{t('search.noSponsoredHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.noAds}
                      onChange={(event) =>
                        onFiltersChange({ noAds: event.target.checked })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.deliveryEstimate')}</span>
                      <small>{t('search.deliveryHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.delivery}
                      disabled={filters.freeShipping}
                      onChange={(event) =>
                        onFiltersChange({ delivery: event.target.checked })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                  <label className="filter-switch">
                    <span className="filter-switch-copy">
                      <span>{t('search.referencePrices')}</span>
                      <small>{t('search.referenceHelp')}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={filters.enrich}
                      onChange={(event) =>
                        onFiltersChange({ enrich: event.target.checked })
                      }
                    />
                    <span className="filter-switch-track" aria-hidden="true" />
                  </label>
                </div>
              </section>

              <p className="filter-note">
                {t('search.filterNote')}
              </p>
            </div>

            <footer className="filter-footer">
              <button
                className="filter-reset"
                type="button"
                onClick={resetFilters}
              >
                {t('search.reset')}
              </button>
              <button
                className="filter-done"
                type="button"
                onClick={applyFilters}
                disabled={running || query.trim().length === 0}
              >
                {filterActionLabel}
                <Icon
                  name={refreshRequired ? 'refresh' : 'arrow-right'}
                />
              </button>
            </footer>
          </aside>
        </>,
        document.body,
      )}
    </>
  )
}
