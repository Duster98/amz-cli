import {
  Fragment,
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type {
  SearchItem,
  DealCategory,
  DealType,
  Marketplace,
  SearchJob,
  SearchResultPayload,
} from '../lib/api'
import { formatDuration } from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/types'
import { ResultsList, type ResultsLayout } from './ResultsList'
import { Icon, SkeletonGrid, SkeletonRows } from './ui'

const DEAL_TYPE_LABELS: Record<DealType, TranslationKey> = {
  all: 'deals.all',
  today: 'deals.today',
  coupons: 'deals.coupons',
}

const STICKY_RELEASE_PX = 8

function HorizontalScroller({
  className,
  ariaLabel,
  resetKey,
  children,
}: {
  className: string
  ariaLabel: string
  resetKey?: string
  children: ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [edges, setEdges] = useState({ left: false, right: false })

  const syncEdges = useCallback(() => {
    const element = ref.current
    if (!element) {
      return
    }
    const maximum = element.scrollWidth - element.clientWidth
    const left = element.scrollLeft > 1
    const right = maximum > 1 && element.scrollLeft < maximum - 1
    setEdges((current) =>
      current.left === left && current.right === right
        ? current
        : { left, right },
    )
  }, [])

  useEffect(() => {
    const element = ref.current
    if (element && resetKey !== undefined) {
      element.scrollLeft = 0
    }
    syncEdges()
    window.requestAnimationFrame(syncEdges)
  }, [resetKey, syncEdges])

  useEffect(() => {
    syncEdges()
  })

  useEffect(() => {
    const element = ref.current
    if (!element) {
      return
    }

    const onWheel = (event: WheelEvent) => {
      if (element.scrollWidth <= element.clientWidth) {
        return
      }
      const delta =
        Math.abs(event.deltaY) >= Math.abs(event.deltaX)
          ? event.deltaY
          : event.deltaX
      if (delta === 0) {
        return
      }
      const factor =
        event.deltaMode === 1
          ? 18
          : event.deltaMode === 2
            ? element.clientWidth
            : 1
      event.preventDefault()
      element.scrollLeft += delta * factor
    }

    const onResize = () => syncEdges()
    element.addEventListener('wheel', onWheel, { passive: false })
    window.addEventListener('resize', onResize)
    return () => {
      element.removeEventListener('wheel', onWheel)
      window.removeEventListener('resize', onResize)
    }
  }, [syncEdges])

  return (
    <div
      className={`deals-scroll-shell${edges.left ? ' can-left' : ''}${edges.right ? ' can-right' : ''}`}
    >
      <div
        ref={ref}
        className={className}
        role="group"
        aria-label={ariaLabel}
        onScroll={syncEdges}
      >
        {children}
      </div>
    </div>
  )
}

function buildPageNumbers(current: number, total: number): number[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, index) => index + 1)
  }

  return [...new Set([1, 2, current - 1, current, current + 1, total - 1, total])]
    .filter((page) => page >= 1 && page <= total)
    .sort((left, right) => left - right)
}

