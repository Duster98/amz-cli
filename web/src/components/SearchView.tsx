import { Fragment, useCallback, useEffect, useRef, useState } from 'react'
import type { ComponentProps } from 'react'
import type {
  SearchItem,
  SearchInput,
  SearchJob,
  SearchResultPayload,
} from '../lib/api'
import { formatDuration } from '../lib/format'
import { formatRecentTime, type RecentSearch } from '../lib/recent'
import { useI18n } from '../i18n/I18nProvider'
import {
  applySearchFilters,
  hasLocalFilters,
  isLocalSort,
} from '../lib/searchFilters'
import { ResultsList, type ResultsLayout } from './ResultsList'
import { SearchForm } from './SearchForm'
import { Icon, SkeletonGrid, SkeletonRows } from './ui'

type SearchFormProps = ComponentProps<typeof SearchForm>

const STICKY_RELEASE_PX = 8

function buildPageNumbers(current: number, total: number): number[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, index) => index + 1)
  }

  return [...new Set([1, 2, current - 1, current, current + 1, total - 1, total])]
    .filter((page) => page >= 1 && page <= total)
    .sort((left, right) => left - right)
}

export function SearchView({
  form,
  results,
  lastSearch,
  job,
  error,
  layout,
  onLayoutChange,
  onPageChange,
  recentSearches,
  onCancel,
  onOpenProduct,
  onRetry,
  cacheNotice,
  onRefreshCache,
  onExample,
  onRecentSearch,
  onRemoveRecentSearch,
  onClearRecentSearches,
  isSaved,
  onToggleSave,
}: {
  form: SearchFormProps
  results: SearchResultPayload | null
  lastSearch: SearchInput | null
  job: SearchJob | null
  error: { code: string; message: string } | null
  layout: ResultsLayout
  onLayoutChange: (layout: ResultsLayout) => void
  onPageChange: (page: number) => void
  recentSearches: RecentSearch[]
  onCancel: () => void
  onOpenProduct: (asin: string, marketplaceId?: string | null) => void
  onRetry: () => void
  cacheNotice?: string | null
  onRefreshCache?: () => void
  onExample: (query: string) => void
  onRecentSearch: (item: RecentSearch) => void
  onRemoveRecentSearch: (item: RecentSearch) => void
  onClearRecentSearches: () => void
  isSaved?: (item: SearchItem) => boolean
  onToggleSave?: (item: SearchItem) => void
}) {
  const [scrolled, setScrolled] = useState(false)
  const pageRef = useRef<HTMLElement>(null)
  const stickyRef = useRef<HTMLDivElement>(null)
  const resultsHeadRef = useRef<HTMLElement>(null)
  const { t, formatNumber, languageLocale } = useI18n()
  const resultsSectionRef = useRef<HTMLElement>(null)
  const running = form.running
  const progress = job?.progress
  const stage = progress?.stage
  const percent = !progress
    ? 0
    : stage === 'done'
      ? 100
      : stage === 'enriching'
        ? 88
        : stage === 'searching'
          ? Math.min(
              80,
              8 +
                (progress.total_pages > 0
                  ? (progress.completed_pages / progress.total_pages) * 72
                  : 0),
            )
          : 4
  const hasSearchState = Boolean(results || running || error)
  const resultMarketplaces = results?.marketplaces ?? []
  const searchedDomains = results
    ? resultMarketplaces.length > 0
      ? resultMarketplaces.map((marketplace) => marketplace.id)
      : [results.marketplace_id]
    : []
  const missingDomains = results
    ? form.domains.filter((id) => !searchedDomains.includes(id))
    : []
  const missingNames = missingDomains.map(
    (id) =>
      form.marketplaces.find((marketplace) => marketplace.id === id)
        ?.display_name ?? id.toUpperCase(),
  )
  const domainResults = results
    ? results.results.filter((item) =>
        form.domains.includes(item.marketplace_id ?? results.marketplace_id),
      )
    : []
  const filteredResults = results
    ? applySearchFilters(domainResults, form.filters)
    : []
  const hasResultItems = filteredResults.length > 0
  const localFiltersActive = hasLocalFilters(form.filters)
  const visibleMarketplaces = resultMarketplaces.filter((marketplace) =>
    form.domains.includes(marketplace.id),
  )
  const multiMarketplace = visibleMarketplaces.length > 1
  const queryChanged = Boolean(
    results && form.query.trim() !== results.query.trim(),
  )
  const requestOptionsChanged = Boolean(
    results &&
      lastSearch &&
      ((!form.filters.noAds && lastSearch.no_ads) ||
        (form.filters.enrich && !lastSearch.enrich) ||
        (form.filters.delivery && !lastSearch.delivery) ||
        (!isLocalSort(form.filters.sort) &&
          form.filters.sort !== lastSearch.sort)),
  )
  const locationChanged = Boolean(
    results &&
      lastSearch &&
      (form.deliveryCountry !== lastSearch.delivery_country ||
        form.deliveryPostalCode !== lastSearch.delivery_postal_code),
  )
  const refreshRequired = Boolean(
    results &&
      (queryChanged ||
        missingDomains.length > 0 ||
        requestOptionsChanged ||
        locationChanged),
  )
  const resultGroups = multiMarketplace
    ? form.domains
        .map((id) =>
          visibleMarketplaces.find((marketplace) => marketplace.id === id),
        )
        .filter(
          (
            marketplace,
          ): marketplace is NonNullable<
            SearchResultPayload['marketplaces']
          >[number] => Boolean(marketplace),
        )
        .map((marketplace) => ({
          ...marketplace,
          items: filteredResults.filter(
            (item) => item.marketplace_id === marketplace.id,
          ),
        }))
        .filter((group) => group.items.length > 0)
    : []
  const showLayoutSwitch = Boolean(results || running)
  const pageCount = results
    ? Math.max(1, results.total_available_pages)
    : 1
  const currentPage = results?.requested_pages[0] ?? 1
  const showPagination =
    Boolean(results) &&
    pageCount > 1 &&
    (hasResultItems || localFiltersActive)
  const pageNumbers = buildPageNumbers(currentPage, pageCount)
  const resultsVersion = results
    ? `${results.query}:${results.completed_pages.join('-')}:${results.results.length}:${results.elapsed_ms}`
    : ''

  const syncDock = useCallback(() => {
    const page = pageRef.current
    if (!page) {
      return
    }

    page.style.setProperty(
      '--search-dock-height',
      `${stickyRef.current?.offsetHeight ?? 0}px`,
    )
    page.style.setProperty(
      '--results-head-height',
      `${resultsHeadRef.current?.offsetHeight ?? 0}px`,
    )
  }, [])

  useEffect(() => {
    let frame = 0
    let docked = false
    const update = () => {
      frame = 0
      const sticky = stickyRef.current
      if (!sticky) {
        return
      }

      const top = sticky.getBoundingClientRect().top
      // Compacting the header changes its height. A small release threshold
      // keeps the state stable when the header sits exactly on the edge.
      const next = docked ? top < STICKY_RELEASE_PX : top <= 0.5
      if (next !== docked) {
        docked = next
        setScrolled(next)
      }
      syncDock()
    }
    const onScroll = () => {
      if (frame === 0) {
        frame = window.requestAnimationFrame(update)
      }
    }
    update()
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('resize', onScroll)
    return () => {
      if (frame !== 0) {
        window.cancelAnimationFrame(frame)
      }
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('resize', onScroll)
    }
  }, [syncDock])

  useEffect(() => {
    syncDock()
    const observer =
      typeof ResizeObserver === 'undefined'
        ? null
        : new ResizeObserver(() => syncDock())
    if (stickyRef.current) {
      observer?.observe(stickyRef.current)
    }
    if (resultsHeadRef.current) {
      observer?.observe(resultsHeadRef.current)
    }
    const frame = window.requestAnimationFrame(syncDock)
    return () => {
      window.cancelAnimationFrame(frame)
      observer?.disconnect()
    }
  }, [hasSearchState, syncDock])

  const goToPage = (nextPage: number) => {
    const bounded = Math.min(Math.max(1, nextPage), pageCount)
    if (bounded === currentPage) {
      return
    }
    onPageChange(bounded)
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    window.requestAnimationFrame(() => {
      resultsSectionRef.current?.scrollIntoView({
        behavior: reduced ? 'auto' : 'smooth',
        block: 'start',
      })
    })
  }

  return (
    <section className="search-page" ref={pageRef}>
      <header className="page-head">
        <h1>{t('searchView.newSearch')}</h1>
        <p>{t('searchView.intro')}</p>
      </header>

      <div
        className={`search-sticky${scrolled ? ' is-scrolled' : ''}`}
        ref={stickyRef}
      >
        <SearchForm
          {...form}
          hasResults={Boolean(results)}
          refreshRequired={refreshRequired}
          locationRefreshRequired={locationChanged}
          pendingDomains={missingDomains}
        />
      </div>

      {hasSearchState ? (
        <section
          className="results-section"
          aria-label={t('searchView.results')}
          ref={resultsSectionRef}
        >
          <header
            ref={resultsHeadRef}
            className={`results-head${running ? ' is-running' : ''}${scrolled ? ' is-docked' : ''}`}
          >
            <div className="results-title-row">
              <h2>{t('searchView.results')}</h2>
              <div className="results-head-tools">
                {running ? (
                  <span className="results-updating">
                    <span className="results-updating-label">
                      {progress?.message ?? t('searchView.updating')}
                    </span>
                    <button
                      className="text-button"
                      type="button"
                      onClick={onCancel}
                    >
                      {t('searchView.cancel')}
                    </button>
                  </span>
                ) : results ? (
                  <span className="results-duration">
                    {formatDuration(results.elapsed_ms)}
                  </span>
                ) : null}

                {showLayoutSwitch ? (
                  <div
                    className="results-layout"
                    role="group"
                    aria-label={t('searchView.resultsView')}
                  >
                    <button
                      className={layout === 'list' ? 'is-active' : ''}
                      type="button"
                      onClick={() => onLayoutChange('list')}
                      aria-pressed={layout === 'list'}
                      title={t('searchView.listView')}
                    >
                      <Icon name="list" />
                      <span className="sr-only">{t('searchView.listView')}</span>
                    </button>
                    <button
                      className={layout === 'grid' ? 'is-active' : ''}
                      type="button"
                      onClick={() => onLayoutChange('grid')}
                      aria-pressed={layout === 'grid'}
                      title={t('searchView.gridView')}
                    >
                      <Icon name="grid" />
                      <span className="sr-only">{t('searchView.gridView')}</span>
                    </button>
                  </div>
                ) : null}
              </div>
            </div>

            {results ? (
              <p className="results-sub">
                {filteredResults.length !== results.results.length
                  ? t('searchView.productsOf', {
                      shown: formatNumber(filteredResults.length),
                      total: formatNumber(results.results.length),
                      query: results.query,
                    })
                  : t('searchView.products', {
                      count: formatNumber(filteredResults.length),
                      query: results.query,
                    })}
                {multiMarketplace
                  ? ` ${t('searchView.across', {
                      names: visibleMarketplaces
                        .map((marketplace) => marketplace.name)
                        .join(' · '),
                    })}`
                  : null}
              </p>
            ) : running ? (
              <p className="results-sub">{t('searchView.reading')}</p>
            ) : (
              <p className="results-sub">{t('searchView.oneSearch')}</p>
            )}

            {missingNames.length > 0 && !running ? (
              <p className="results-refresh-hint" role="status">
                <Icon name="alert" />
                {t(
                  missingNames.length === 1
                    ? 'searchView.notIncludedOne'
                    : 'searchView.notIncludedMany',
                  {
                    name: missingNames[0] ?? '',
                    names: missingNames.join(', '),
                  },
                )}
              </p>
            ) : null}

            {running ? (
              <span
                className="results-progress-line"
                style={{ width: `${percent}%` }}
                aria-hidden="true"
              />
            ) : null}
          </header>

          {cacheNotice && results ? (
            <div className="notice history-cache-notice" role="status">
              <Icon name="clock" />
              <div>
                <strong>{t('history.cacheNotice')}</strong>
                <p>
                  {new Intl.DateTimeFormat(languageLocale, {
                    day: 'numeric',
                    month: 'short',
                    hour: '2-digit',
                    minute: '2-digit',
                  }).format(new Date(cacheNotice))}
                </p>
              </div>
              {onRefreshCache ? (
                <button
                  className="text-button"
                  type="button"
                  onClick={onRefreshCache}
                >
                  {t('history.cacheRefresh')}
                </button>
              ) : null}
            </div>
          ) : null}

          {error ? (
            <div className="notice notice-error" role="alert">
              <Icon name="alert" />
              <div>
                <strong>{t('searchView.searchFailed')}</strong>
                <p>{error.message}</p>
                <button className="text-button" type="button" onClick={onRetry}>
                  {t('searchView.tryAgain')}
                </button>
              </div>
            </div>
          ) : null}

          {running && !hasResultItems ? (
            <div className="results-initial-loading" role="status">
              <span>{progress?.message ?? t('searchView.preparing')}</span>
              <span className="mono">
                {progress
                  ? t('searchView.pages', {
                      completed: progress.completed_pages,
                      total: progress.total_pages,
                    })
                  : t('searchView.queued')}
              </span>
            </div>
          ) : null}

          {!running && !error && results?.warnings.length ? (
            <div className="notice notice-warning" role="status">
              <Icon name="alert" />
              <div>
                <strong>{t('searchView.somePages')}</strong>
                {results.warnings.map((warning) => (
                  <p key={warning}>{warning}</p>
                ))}
              </div>
            </div>
          ) : null}

          {hasResultItems && results ? (
            <div
              className={`results-list-wrap${running ? ' is-updating' : ''}`}
              aria-busy={running}
            >
              {multiMarketplace ? (
                resultGroups.map((group) => (
                  <section
                    className="result-marketplace-group"
                    key={group.id}
                  >
                    <header className="result-marketplace-head">
                      <h3>{group.name}</h3>
                      <span>
                        {group.items.length === 1
                          ? t('searchView.resultCountOne')
                          : t('searchView.resultCountMany', {
                              count: formatNumber(group.items.length),
                            })}
                      </span>
                    </header>
                    <ResultsList
                      key={`${resultsVersion}:${layout}:${currentPage}:${group.id}`}
                      items={group.items}
                      currency={group.currency || results.currency}
                      layout={layout}
                      showDelivery={
                        form.filters.delivery || form.filters.freeShipping
                      }
                      onOpen={onOpenProduct}
                      isSaved={isSaved}
                      onToggleSave={onToggleSave}
                    />
                  </section>
                ))
              ) : (
                <ResultsList
                  key={`${resultsVersion}:${layout}:${currentPage}`}
                  items={filteredResults}
                  currency={
                    visibleMarketplaces.length === 1
                      ? visibleMarketplaces[0].currency || results.currency
                      : results.currency
                  }
                  layout={layout}
                  showDelivery={
                    form.filters.delivery || form.filters.freeShipping
                  }
                  onOpen={onOpenProduct}
                  isSaved={isSaved}
                  onToggleSave={onToggleSave}
                />
              )}
            </div>
          ) : null}

          {showPagination ? (
            <nav className="results-pagination" aria-label={t('searchView.pagination')}>
              <button
                className="pagination-step"
                type="button"
                onClick={() => goToPage(currentPage - 1)}
                disabled={running || currentPage <= 1}
                aria-label={t('searchView.previousPage')}
              >
                <Icon name="arrow-left" />
              </button>
              <div className="pagination-pages">
                {pageNumbers.map((pageNumber, index) => (
                  <Fragment key={pageNumber}>
                    {index > 0 &&
                    pageNumber - pageNumbers[index - 1] > 1 ? (
                      <span className="pagination-ellipsis" aria-hidden="true">
                        …
                      </span>
                    ) : null}
                    <button
                      className={
                        pageNumber === currentPage
                          ? 'pagination-page is-active'
                          : 'pagination-page'
                      }
                      type="button"
                      onClick={() => goToPage(pageNumber)}
                      disabled={running}
                      aria-label={t('searchView.page', { page: pageNumber })}
                      aria-current={
                        pageNumber === currentPage ? 'page' : undefined
                      }
                    >
                      {pageNumber}
                    </button>
                  </Fragment>
                ))}
              </div>
              <button
                className="pagination-step"
                type="button"
                onClick={() => goToPage(currentPage + 1)}
                disabled={running || currentPage >= pageCount}
                aria-label={t('searchView.nextPage')}
              >
                <Icon name="arrow-right" />
              </button>
              <span className="pagination-info">
                {currentPage} / {pageCount}
              </span>
            </nav>
          ) : null}

          {running && !hasResultItems ? (
            layout === 'grid' ? <SkeletonGrid /> : <SkeletonRows />
          ) : null}

          {!running && !error && results && filteredResults.length === 0 ? (
            <div className="empty-state">
              <Icon name="search" />
              {missingDomains.length > 0 && domainResults.length === 0 ? (
                <>
                  <h3>{t('searchView.notSearched')}</h3>
                  <p>
                    {t('searchView.notSearchedHelp', {
                      names: missingNames.join(', '),
                    })}
                  </p>
                </>
              ) : results.results.length > 0 ? (
                <>
                  <h3>{t('searchView.noFiltered')}</h3>
                  <p>
                    {t('searchView.noFilteredHelp', {
                      count: formatNumber(results.results.length),
                    })}
                  </p>
                </>
              ) : (
                <>
                  <h3>{t('searchView.noResults')}</h3>
                  <p>
                    {t('searchView.noResultsHelp')}
                  </p>
                </>
              )}
            </div>
          ) : null}
        </section>
      ) : (
        <section className="recent-section" aria-label={t('searchView.recentSearches')}>
          <div className="recent-head">
            <h2>{t('searchView.recentSearches')}</h2>
            {recentSearches.length > 0 ? (
              <div className="recent-head-tools">
                <span className="recent-head-count">
                  {recentSearches.length}
                </span>
                <button
                  className="recent-clear"
                  type="button"
                  onClick={onClearRecentSearches}
                  aria-label={t('searchView.clearAllAria')}
                  title={t('searchView.clearAllAria')}
                >
                  <Icon name="close" />
                  {t('searchView.clearAll')}
                </button>
              </div>
            ) : null}
          </div>

          {recentSearches.length > 0 ? (
            <ol className="recent-search-list">
              {recentSearches.map((item) => (
                <li
                  className="recent-search-item"
                  key={`${item.domain}:${item.query}`}
                >
                  <button
                    className="recent-search-row"
                    type="button"
                    onClick={() => onRecentSearch(item)}
                  >
                    <span className="recent-search-query">
                      <Icon name="search" />
                      <span>{item.query}</span>
                    </span>
                    <span className="recent-search-market">
                      {item.marketplace}
                    </span>
                    <time>{formatRecentTime(item.at, t)}</time>
                    <Icon name="arrow-right" className="recent-search-arrow" />
                  </button>
                  <button
                    className="recent-search-remove"
                    type="button"
                    onClick={() => onRemoveRecentSearch(item)}
                    aria-label={t('searchView.removeSearch', {
                      query: item.query,
                      marketplace: item.marketplace,
                    })}
                    title={t('searchView.remove')}
                  >
                    <Icon name="close" />
                  </button>
                </li>
              ))}
            </ol>
          ) : (
            <div className="search-empty">
              <p>{t('searchView.emptyRecent')}</p>
              <div className="example-row">
                {['ssd 1tb', 'bluetooth headphones', 'kindle paperwhite'].map(
                  (example) => (
                    <button
                      className="example-chip"
                      type="button"
                      key={example}
                      onClick={() => onExample(example)}
                    >
                      {example}
                    </button>
                  ),
                )}
              </div>
            </div>
          )}
        </section>
      )}
    </section>
  )
}
