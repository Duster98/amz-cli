import { useMemo, useState } from 'react'
import type { HistoryEntry } from '../lib/api'
import {
  hasHistoryFilters,
  historyFilterLabels,
} from '../lib/history'
import { useI18n } from '../i18n/I18nProvider'
import type { ResultsLayout } from './ResultsList'
import { Icon, ProductImage, Tag } from './ui'
import { ConfirmDialog } from './SavedProductsView'
import { formatPrice } from '../lib/format'

export type HistoryFilter = 'all' | 'search' | 'product'

export function HistoryView({
  entries,
  loading,
  error,
  filter,
  layout,
  onFilterChange,
  onLayoutChange,
  onOpenSearch,
  onOpenProduct,
  onDelete,
  onClear,
}: {
  entries: HistoryEntry[]
  loading: boolean
  error: string | null
  filter: HistoryFilter
  layout: ResultsLayout
  onFilterChange: (filter: HistoryFilter) => void
  onLayoutChange: (layout: ResultsLayout) => void
  onOpenSearch: (entry: HistoryEntry) => void
  onOpenProduct: (entry: HistoryEntry) => void
  onDelete: (entry: HistoryEntry) => void
  onClear: () => void
}) {
  const { t } = useI18n()
  const [confirmClear, setConfirmClear] = useState(false)
  const searches = entries.filter((entry) => entry.kind === 'search').length
  const products = entries.filter((entry) => entry.kind === 'product').length
  const visible = useMemo(
    () =>
      filter === 'all'
        ? entries
        : entries.filter((entry) => entry.kind === filter),
    [entries, filter],
  )

  return (
    <section className="history-view">
      <header className="saved-header history-header">
        <div className="history-top-row">
          <div className="saved-heading">
            <h1>{t('history.title')}</h1>
          </div>
          <button
            className="history-clear"
            type="button"
            disabled={entries.length === 0}
            onClick={() => setConfirmClear(true)}
          >
            <Icon name="trash" />
            {t('history.clear')}
          </button>
        </div>
        <div className="saved-header-actions">
          <div
            className="saved-tabs history-tabs"
            role="tablist"
            aria-label={t('history.filter')}
          >
            <HistoryFilterButton
              active={filter === 'all'}
              label={t('history.filterAll')}
              count={entries.length}
              onClick={() => onFilterChange('all')}
            />
            <HistoryFilterButton
              active={filter === 'search'}
              label={t('history.filterSearches')}
              count={searches}
              onClick={() => onFilterChange('search')}
            />
            <HistoryFilterButton
              active={filter === 'product'}
              label={t('history.filterProducts')}
              count={products}
              onClick={() => onFilterChange('product')}
            />
          </div>
          <div className="results-layout" role="group" aria-label={t('saved.layout')}>
            <button
              type="button"
              className={layout === 'list' ? 'is-active' : ''}
              aria-pressed={layout === 'list'}
              title={t('saved.listView')}
              onClick={() => onLayoutChange('list')}
            >
              <Icon name="list" />
            </button>
            <button
              type="button"
              className={layout === 'grid' ? 'is-active' : ''}
              aria-pressed={layout === 'grid'}
              title={t('saved.gridView')}
              onClick={() => onLayoutChange('grid')}
            >
              <Icon name="grid" />
            </button>
          </div>
        </div>
      </header>

      {error ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('history.loadFailed')}</strong>
            <p>{error}</p>
          </div>
        </div>
      ) : null}

      {loading ? (
        <div className="history-loading" aria-busy="true">
          <span className="skeleton history-loading-line" />
          <span className="skeleton history-loading-line" />
          <span className="skeleton history-loading-line short" />
        </div>
      ) : visible.length === 0 ? (
        <div className="empty-state saved-empty">
          <Icon name="clock" />
          <h3>{t('history.emptyTitle')}</h3>
          <p>{t('history.emptyBody')}</p>
        </div>
      ) : (
        <div className={`history-entries history-entries--${layout}`}>
          {visible.map((entry) =>
            entry.kind === 'search' ? (
              <HistorySearchCard
                key={entry.id}
                entry={entry}
                layout={layout}
                onOpen={() => onOpenSearch(entry)}
                onDelete={() => onDelete(entry)}
              />
            ) : (
              <HistoryProductCard
                key={entry.id}
                entry={entry}
                layout={layout}
                onOpen={() => onOpenProduct(entry)}
                onDelete={() => onDelete(entry)}
              />
            ),
          )}
        </div>
      )}

      {confirmClear ? (
        <ConfirmDialog
          title={t('history.clearTitle')}
          body={t('history.clearBody')}
          confirmLabel={t('history.clear')}
          onConfirm={() => {
            setConfirmClear(false)
            onClear()
          }}
          onClose={() => setConfirmClear(false)}
        />
      ) : null}
    </section>
  )
}

function HistoryFilterButton({
  active,
  label,
  count,
  onClick,
}: {
  active: boolean
  label: string
  count: number
  onClick: () => void
}) {
  const { formatNumber } = useI18n()
  return (
    <button
      type="button"
      role="tab"
      className={`saved-tab${active ? ' is-active' : ''}`}
      aria-selected={active}
      onClick={onClick}
    >
      {label}
      <span className="saved-tab-count">{formatNumber(count)}</span>
    </button>
  )
}

function HistorySourceTag({ entry }: { entry: HistoryEntry }) {
  const { t } = useI18n()
  if (entry.source === 'agent') {
    return <Tag tone="gold">{t('history.sourceAgent')}</Tag>
  }
  if (entry.source === 'cli') {
    return <Tag tone="quiet">{t('history.sourceCli')}</Tag>
  }
  return <Tag tone="quiet">{t('history.sourceWeb')}</Tag>
}