export function DealsView({
  domain,
  marketplaces,
  dealType,
  dealTypes,
  categories,
  category,
  categoriesLoading,
  categoriesError,
  results,
  job,
  running,
  error,
  layout,
  onDomainChange,
  onTypeChange,
  onCategoryChange,
  onPageChange,
  onRefresh,
  onLayoutChange,
  onOpenProduct,
  onOpenSearch,
  onRetry,
  isSaved,
  onToggleSave,
}: {
  domain: string
  marketplaces: Marketplace[]
  dealType: DealType
  dealTypes: DealType[]
  categories: DealCategory[]
  category: string | null
  categoriesLoading: boolean
  categoriesError: string | null
  results: SearchResultPayload | null
  job: SearchJob | null
  running: boolean
  error: { code: string; message: string } | null
  layout: ResultsLayout
  onDomainChange: (id: string) => void
  onTypeChange: (type: DealType) => void
  onCategoryChange: (nodeId: string | null) => void
  onPageChange: (page: number) => void
  onRefresh: () => void
  onLayoutChange: (layout: ResultsLayout) => void
  onOpenProduct: (asin: string, marketplaceId?: string | null) => void
  onOpenSearch: () => void
  onRetry: () => void
  isSaved?: (item: SearchItem) => boolean
  onToggleSave?: (item: SearchItem) => void
}) {
  const [scrolled, setScrolled] = useState(false)
  const { t, formatNumber } = useI18n()
  const pageRef = useRef<HTMLElement>(null)
  const dockRef = useRef<HTMLDivElement>(null)
  const resultsRef = useRef<HTMLElement>(null)

  const syncDock = useCallback(() => {
    const page = pageRef.current
    const dock = dockRef.current
    if (!page || !dock) {
      return
    }
    page.style.setProperty(
      '--deals-dock-height',
      `${dock.offsetHeight}px`,
    )
  }, [])

  useEffect(() => {
    let frame = 0
    let docked = false
    const update = () => {
      frame = 0
      const dock = dockRef.current
      if (!dock) {
        return
      }

      const top = dock.getBoundingClientRect().top
      // Compacting the dock changes its height. A small release threshold
      // keeps the state stable when the dock sits exactly on the edge.
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
    if (dockRef.current) {
      observer?.observe(dockRef.current)
    }
    const frame = window.requestAnimationFrame(syncDock)
    return () => {
      window.cancelAnimationFrame(frame)
      observer?.disconnect()
    }
  }, [syncDock])

  const marketplace =
    marketplaces.find((item) => item.id === domain) ?? null
  const selectedCategory = categories.find(
    (item) => item.node_id === category,
  )
  const pageCount = results
    ? Math.max(1, results.total_available_pages)
    : 1
  const currentPage = results?.requested_pages[0] ?? 1
  const pageNumbers = buildPageNumbers(currentPage, pageCount)
  const resultContext = [
    marketplace?.display_name,
    selectedCategory?.label,
  ]
    .filter((value): value is string => Boolean(value))
    .join(' · ')
  const progress = job?.progress
  const stage = progress?.stage
  const percent = !progress
    ? 8
    : stage === 'done'
      ? 100
      : stage === 'enriching'
        ? 88
        : stage === 'searching'
          ? Math.min(
              82,
              10 +
                (progress.total_pages > 0
                  ? (progress.completed_pages / progress.total_pages) * 72
                  : 0),
            )
          : 8

  const goToPage = (nextPage: number) => {
    const bounded = Math.min(Math.max(1, nextPage), pageCount)
    if (bounded === currentPage) {
      return
    }
    onPageChange(bounded)
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    window.requestAnimationFrame(() => {
      resultsRef.current?.scrollIntoView({
        behavior: reduced ? 'auto' : 'smooth',
        block: 'start',
      })
    })
  }

  if (error) {
    return (
      <section className="deals-page" ref={pageRef}>
        <header className="page-head deals-page-head">
          <div>
            <h1>{t('deals.title')}</h1>
            <p>{t('deals.subtitle')}</p>
          </div>
          <button className="text-button deals-back-search" type="button" onClick={onOpenSearch}>
            {t('deals.backToSearch')}
            <Icon name="arrow-right" />
          </button>
        </header>
        <div className="notice notice-error" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('deals.notAvailable')}</strong>
            <p>{error.message}</p>
            <button className="text-button" type="button" onClick={onRetry}>
              {t('app.tryAgain')}
            </button>
          </div>
        </div>
      </section>
    )
  }

  return (
    <section className="deals-page" ref={pageRef}>
      <header className="page-head deals-page-head">
        <div>
          <h1>{t('deals.title')}</h1>
          <p>
            {dealType === 'today'
              ? t('deals.todayDeals')
              : dealType === 'coupons'
                ? t('deals.activeCoupons')
                : t('deals.allActive')}
            {marketplace
              ? ` ${t('deals.onMarketplace', {
                  marketplace: marketplace.display_name,
                })}`
              : ''}
            {selectedCategory ? ` · ${selectedCategory.label}` : ''}
          </p>
        </div>
        <button
          className="text-button deals-back-search"
          type="button"
          onClick={onOpenSearch}
        >
          {t('deals.searchProducts')}
          <Icon name="search" />
        </button>
      </header>

      <div
        className={`deals-dock${scrolled ? ' is-scrolled' : ''}`}
        ref={dockRef}
      >
        <div className="deals-dock-inner">
          <span className="filter-label deals-control-label">{t('deals.marketplace')}</span>
          <HorizontalScroller
            className="deals-market-pills"
            ariaLabel={t('deals.dealMarketplaces')}
          >
            {marketplaces.map((item) => (
              <button
                className={`deals-market-pill${item.id === domain ? ' is-selected' : ''}`}
                type="button"
                key={item.id}
                aria-pressed={item.id === domain}
                onClick={() => onDomainChange(item.id)}
              >
                {item.display_name}
              </button>
            ))}
          </HorizontalScroller>

          <span className="filter-label deals-control-label">{t('deals.type')}</span>
          <div className="deals-type-controls">
            <div
              className="deals-tabs"
              role="group"
              aria-label={t('deals.dealType')}
            >
              {dealTypes.map((type) => (
                <button
                  className={`deals-tab${dealType === type ? ' is-active' : ''}`}
                  type="button"
                  key={type}
                  aria-pressed={dealType === type}
                  onClick={() => onTypeChange(type)}
                >
                  {t(DEAL_TYPE_LABELS[type])}
                </button>
              ))}
            </div>
            <button
              className="deals-refresh"
              type="button"
              onClick={onRefresh}
              disabled={running}
              aria-label={t('deals.refreshDeals')}
              title={t('deals.refreshDeals')}
            >
              <Icon name="refresh" />
              <span>{t('deals.refresh')}</span>
            </button>
          </div>

          <span className="filter-label deals-control-label">{t('deals.category')}</span>
          <HorizontalScroller
            className="deals-category-strip"
            ariaLabel={t('deals.dealCategory')}
            resetKey={`${domain}:${dealType}`}
          >
            <button
              className={`deals-category-pill${category === null ? ' is-selected' : ''}`}
              type="button"
              aria-pressed={category === null}
              onClick={() => onCategoryChange(null)}
            >
              {t('deals.all')}
            </button>
            {categoriesLoading ? (
              <span
                className="skeleton deals-category-skeleton"
                aria-hidden="true"
              />
            ) : (
              categories.map((item) => (
                <button
                  className={`deals-category-pill${category === item.node_id ? ' is-selected' : ''}`}
                  type="button"
                  key={item.node_id}
                  aria-pressed={category === item.node_id}
                  title={item.label}
                  onClick={() => onCategoryChange(item.node_id)}
                >
                  {item.label}
                </button>
              ))
            )}
          </HorizontalScroller>
        </div>
      </div>

      <section
        className="deals-results"
        aria-label={t('deals.title')}
        ref={resultsRef}
      >
        <div
          className={`results-head deals-results-head${running ? ' is-running' : ''}${scrolled ? ' is-docked' : ''}`}
        >
          <div className="results-title-row">
            <h2>{t('deals.results')}</h2>
            <div className="results-head-tools">
              {running ? (
                <span className="results-updating">
                  <span className="results-updating-label">
                    {progress?.message ??
                      (categoriesLoading
                        ? t('deals.preparing')
                        : t('deals.updating'))}
                  </span>
                </span>
              ) : results ? (
                <span className="results-duration">
                  {formatDuration(results.elapsed_ms)}
                </span>
              ) : null}
              <div className="results-layout" role="group" aria-label={t('deals.view')}>
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
            </div>
          </div>
          <p className="results-sub">
            {results
              ? [
                  t('deals.dealsCount', {
                    count: formatNumber(results.results.length),
                  }),
                  resultContext,
                  pageCount > 1
                    ? t('deals.pageOf', {
                        page: currentPage,
                        pages: pageCount,
                      })
                    : null,
                ]
                  .filter(Boolean)
                  .join(' · ')
              : running
                ? categoriesLoading
                  ? t('deals.preparing')
                  : t('deals.reading')
                : t('deals.chooseType')}
          </p>
          {running ? (
            <span
              className="results-progress-line"
              style={{ width: `${percent}%` }}
              aria-hidden="true"
            />
          ) : null}
        </div>

        {categoriesError && !categoriesLoading ? (
          <p className="deals-categories-error" role="status">
            {categoriesError}
          </p>
        ) : null}

        {running && !results ? (
          layout === 'grid' ? <SkeletonGrid /> : <SkeletonRows />
        ) : null}

        {!running && results && results.results.length > 0 ? (
          <div className="results-list-wrap">
            <ResultsList
              items={results.results}
              currency={results.currency}
              layout={layout}
              onOpen={onOpenProduct}
              isSaved={isSaved}
              onToggleSave={onToggleSave}
            />
          </div>
        ) : null}

        {!running && results && results.results.length === 0 ? (
          <div className="empty-state">
            <Icon name="tag" />
            <h3>{t('deals.noDeals')}</h3>
            <p>
              {t('deals.noDealsHelp')}
            </p>
            {category ? (
              <button
                className="text-button"
                type="button"
                onClick={() => onCategoryChange(null)}
              >
                {t('deals.showAllCategories')}
              </button>
            ) : null}
          </div>
        ) : null}

        {pageCount > 1 && results && results.results.length > 0 ? (
          <nav className="results-pagination" aria-label={t('deals.pagination')}>
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
      </section>
    </section>
  )
}