function HistoryStatusTag({ entry }: { entry: HistoryEntry }) {
  const { t } = useI18n()
  if (entry.status === 'running') {
    return (
      <span className="history-status is-running">
        <span className="history-spinner" aria-hidden="true" />
        {t('history.statusRunning')}
      </span>
    )
  }
  if (entry.status === 'failed') {
    return <Tag tone="rust">{t('history.statusFailed')}</Tag>
  }
  if (entry.status === 'cancelled') {
    return <Tag tone="rust">{t('history.statusCancelled')}</Tag>
  }
  if (entry.status === 'interrupted') {
    return <Tag tone="rust">{t('history.statusInterrupted')}</Tag>
  }
  return entry.cached ? (
    <Tag tone="blue">{t('history.cached')}</Tag>
  ) : null
}

function HistorySearchCard({
  entry,
  layout,
  onOpen,
  onDelete,
}: {
  entry: HistoryEntry
  layout: ResultsLayout
  onOpen: () => void
  onDelete: () => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const running = entry.status === 'running'
  const filters = entry.filters
  const filterLabels = historyFilterLabels(filters, t, 'EUR', languageLocale)
  const hasFilters = hasHistoryFilters(filters)
  const date = formatHistoryDate(entry.at, languageLocale)
  return (
    <article
      className={`history-entry history-entry--search history-entry--${layout}`}
    >
      <button
        className="history-entry-main"
        type="button"
        disabled={running}
        onClick={onOpen}
        title={running ? t('history.runningHint') : undefined}
      >
        <span className="history-entry-icon" aria-hidden="true">
          <Icon name="search" />
        </span>
        <span className="history-entry-copy">
          <span className="history-entry-topline">
            <span className="history-entry-kind">{t('history.search')}</span>
            <HistorySourceTag entry={entry} />
            <HistoryStatusTag entry={entry} />
          </span>
          <span className="history-entry-title">{entry.query}</span>
          <span className="history-entry-meta">
            <span>{entry.domain?.toUpperCase() ?? '—'}</span>
            <span className="saved-dot" aria-hidden="true" />
            <span>{date}</span>
            {entry.result_count !== undefined ? (
              <>
                <span className="saved-dot" aria-hidden="true" />
                <span>
                  {t(
                  entry.result_count === 1
                    ? 'history.resultsOne'
                    : 'history.results',
                  { count: formatNumber(entry.result_count) },
                )}
                </span>
              </>
            ) : null}
          </span>
          {hasFilters ? (
            <span className="history-filter-chips">
              <span className="history-filter-count">
                <Icon name="filter" />
                {t(
                  filterLabels.length === 1
                    ? 'history.filterCountOne'
                    : 'history.filterCount',
                  { count: formatNumber(filterLabels.length) },
                )}
              </span>
              {filterLabels.slice(0, 3).map((label) => (
                <Tag tone="neutral" key={label}>
                  {label}
                </Tag>
              ))}
              {filterLabels.length > 3 ? (
                <Tag tone="quiet">+{filterLabels.length - 3}</Tag>
              ) : null}
            </span>
          ) : (
            <span className="history-no-filters">{t('history.noFilters')}</span>
          )}
          {entry.error ? (
            <span className="history-entry-error">{entry.error}</span>
          ) : null}
        </span>
        <span className="history-entry-action" aria-hidden="true">
          {running ? null : <Icon name="arrow-right" />}
        </span>
      </button>
      <button
        className="history-delete"
        type="button"
        title={t('history.delete')}
        aria-label={t('history.delete')}
        onClick={onDelete}
      >
        <Icon name="close" />
      </button>
    </article>
  )
}

function HistoryProductCard({
  entry,
  layout,
  onOpen,
  onDelete,
}: {
  entry: HistoryEntry
  layout: ResultsLayout
  onOpen: () => void
  onDelete: () => void
}) {
  const { t, languageLocale } = useI18n()
  const snapshot = entry.snapshot
  const price = formatPrice(
    snapshot?.price_amount ?? null,
    snapshot?.currency,
    snapshot?.price ?? '',
    languageLocale,
  )
  return (
    <article
      className={`history-entry history-entry--product history-entry--${layout}`}
    >
      <button className="history-entry-main" type="button" onClick={onOpen}>
        <span className="history-entry-image">
          <ProductImage src={snapshot?.image} alt={snapshot?.title ?? ''} />
        </span>
        <span className="history-entry-copy">
          <span className="history-entry-topline">
            <span className="history-entry-kind">{t('history.product')}</span>
            <HistorySourceTag entry={entry} />
            {entry.cached ? (
              <Tag tone="blue">{t('history.detailCached')}</Tag>
            ) : null}
          </span>
          <span className="history-entry-title">{snapshot?.title}</span>
          <span className="history-entry-meta">
            <span>{entry.domain?.toUpperCase() ?? '—'}</span>
            <span className="saved-dot" aria-hidden="true" />
            <span>{formatHistoryDate(entry.at, languageLocale)}</span>
          </span>
          <span className="history-entry-price">{price}</span>
        </span>
        <span className="history-entry-action" aria-hidden="true">
          <Icon name="arrow-right" />
        </span>
      </button>
      <button
        className="history-delete"
        type="button"
        title={t('history.delete')}
        aria-label={t('history.delete')}
        onClick={onDelete}
      >
        <Icon name="close" />
      </button>
    </article>
  )
}

function formatHistoryDate(value: string, locale: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}
