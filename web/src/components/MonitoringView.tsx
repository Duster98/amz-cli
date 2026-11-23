import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from 'react'
import { createPortal } from 'react-dom'
import type {
  Library,
  MonitorBinding,
  MonitorBindingInput,
  MonitorExternalRun,
  MonitorItemRule,
  MonitorOverview,
  MonitorRuntimeInput,
  MonitorSeriesResponse,
} from '../lib/api'
import {
  createMonitor,
  deleteMonitor,
  getMonitorSeries,
  getMonitors,
  installMonitorStartup,
  removeMonitorStartup,
  restartMonitor,
  startMonitor,
  stopMonitor,
  testMonitorNotification,
  updateMonitor,
  verifyMonitorStartup,
} from '../lib/api'
import { useI18n } from '../i18n/I18nProvider'
import type { Translate } from '../i18n/types'
import { formatPrice } from '../lib/format'
import { ConfirmDialog, Modal } from './SavedProductsView'
import { PriceChart, type ChartRange } from './PriceChart'
import type { ResultsLayout } from './ResultsList'
import { Icon, ProductImage, Tag } from './ui'

type EditorState =
  | { mode: 'create' }
  | { mode: 'edit'; binding: MonitorBinding }
  | null

export function MonitoringView({
  library,
  revision,
  layout,
  onLayoutChange,
}: {
  library: Library
  revision: number
  layout: ResultsLayout
  onLayoutChange: (layout: ResultsLayout) => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const [overview, setOverview] = useState<MonitorOverview | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [series, setSeries] = useState<MonitorSeriesResponse | null>(null)
  const [seriesLoading, setSeriesLoading] = useState(false)
  const [seriesError, setSeriesError] = useState<string | null>(null)
  const [range, setRange] = useState<ChartRange>('30d')
  const [editor, setEditor] = useState<EditorState>(null)
  const [deleteTarget, setDeleteTarget] = useState<MonitorBinding | null>(null)
  const [stopTarget, setStopTarget] = useState<MonitorBinding | null>(null)
  const [actionId, setActionId] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const refresh = useCallback(async (quiet = false) => {
    if (!quiet) {
      setLoading(true)
    }
    try {
      const next = await getMonitors()
      setOverview(next)
      setError(null)
      setSelectedId((current) =>
        current && next.monitors.some((item) => item.id === current)
          ? current
          : null,
      )
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      if (!quiet) {
        setLoading(false)
      }
    }
  }, [t])

  useEffect(() => {
    void refresh()
    const timer = window.setInterval(() => void refresh(true), 5000)
    return () => window.clearInterval(timer)
  }, [refresh, revision])

  const selected =
    overview?.monitors.find((item) => item.id === selectedId) ?? null

  useEffect(() => {
    if (!selected) {
      setSeries(null)
      setSeriesError(null)
      return
    }
    let active = true
    setSeriesLoading(true)
    setSeriesError(null)
    void getMonitorSeries(selected.id, range, 500)
      .then((data) => {
        if (active) {
          setSeries(data)
        }
      })
      .catch((caught) => {
        if (active) {
          setSeriesError(
            caught instanceof Error ? caught.message : t('app.unexpectedError'),
          )
        }
      })
      .finally(() => {
        if (active) {
          setSeriesLoading(false)
        }
      })
    const timer = window.setInterval(() => {
      void getMonitorSeries(selected.id, range, 500)
        .then((data) => {
          if (active) {
            setSeries(data)
          }
        })
        .catch(() => undefined)
    }, 10000)
    return () => {
      active = false
      window.clearInterval(timer)
    }
  }, [range, revision, selected?.id, t])

  const replaceBinding = (next: MonitorBinding) => {
    setOverview((current) =>
      current
        ? {
            ...current,
            monitors: current.monitors.map((item) =>
              item.id === next.id ? next : item,
            ),
          }
        : current,
    )
  }

  const runAction = async (
    binding: MonitorBinding,
    action: 'start' | 'stop' | 'restart',
  ) => {
    setActionId(binding.id)
    setError(null)
    setNotice(null)
    try {
      const next =
        action === 'start'
          ? await startMonitor(binding.id)
          : action === 'stop'
            ? await stopMonitor(binding.id)
            : await restartMonitor(binding.id)
      replaceBinding(next)
      setNotice(
        action === 'stop'
          ? binding.startup.configured
            ? t('monitoring.stoppedStartup')
            : t('monitoring.stopped')
          : next.startup?.message ?? null,
      )
      if (selectedId === next.id) {
        setRange((current) => current)
      }
      await refresh(true)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      setActionId(null)
    }
  }

  const requestStop = (binding: MonitorBinding) => {
    if (binding.startup.configured) {
      setStopTarget(binding)
      return
    }
    void runAction(binding, 'stop')
  }

  const runStartupAction = async (
    binding: MonitorBinding,
    action: 'install' | 'remove' | 'verify',
  ) => {
    setActionId(binding.id)
    setError(null)
    setNotice(null)
    try {
      const next =
        action === 'install'
          ? await installMonitorStartup(binding.id)
          : action === 'remove'
            ? await removeMonitorStartup(binding.id)
            : await verifyMonitorStartup(binding.id)
      replaceBinding(next)
      setNotice(next.startup?.message ?? null)
      await refresh(true)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      setActionId(null)
    }
  }

  const runNotifyTest = async (binding: MonitorBinding) => {
    setActionId(binding.id)
    setError(null)
    setNotice(null)
    try {
      const result = await testMonitorNotification(binding.id)
      if (result.success) {
        setNotice(t('monitoring.notifyTestSuccess', { backend: result.backend }))
      } else {
        setError(result.error ?? t('monitoring.notifyTestFailed'))
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      setActionId(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) {
      return
    }
    setActionId(deleteTarget.id)
    try {
      await deleteMonitor(deleteTarget.id)
      if (selectedId === deleteTarget.id) {
        setSelectedId(null)
      }
      setDeleteTarget(null)
      await refresh(true)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      setActionId(null)
    }
  }

  const openEditor = (binding?: MonitorBinding) => {
    setEditor(binding ? { mode: 'edit', binding } : { mode: 'create' })
  }

  if (selected) {
    const seriesPrices = (series?.points ?? [])
      .map((point) => point.price)
      .filter((value) => Number.isFinite(value))
    const seriesFirst = seriesPrices.length > 0 ? seriesPrices[0] : null
    const seriesMin = seriesPrices.length > 0 ? Math.min(...seriesPrices) : null
    const seriesMax = seriesPrices.length > 0 ? Math.max(...seriesPrices) : null
    const statsCurrency =
      selected.stats.currency ?? series?.currency ?? selected.target.currency
    const hasHistory =
      selected.stats.observations > 0 ||
      selected.stats.last_seen_at != null ||
      selected.run?.last_seen_at != null
    const minValue = selected.kind === 'group'
      ? selected.stats.min_total ?? seriesMin
      : selected.stats.min_price ?? seriesMin
    const maxValue = selected.kind === 'group'
      ? selected.stats.max_total ?? seriesMax
      : selected.stats.max_price ?? seriesMax
    const firstValue = selected.kind === 'group'
      ? seriesFirst
      : selected.stats.first_price ?? seriesFirst
    const dropThreshold = series?.thresholds.find(
      (threshold) => threshold.label === 'drop_percent',
    )
    const riseThreshold = series?.thresholds.find(
      (threshold) => threshold.label === 'rise_percent',
    )
    return (
      <section className="monitoring-view">
        <header className="monitor-detail-hero">
          <button
            className="back-button"
            type="button"
            onClick={() => setSelectedId(null)}
          >
            <Icon name="arrow-left" />
            {t('monitoring.back')}
          </button>
          <div className="monitor-detail-kicker">
            <MonitorStatus status={selected.status} />
            {selected.kind === 'product' &&
            (selected.target.marketplace || selected.target.asin) ? (
              <span
                className="product-meta-chip monitor-detail-chip"
                title={[selected.target.marketplace, selected.target.asin]
                  .filter(Boolean)
                  .join(' · ')}
              >
                {selected.target.marketplace ? (
                  <>
                    <Icon name="globe" />
                    <span>{selected.target.marketplace}</span>
                  </>
                ) : null}
                {selected.target.marketplace && selected.target.asin ? (
                  <span className="product-meta-sep" aria-hidden="true">
                    ·
                  </span>
                ) : null}
                {selected.target.asin ? (
                  <span className="mono">{selected.target.asin}</span>
                ) : null}
              </span>
            ) : null}
            {selected.kind === 'group' ? (
              <>
                {selected.target.marketplace ? (
                  <span className="monitor-detail-meta">
                    <Icon name="globe" />
                    {selected.target.marketplace}
                  </span>
                ) : null}
                <span className="monitor-detail-meta">
                  {t('monitoring.groupTarget', {
                    count: formatNumber(selected.target.products),
                  })}
                </span>
              </>
            ) : null}
          </div>
          <div className="monitor-detail-heading">
            <h1>{selected.target.title ?? selected.name ?? selected.id}</h1>
          </div>
          <div className="monitor-detail-actions">
            <div className="monitor-detail-action-group">
              {selected.target.url ? (
                <a
                  className="monitor-detail-link"
                  href={selected.target.url}
                  target="_blank"
                  rel="noreferrer"
                >
                  <Icon name="external" />
                  {t('monitoring.openProduct')}
                </a>
              ) : null}
              <button
                className="text-button monitor-detail-text-action"
                type="button"
                onClick={() => openEditor(selected)}
              >
                {t('monitoring.edit')}
              </button>
              {selected.status === 'running' ? (
                <button
                  className="text-button monitor-detail-text-action"
                  type="button"
                  disabled={actionId === selected.id}
                  onClick={() => void runAction(selected, 'restart')}
                >
                  {t('monitoring.restart')}
                </button>
              ) : null}
            </div>
            <div className="monitor-detail-primary-group">
              {selected.can_stop ? (
                <button
                  className="monitor-control monitor-control--stop"
                  type="button"
                  disabled={actionId === selected.id}
                  onClick={() => requestStop(selected)}
                >
                  <Icon name="stop" />
                  {t('monitoring.stop')}
                </button>
              ) : (
                <button
                  className="monitor-control monitor-control--primary"
                  type="button"
                  disabled={!selected.can_start || actionId === selected.id}
                  onClick={() => void runAction(selected, 'start')}
                >
                  <Icon name="play" />
                  {hasHistory
                    ? t('monitoring.resume')
                    : t('monitoring.start')}
                </button>
              )}
              <MonitorDetailMenu
                disabled={actionId === selected.id}
                onDelete={() => setDeleteTarget(selected)}
              />
            </div>
          </div>
        </header>

        {error ? <MonitorError message={error} /> : null}
        {selected.error ? <MonitorError message={selected.error} /> : null}
        {selected.restart_required ? (
          <div className="notice notice-warning" role="status">
            <Icon name="refresh" />
            <div>
              <p>{t('monitoring.restartRequired')}</p>
            </div>
          </div>
        ) : null}
        {notice ? (
          <div className="notice" role="status">
            <Icon name="check" />
            <div>
              <p>{notice}</p>
            </div>
          </div>
        ) : null}

        <section className="monitor-panel monitor-chart-panel">
          <div className="monitor-chart-heading">
            <div>
              <h2>{t('monitoring.priceTrend')}</h2>
              <p>{t('monitoring.priceTrendBody')}</p>
            </div>
            <div className="monitor-threshold-list">
              {selected.kind === 'group' && selected.total_max != null ? (
                <Tag tone="gold">
                  {t('monitoring.totalMax')}{' '}
                  {formatStat(
                    selected.total_max,
                    selected.stats.currency ?? selected.currency,
                    languageLocale,
                  )}
                </Tag>
              ) : null}
              {selected.max_price != null ? (
                <Tag tone="gold">
                  {t('monitoring.maxPrice')}{' '}
                  {formatStat(
                    selected.max_price,
                    selected.target.currency,
                    languageLocale,
                  )}
                </Tag>
              ) : null}
              {selected.drop_percent != null ? (
                <Tag tone="blue">
                  {t('monitoring.dropPercent')} {selected.drop_percent}%
                  {dropThreshold
                    ? ` · ${formatStat(
                        dropThreshold.value,
                        statsCurrency,
                        languageLocale,
                      )}`
                    : ''}
                </Tag>
              ) : null}
              {selected.rise_percent != null ? (
                <Tag tone="rust">
                  {t('monitoring.risePercent')} {selected.rise_percent}%
                  {riseThreshold
                    ? ` · ${formatStat(
                        riseThreshold.value,
                        statsCurrency,
                        languageLocale,
                      )}`
                    : ''}
                </Tag>
              ) : null}
            </div>
          </div>
          <PriceChart
            points={series?.points ?? []}
            alerts={series?.alerts ?? []}
            thresholds={series?.thresholds ?? []}
            currency={series?.currency ?? selected.stats.currency}
            range={range}
            onRangeChange={setRange}
            loading={seriesLoading}
            error={seriesError}
            emptyLabel={t('monitoring.chartEmpty')}
            observations={series?.observations}
          >
            <dl className="monitor-chart-stats">
              <Stat
                label={t('monitoring.minimum')}
                value={formatStat(minValue, statsCurrency, languageLocale)}
              />
              <Stat
                label={t('monitoring.maximum')}
                value={formatStat(maxValue, statsCurrency, languageLocale)}
              />
              <Stat
                label={t('monitoring.first')}
                value={formatStat(firstValue, statsCurrency, languageLocale)}
              />
              <Stat
                label={t('monitoring.observationsLabel')}
                value={formatNumber(
                  selected.stats.observations || series?.observations || 0,
                )}
              />
              <Stat
                label={t('monitoring.lastCheck')}
                value={formatDate(
                  selected.run?.last_seen_at ?? selected.stats.last_seen_at,
                  languageLocale,
                )}
              />
            </dl>
          </PriceChart>
          {series?.warnings?.length ? (
            <div className="monitor-warning-list">
              {series.warnings.map((warning) => (
                <p key={warning}>{warning}</p>
              ))}
            </div>
          ) : null}
        </section>

        <div className="monitor-secondary-grid">
          <section className="monitor-panel">
            <div className="monitor-panel-heading">
              <div>
                <h2>{t('monitoring.configuration')}</h2>
                <p>{t('monitoring.configurationBody')}</p>
              </div>
            </div>
          <dl className="monitor-config">
            <Stat label={t('monitoring.timer')} value={formatTimer(selected.runtime.timer_seconds)} />
            <Stat
              label={t('monitoring.requestDelay')}
              value={`${selected.runtime.request_delay_seconds}s`}
            />
            <Stat
              label={t('monitoring.notifications')}
              value={selected.runtime.notify ? t('monitoring.enabled') : t('monitoring.disabled')}
            />
            <Stat label={t('monitoring.savePath')} value={selected.runtime.save_path} mono />
            <Stat
              label={t('monitoring.lastCheck')}
              value={formatDate(
                selected.run?.last_seen_at ?? selected.stats.last_seen_at,
                languageLocale,
              )}
            />
          </dl>
          {selected.run?.data_files?.length ? (
            <p className="monitor-data-files">
              {selected.run.data_files.join(' · ')}
            </p>
          ) : null}
          <div className="monitor-panel-actions">
            <button
              className="text-button"
              type="button"
              disabled={actionId === selected.id}
              onClick={() => void runNotifyTest(selected)}
            >
              {t('monitoring.notifyTest')}
            </button>
          </div>
        </section>

        <section className="monitor-panel">
          <div className="monitor-panel-heading">
            <div>
              <h2>{t('monitoring.startup')}</h2>
              <p>{t('monitoring.startupBody')}</p>
            </div>
            <Tag
              tone={
                selected.startup.configured
                  ? selected.startup.registered
                    ? 'blue'
                    : 'rust'
                  : 'quiet'
              }
            >
              {selected.startup.configured
                ? selected.startup.registered
                  ? t('monitoring.startupEnabled')
                  : t('monitoring.startupMissing')
                : t('monitoring.startupNotConfigured')}
            </Tag>
          </div>
          {selected.startup.backend ? (
            <p className="monitor-muted">
              {selected.startup.name
                ? t('monitoring.startupBackend', {
                    backend: selected.startup.backend,
                    name: selected.startup.name,
                  })
                : selected.startup.backend}
            </p>
          ) : (
            <p className="monitor-muted">
              {selected.startup.error ?? t('monitoring.startupUnavailable')}
            </p>
          )}
          {selected.startup.command_preview ? (
            <p className="monitor-data-files">
              {selected.startup.command_preview}
            </p>
          ) : null}
          <div className="monitor-panel-actions">
            {selected.startup.configured ? (
              <button
                className="text-button danger"
                type="button"
                disabled={actionId === selected.id}
                onClick={() => void runStartupAction(selected, 'remove')}
              >
                {t('monitoring.startupRemove')}
              </button>
            ) : (
              <button
                className="monitor-control"
                type="button"
                disabled={actionId === selected.id || !selected.startup.available}
                onClick={() => void runStartupAction(selected, 'install')}
              >
                {t('monitoring.startupInstall')}
              </button>
            )}
            <button
              className="text-button"
              type="button"
              disabled={actionId === selected.id || !selected.startup.configured}
              onClick={() => void runStartupAction(selected, 'verify')}
            >
              {t('monitoring.startupVerify')}
            </button>
          </div>
          <p className="monitor-muted">{t('monitoring.startupHint')}</p>
        </section>
        </div>

        <section className="monitor-panel">
          <div className="monitor-panel-heading">
            <div>
              <h2>{t('monitoring.alerts')}</h2>
              <p>{t('monitoring.alertsBody')}</p>
            </div>
          </div>
          {series?.alerts?.length ? (
            <ul className="monitor-alert-list">
              {series.alerts.slice().reverse().slice(0, 20).map((alert) => (
                <li key={`${alert.at}-${alert.message}`}>
                  <Tag tone="rust">{alert.type}</Tag>
                  <span>{alert.message}</span>
                  <span className="monitor-alert-date">
                    {formatDate(alert.at, languageLocale)}
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="monitor-muted">{t('monitoring.noAlerts')}</p>
          )}
        </section>

        {editor ? (
          <MonitorEditorDialog
            state={editor}
            library={library}
            startupAvailable={overview?.startup_available ?? false}
            onClose={() => setEditor(null)}
            onSaved={(binding) => {
              setEditor(null)
              setSelectedId(binding.id)
              void refresh(true)
            }}
          />
        ) : null}
        {deleteTarget ? (
          <ConfirmDialog
            title={t('monitoring.deleteTitle')}
            body={t('monitoring.deleteBody', {
              name: deleteTarget.target.title ?? deleteTarget.name ?? deleteTarget.id,
            })}
            confirmLabel={t('monitoring.delete')}
            onConfirm={() => void confirmDelete()}
            onClose={() => setDeleteTarget(null)}
          />
        ) : null}
        {stopTarget ? (
          <ConfirmDialog
            title={t('monitoring.stopStartupTitle')}
            body={t('monitoring.stopStartupBody')}
            confirmLabel={t('monitoring.stopStartupConfirm')}
            onConfirm={() => {
              const target = stopTarget
              setStopTarget(null)
              void runAction(target, 'stop')
            }}
            onClose={() => setStopTarget(null)}
          />
        ) : null}
      </section>
    )
  }

  return (
    <section className="monitoring-view">
      <header className="saved-header monitoring-header">
        <div className="saved-heading">
          <h1>{t('monitoring.title')}</h1>
          <p className="saved-summary">
            {overview
              ? [
                  t('monitoring.monitorCount', {
                    count: formatNumber(overview.bindings),
                  }),
                  t('monitoring.runningCount', {
                    count: formatNumber(overview.running),
                  }),
                  t('monitoring.alertCount', {
                    count: formatNumber(overview.alerts),
                  }),
                ].join(' · ')
              : t('monitoring.loading')}
          </p>
        </div>
        <div className="saved-header-actions">
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
          <button
            className="primary-button"
            type="button"
            onClick={() => openEditor()}
          >
            <Icon name="plus" />
            {t('monitoring.newMonitor')}
          </button>
        </div>
      </header>

      {error ? <MonitorError message={error} /> : null}

      {loading && !overview ? (
        <div className="monitor-loading">
          <span className="skeleton monitor-loading-line" />
          <span className="skeleton monitor-loading-line" />
          <span className="skeleton monitor-loading-line short" />
        </div>
      ) : overview && overview.monitors.length === 0 && overview.other_runs.length === 0 ? (
        <div className="empty-state saved-empty">
          <Icon name="activity" />
          <h3>{t('monitoring.emptyTitle')}</h3>
          <p>{t('monitoring.emptyBody')}</p>
        </div>
      ) : (
        <>
          <div className={`monitor-list monitor-list--${layout}`}>
            {overview?.monitors.map((binding) => (
              <MonitorCard
                key={binding.id}
                binding={binding}
                languageLocale={languageLocale}
                actionId={actionId}
                onOpen={() => setSelectedId(binding.id)}
                onStart={() => void runAction(binding, 'start')}
                onStop={() => requestStop(binding)}
                onEdit={() => openEditor(binding)}
                onDelete={() => setDeleteTarget(binding)}
              />
            ))}
          </div>
          {overview?.other_runs.length ? (
            <section className="monitor-external-section">
              <div className="monitor-panel-heading">
                <div>
                  <h2>{t('monitoring.otherRuns')}</h2>
                  <p>{t('monitoring.otherRunsBody')}</p>
                </div>
              </div>
              <div className="monitor-list">
                {overview.other_runs.map((run) => (
                  <ExternalRunCard key={`${run.run.pid}-${run.run.binding_id}`} item={run} />
                ))}
              </div>
            </section>
          ) : null}
        </>
      )}

      {editor ? (
        <MonitorEditorDialog
          state={editor}
          library={library}
          startupAvailable={overview?.startup_available ?? false}
          onClose={() => setEditor(null)}
          onSaved={(binding) => {
            setEditor(null)
            setSelectedId(binding.id)
            void refresh(true)
          }}
        />
      ) : null}
      {deleteTarget ? (
        <ConfirmDialog
          title={t('monitoring.deleteTitle')}
          body={t('monitoring.deleteBody', {
            name: deleteTarget.target.title ?? deleteTarget.name ?? deleteTarget.id,
          })}
          confirmLabel={t('monitoring.delete')}
          onConfirm={() => void confirmDelete()}
          onClose={() => setDeleteTarget(null)}
        />
      ) : null}
      {stopTarget ? (
        <ConfirmDialog
          title={t('monitoring.stopStartupTitle')}
          body={t('monitoring.stopStartupBody')}
          confirmLabel={t('monitoring.stopStartupConfirm')}
          onConfirm={() => {
            const target = stopTarget
            setStopTarget(null)
            void runAction(target, 'stop')
          }}
          onClose={() => setStopTarget(null)}
        />
      ) : null}
    </section>
  )
}

function MonitorCard({
  binding,
  languageLocale,
  actionId,
  onOpen,
  onStart,
  onStop,
  onEdit,
  onDelete,
}: {
  binding: MonitorBinding
  languageLocale: string
  actionId: string | null
  onOpen: () => void
  onStart: () => void
  onStop: () => void
  onEdit: () => void
  onDelete: () => void
}) {
  const { t, formatNumber } = useI18n()
  const busy = actionId === binding.id
  const lastSeen = binding.run?.last_seen_at ?? binding.stats.last_seen_at
  const title = binding.target.title ?? binding.name ?? binding.id
  const hasPrice =
    binding.kind === 'group'
      ? binding.stats.last_total != null
      : binding.stats.last_price != null
  return (
    <article className={`monitor-card monitor-card--${binding.status}`}>
      <button className="monitor-card-main" type="button" onClick={onOpen}>
        <span className="monitor-card-image">
          {binding.target.image ? (
            <ProductImage src={binding.target.image} alt="" />
          ) : (
            <span className="monitor-card-icon">
              <Icon name={binding.kind === 'group' ? 'folder' : 'bookmark'} />
            </span>
          )}
        </span>
        <span className="monitor-card-body">
          <span className="monitor-card-kicker">
            {binding.target.marketplace ? (
              <span className="monitor-card-marketplace">
                <Icon name="globe" />
                {binding.target.marketplace}
              </span>
            ) : null}
            <span className="monitor-card-asin">
              {binding.kind === 'group'
                ? t('monitoring.groupTab')
                : binding.target.asin
                  ? `${t('monitoring.asin')} ${binding.target.asin}`
                  : binding.id}
            </span>
          </span>
          <span className="monitor-card-name" title={title}>
            {title}
          </span>
          <span className="monitor-card-facts">
            <span className="monitor-fact">
              <span className="monitor-fact-label">{t('monitoring.timer')}</span>
              <span className="monitor-fact-value">
                {formatTimer(binding.runtime.timer_seconds)}
              </span>
            </span>
            <span className="monitor-fact">
              <span className="monitor-fact-label">
                {t('monitoring.lastCheck')}
              </span>
              <span className="monitor-fact-value">
                {lastSeen
                  ? formatDate(lastSeen, languageLocale)
                  : t('monitoring.never')}
              </span>
            </span>
          </span>
          {binding.error ? (
            <span className="monitor-card-error">
              <Icon name="alert" />
              {binding.error}
            </span>
          ) : null}
        </span>
        <span className="monitor-card-signal">
          <MonitorStatus status={binding.status} />
          <span className="monitor-card-price-block">
            {hasPrice ? (
              <>
                <span className="monitor-card-price-label">
                  {t('monitoring.current')}
                </span>
                <span className="monitor-card-price">
                  {formatMonitorValue(binding, languageLocale)}
                </span>
              </>
            ) : (
              <span className="monitor-card-price-note">
                {t('monitoring.noPrice')}
              </span>
            )}
          </span>
          {binding.stats.observations > 0 ? (
            <span className="monitor-card-observations">
              {t('monitoring.observationsShort', {
                count: formatNumber(binding.stats.observations),
              })}
            </span>
          ) : null}
        </span>
      </button>
      <div className="monitor-card-actions">
        {binding.target.url ? (
          <a
            className="monitor-card-link"
            href={binding.target.url}
            target="_blank"
            rel="noreferrer"
          >
            <Icon name="external" />
            {t('monitoring.openProduct')}
          </a>
        ) : binding.kind === 'group' ? (
          <span className="monitor-card-footnote">
            {t('monitoring.groupTarget', {
              count: formatNumber(binding.target.products),
            })}
          </span>
        ) : null}
        <div className="monitor-card-action-group">
          {binding.can_stop ? (
            <button
              className="monitor-action"
              type="button"
              disabled={busy}
              onClick={onStop}
            >
              {t('monitoring.stop')}
            </button>
          ) : (
            <button
              className="monitor-action monitor-action--start"
              type="button"
              disabled={!binding.can_start || busy}
              onClick={onStart}
            >
              {t('monitoring.start')}
            </button>
          )}
          <button
            className="text-button monitor-card-text-action"
            type="button"
            onClick={onEdit}
          >
            {t('monitoring.edit')}
          </button>
          <button
            className="text-button monitor-card-text-action danger"
            type="button"
            onClick={onDelete}
          >
            {t('monitoring.delete')}
          </button>
        </div>
      </div>
    </article>
  )
}

function ExternalRunCard({ item }: { item: MonitorExternalRun }) {
  const { t, formatNumber, languageLocale } = useI18n()
  return (
    <article
      className={`monitor-card monitor-card--static monitor-card--${item.run.status}`}
    >
      <div className="monitor-card-main monitor-card-main--static">
        <span className="monitor-card-image">
          <span className="monitor-card-icon">
            <Icon name="activity" />
          </span>
        </span>
        <span className="monitor-card-body">
          <span className="monitor-card-kicker">
            <span className="monitor-card-marketplace">
              <Icon name="activity" />
              {t('monitoring.cliRun')}
            </span>
            <span className="monitor-card-asin">
              {t('monitoring.targetsCount', {
                count: formatNumber(item.run.targets),
              })}
            </span>
          </span>
          <span
            className="monitor-card-name"
            title={item.title ?? item.run.binding_id ?? `PID ${item.run.pid}`}
          >
            {item.title ?? item.run.binding_id ?? `PID ${item.run.pid}`}
          </span>
          <span className="monitor-card-facts">
            <span className="monitor-fact">
              <span className="monitor-fact-label">{t('monitoring.timer')}</span>
              <span className="monitor-fact-value">
                {formatTimer(item.run.timer_seconds)}
              </span>
            </span>
            <span className="monitor-fact">
              <span className="monitor-fact-label">
                {t('monitoring.lastCheck')}
              </span>
              <span className="monitor-fact-value">
                {item.run.last_seen_at
                  ? formatDate(item.run.last_seen_at, languageLocale)
                  : t('monitoring.never')}
              </span>
            </span>
          </span>
        </span>
        <span className="monitor-card-signal">
          <MonitorStatus status={item.run.status} />
        </span>
      </div>
    </article>
  )
}

function MonitorDetailMenu({
  onDelete,
  disabled,
}: {
  onDelete: () => void
  disabled?: boolean
}) {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
        triggerRef.current?.focus()
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <div className="monitor-detail-menu" ref={rootRef}>
      <button
        ref={triggerRef}
        className="monitor-menu-trigger"
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={t('monitoring.moreActions')}
        title={t('monitoring.moreActions')}
        disabled={disabled}
        onClick={() => setOpen((value) => !value)}
      >
        <Icon name="more" />
      </button>
      {open ? (
        <div className="monitor-menu-list" role="menu">
          <button
            className="danger"
            type="button"
            role="menuitem"
            autoFocus
            onClick={() => {
              setOpen(false)
              onDelete()
            }}
          >
            {t('monitoring.deleteMonitor')}
          </button>
        </div>
      ) : null}
    </div>
  )
}

type ItemRuleDraft = {
  enabled: boolean
  maxPrice: string
  dropPercent: string
  risePercent: string
  baseline: string
  baselinePrice: string
  cooldown: string
}

function itemRuleToDraft(rule?: MonitorItemRule | null): ItemRuleDraft {
  return {
    enabled: Boolean(rule),
    maxPrice: rule?.max_price?.toString() ?? '',
    dropPercent: rule?.drop_percent?.toString() ?? '',
    risePercent: rule?.rise_percent?.toString() ?? '',
    baseline: rule?.baseline ?? '',
    baselinePrice: rule?.baseline_price?.toString() ?? '',
    cooldown: rule?.cooldown_seconds?.toString() ?? '',
  }
}

type TargetOption = {
  id: string
  label: string
}

type TargetPickerPosition = {
  left: number
  top?: number
  bottom?: number
  width: number
  maxHeight: number
}

function TargetPicker({
  value,
  options,
  disabled,
  placeholder,
  ariaLabel,
  onChange,
}: {
  value: string
  options: TargetOption[]
  disabled?: boolean
  placeholder: string
  ariaLabel: string
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const [position, setPosition] = useState<TargetPickerPosition | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([])
  const selectedIndex = Math.max(
    0,
    options.findIndex((option) => option.id === value),
  )
  const selected = options.find((option) => option.id === value)

  const placeMenu = () => {
    const button = buttonRef.current
    if (!button) {
      return
    }
    const rect = button.getBoundingClientRect()
    const spaceBelow = window.innerHeight - rect.bottom
    const spaceAbove = rect.top
    const openAbove = spaceBelow < 220 && spaceAbove > spaceBelow
    setPosition({
      left: rect.left,
      top: openAbove ? undefined : rect.bottom + 6,
      bottom: openAbove ? window.innerHeight - rect.top + 6 : undefined,
      width: rect.width,
      maxHeight: Math.min(
        320,
        Math.max(140, (openAbove ? spaceAbove : spaceBelow) - 12),
      ),
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
    }
  }

  const choose = (id: string, restoreFocus = false) => {
    onChange(id)
    closeMenu(restoreFocus)
  }

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
    const onResize = () => closeMenu()

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('resize', onResize)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
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
          return options.length - 1
        }
        if (next >= options.length) {
          return 0
        }
        return next
      })
      return
    }
    if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault()
      setActiveIndex(event.key === 'Home' ? 0 : options.length - 1)
      return
    }
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      const option = options[activeIndex]
      if (option) {
        choose(option.id, true)
      }
    }
  }

  return (
    <div className="monitor-select" ref={rootRef}>
      <button
        ref={buttonRef}
        className="monitor-select-button"
        type="button"
        disabled={disabled || options.length === 0}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={ariaLabel}
        onClick={(event) =>
          open ? closeMenu(event.detail === 0) : openMenu()
        }
        onKeyDown={(event) => {
          if (!open && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
            event.preventDefault()
            openMenu()
          }
        }}
      >
        <span className="monitor-select-value">
          {selected?.label ?? placeholder}
        </span>
        <Icon name="chevron-down" className="monitor-select-chevron" />
      </button>

      {open && position
        ? createPortal(
            <div
              ref={menuRef}
              className="monitor-select-menu"
              style={{
                left: position.left,
                top: position.top,
                bottom: position.bottom,
                width: position.width,
                maxHeight: position.maxHeight,
              }}
              role="listbox"
              aria-label={ariaLabel}
              onKeyDown={onMenuKeyDown}
            >
              {options.map((option, index) => {
                const isSelected = option.id === value
                return (
                  <button
                    key={option.id}
                    ref={(element) => {
                      optionRefs.current[index] = element
                    }}
                    className={`monitor-select-option${isSelected ? ' is-selected' : ''}${index === activeIndex ? ' is-active' : ''}`}
                    type="button"
                    role="option"
                    aria-selected={isSelected}
                    tabIndex={-1}
                    onClick={() => choose(option.id)}
                    onMouseEnter={() => setActiveIndex(index)}
                  >
                    <span>{option.label}</span>
                    {isSelected ? <Icon name="check" /> : null}
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

function MonitorEditorDialog({
  state,
  library,
  startupAvailable,
  onClose,
  onSaved,
}: {
  state: EditorState
  library: Library
  startupAvailable: boolean
  onClose: () => void
  onSaved: (binding: MonitorBinding) => void
}) {
  const { t, languageLocale } = useI18n()
  const binding = state?.mode === 'edit' ? state.binding : null
  const productsInGroups = useMemo(() => {
    const result = new Set<string>()
    for (const group of library.groups) {
      for (const item of group.items) {
        result.add(item.product_id)
      }
    }
    return result
  }, [library.groups])
  const selectableProducts = library.products.filter(
    (product) => !productsInGroups.has(product.id),
  )
  const [kind, setKind] = useState<'product' | 'group'>(
    binding?.kind ?? (library.groups.length > 0 ? 'group' : 'product'),
  )
  const [targetId, setTargetId] = useState(
    binding?.product_id ?? binding?.group_id ?? '',
  )
  const [name, setName] = useState(binding?.name ?? '')
  const [maxPrice, setMaxPrice] = useState(binding?.max_price?.toString() ?? '')
  const [dropPercent, setDropPercent] = useState(
    binding?.drop_percent?.toString() ?? '',
  )
  const [risePercent, setRisePercent] = useState(
    binding?.rise_percent?.toString() ?? '',
  )
  const [baseline, setBaseline] = useState(binding?.baseline ?? 'original')
  const [baselinePrice, setBaselinePrice] = useState(
    binding?.baseline_price?.toString() ?? '',
  )
  const [cooldown, setCooldown] = useState(
    binding?.cooldown_seconds?.toString() ?? '21600',
  )
  const [totalMax, setTotalMax] = useState(binding?.total_max?.toString() ?? '')
  const [currency, setCurrency] = useState(binding?.currency ?? '')
  const [allowIncomplete, setAllowIncomplete] = useState(
    binding?.allow_incomplete ?? false,
  )
  const [fx, setFx] = useState<Record<string, string>>(
    Object.fromEntries(
      Object.entries(binding?.fx ?? {}).map(([code, rate]) => [
        code,
        String(rate),
      ]),
    ),
  )
  const [timerSeconds, setTimerSeconds] = useState(
    String(binding?.runtime.timer_seconds ?? 3600),
  )
  const [requestDelay, setRequestDelay] = useState(
    String(binding?.runtime.request_delay_seconds ?? 10),
  )
  const [jitter, setJitter] = useState(
    String(binding?.runtime.jitter_seconds ?? 0),
  )
  const [savePath, setSavePath] = useState(binding?.runtime.save_path ?? '')
  const [saveFile, setSaveFile] = useState(
    binding?.runtime.save_file ?? 'amz-monitor',
  )
  const [saveFormat, setSaveFormat] = useState(
    binding?.runtime.save_format ?? 'json',
  )
  const [layout, setLayout] = useState(binding?.runtime.layout ?? 'auto')
  const [notify, setNotify] = useState(binding?.runtime.notify ?? false)
  const [notifyOn, setNotifyOn] = useState<string[]>(
    binding?.runtime.notify_on?.length
      ? binding.runtime.notify_on
      : ['alerts'],
  )
  const [notifyCooldown, setNotifyCooldown] = useState(
    String(binding?.runtime.notify_cooldown_seconds ?? 3600),
  )
  const [noAutoUpdate, setNoAutoUpdate] = useState(
    binding?.runtime.no_auto_update ?? false,
  )
  const [startupEnabled, setStartupEnabled] = useState(
    binding?.startup.configured ?? false,
  )
  const [itemDrafts, setItemDrafts] = useState<Record<string, ItemRuleDraft>>({})
  const [startNow, setStartNow] = useState(!binding)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setItemDrafts({})
  }, [targetId])

  const options = kind === 'group'
    ? library.groups.map((group) => ({
        id: group.id,
        label: `${group.name} (${group.items.length})`,
      }))
    : selectableProducts.map((product) => ({
        id: product.id,
        label: `${product.snapshot.title} · ${product.asin}`,
      }))
  const selectedGroup = kind === 'group'
    ? library.groups.find((group) => group.id === targetId)
    : undefined
  const selectedProduct = kind === 'product'
    ? library.products.find((product) => product.id === targetId)
    : undefined
  const groupCurrencies = selectedGroup
    ? [
        ...new Set(
          selectedGroup.items
            .map((item) => library.products.find((product) => product.id === item.product_id))
            .map((product) => product?.currency)
            .filter((value): value is string => Boolean(value)),
        ),
      ].sort()
    : []
  const missingFxCurrencies = currency.trim()
    ? groupCurrencies.filter(
        (code) =>
          code.toUpperCase() !== currency.trim().toUpperCase() &&
          !fx[code],
      )
    : []

  const draftFor = (productId: string): ItemRuleDraft =>
    itemDrafts[productId]
    ?? itemRuleToDraft(binding?.item_rules?.[productId])

  const updateDraft = (productId: string, patch: Partial<ItemRuleDraft>) => {
    setItemDrafts((current) => ({
      ...current,
      [productId]: {
        ...draftFor(productId),
        ...patch,
      },
    }))
  }

  const referencePrice = kind === 'group'
    ? binding?.stats.last_total ?? binding?.target.price_last ?? null
    : binding?.stats.first_price
      ?? binding?.stats.last_price
      ?? selectedProduct?.price_last
      ?? selectedProduct?.snapshot.price_amount
      ?? null
  const reference = baseline === 'fixed'
    ? parseNumber(baselinePrice)
    : baseline === 'first_seen'
      ? binding?.stats.first_price ?? referencePrice
      : baseline === 'max_seen'
        ? binding?.stats.max_price ?? referencePrice
        : baseline === 'last_seen'
          ? binding?.stats.last_price ?? referencePrice
          : null
  const previewCurrency = kind === 'group'
    ? binding?.currency ?? selectedGroup?.items
      .map((item) => library.products.find((product) => product.id === item.product_id)?.currency)
      .find(Boolean)
    : selectedProduct?.currency ?? binding?.target.currency
  const previewDrop = reference != null && parseNumber(dropPercent) != null
    ? reference * (1 - (parseNumber(dropPercent) ?? 0) / 100)
    : null
  const previewRise = reference != null && parseNumber(risePercent) != null
    ? reference * (1 + (parseNumber(risePercent) ?? 0) / 100)
    : null

  const toggleNotifyEvent = (event: string) => {
    setNotifyOn((current) =>
      current.includes(event)
        ? current.filter((value) => value !== event)
        : [...current, event],
    )
  }

  const submit = async () => {
    if (!targetId) {
      setError(t('monitoring.selectTargetError'))
      return
    }
    setSaving(true)
    setError(null)
    try {
      if (missingFxCurrencies.length > 0) {
        setError(t('monitoring.fxRequired'))
        return
      }
      const runtime: MonitorRuntimeInput = {
        ...binding?.runtime,
        timer_seconds: Number(timerSeconds) || 3600,
        request_delay_seconds: Number(requestDelay) || 10,
        jitter_seconds: Number(jitter) || 0,
        save_path: savePath.trim() || null,
        save_file: saveFile.trim() || 'amz-monitor',
        save_format: saveFormat,
        layout,
        notify,
        notify_on: notify ? notifyOn : [],
        notify_cooldown_seconds: notify ? Number(notifyCooldown) || 3600 : null,
        no_auto_update: noAutoUpdate,
      }
      const fxInput = Object.fromEntries(
        Object.entries(fx)
          .map(([code, value]) => [code, Number(value.replace(',', '.'))])
          .filter(([, value]) => Number.isFinite(value) && Number(value) > 0),
      )
      const itemRules: Record<string, MonitorItemRule> = {}
      if (kind === 'group' && selectedGroup) {
        for (const item of selectedGroup.items) {
          const draft = draftFor(item.product_id)
          if (!draft.enabled) {
            continue
          }
          const rule: MonitorItemRule = {
            max_price: parseNumber(draft.maxPrice),
            drop_percent: parseNumber(draft.dropPercent),
            rise_percent: parseNumber(draft.risePercent),
            baseline: draft.baseline || null,
            baseline_price: draft.baseline === 'fixed'
              ? parseNumber(draft.baselinePrice)
              : null,
            cooldown_seconds: parseNumber(draft.cooldown),
          }
          if (Object.values(rule).some((value) => value != null)) {
            itemRules[item.product_id] = rule
          }
        }
      }
      const input: MonitorBindingInput = {
        kind,
        product_id: kind === 'product' ? targetId : null,
        group_id: kind === 'group' ? targetId : null,
        name: name.trim() || null,
        max_price: parseNumber(maxPrice),
        drop_percent: parseNumber(dropPercent),
        rise_percent: parseNumber(risePercent),
        baseline,
        baseline_price: baseline === 'fixed' ? parseNumber(baselinePrice) : null,
        cooldown_seconds: parseNumber(cooldown),
        total_max: kind === 'group' ? parseNumber(totalMax) : null,
        allow_incomplete: kind === 'group' ? allowIncomplete : null,
        currency: currency.trim().toUpperCase() || null,
        fx: fxInput,
        item_rules: itemRules,
        runtime,
        start: !binding && startNow,
      }
      let saved = binding
        ? await updateMonitor(binding.id, input)
        : await createMonitor(input)
      if (startupEnabled && !saved.startup.configured) {
        saved = await installMonitorStartup(saved.id)
      } else if (!startupEnabled && saved.startup.configured) {
        saved = await removeMonitorStartup(saved.id)
      }
      onSaved(saved)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('app.unexpectedError'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={binding ? t('monitoring.editTitle') : t('monitoring.createTitle')}
      subtitle={t('monitoring.editorSubtitle')}
      onClose={onClose}
      footer={
        <>
          <button className="text-button" type="button" onClick={onClose}>
            {t('saved.cancel')}
          </button>
          <button
            className="primary-button"
            type="button"
            disabled={saving}
            onClick={() => void submit()}
          >
            {saving
              ? t('monitoring.saving')
              : binding
                ? t('saved.saveChanges')
                : t('monitoring.create')}
          </button>
        </>
      }
    >
      {error ? <MonitorError message={error} /> : null}
      {!binding ? (
        <div className="monitor-editor-tabs">
          <button
            type="button"
            className={kind === 'product' ? 'is-active' : ''}
            onClick={() => {
              setKind('product')
              setTargetId('')
            }}
          >
            {t('monitoring.productTarget')}
          </button>
          <button
            type="button"
            className={kind === 'group' ? 'is-active' : ''}
            onClick={() => {
              setKind('group')
              setTargetId('')
            }}
          >
            {t('monitoring.groupTab')}
          </button>
        </div>
      ) : null}

      <section className="monitor-editor-section">
        <h3>{t('monitoring.targetSection')}</h3>
        <div className="modal-field">
          <span>{t('monitoring.target')}</span>
          <TargetPicker
            value={targetId}
            options={options}
            disabled={Boolean(binding)}
            placeholder={t('monitoring.selectTarget')}
            ariaLabel={t('monitoring.target')}
            onChange={setTargetId}
          />
        </div>
        <label className="modal-field">
          <span>{t('monitoring.name')}</span>
          <input
            value={name}
            placeholder={t('monitoring.namePlaceholder')}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
      </section>

      <section className="monitor-editor-section">
        <h3>{t('monitoring.rules')}</h3>
        <p className="monitor-muted">{t('monitoring.rulesHint')}</p>
        <div className="monitor-editor-grid">
          <label className="modal-field">
            <span>{t('monitoring.maxPrice')}</span>
            <input
              type="number"
              min="0"
              step="0.01"
              value={maxPrice}
              onChange={(event) => setMaxPrice(event.target.value)}
            />
          </label>
          <label className="modal-field">
            <span>{t('monitoring.dropPercent')}</span>
            <input
              type="number"
              min="0"
              max="100"
              step="1"
              value={dropPercent}
              onChange={(event) => setDropPercent(event.target.value)}
            />
          </label>
          <label className="modal-field">
            <span>{t('monitoring.risePercent')}</span>
            <input
              type="number"
              min="0"
              max="100"
              step="1"
              value={risePercent}
              onChange={(event) => setRisePercent(event.target.value)}
            />
          </label>
          <label className="modal-field">
            <span>{t('monitoring.baseline')}</span>
            <select
              value={baseline}
              onChange={(event) => setBaseline(event.target.value)}
            >
              <option value="original">{t('monitoring.baselineOriginal')}</option>
              <option value="first_seen">{t('monitoring.baselineFirstSeen')}</option>
              <option value="max_seen">{t('monitoring.baselineMaxSeen')}</option>
              <option value="last_seen">{t('monitoring.baselineLastSeen')}</option>
              <option value="fixed">{t('monitoring.baselineFixed')}</option>
            </select>
          </label>
          {baseline === 'fixed' ? (
            <label className="modal-field">
              <span>{t('monitoring.baselinePrice')}</span>
              <input
                type="number"
                min="0"
                step="0.01"
                value={baselinePrice}
                onChange={(event) => setBaselinePrice(event.target.value)}
              />
            </label>
          ) : null}
          <label className="modal-field">
            <span>{t('monitoring.cooldown')}</span>
            <input
              type="number"
              min="0"
              step="60"
              value={cooldown}
              onChange={(event) => setCooldown(event.target.value)}
            />
          </label>
        </div>
        {previewDrop != null || previewRise != null ? (
          <div className="monitor-threshold-preview">
            <strong>{t('monitoring.thresholdPreview')}</strong>
            {previewDrop != null ? (
              <span>
                {t('monitoring.thresholdDrop')}{' '}
                {formatPrice(previewDrop, previewCurrency ?? undefined, '', languageLocale)}
              </span>
            ) : null}
            {previewRise != null ? (
              <span>
                {t('monitoring.thresholdRise')}{' '}
                {formatPrice(previewRise, previewCurrency ?? undefined, '', languageLocale)}
              </span>
            ) : null}
          </div>
        ) : null}

        {kind === 'group' ? (
          <>
            <div className="monitor-editor-grid">
              <label className="modal-field">
                <span>{t('monitoring.totalMax')}</span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={totalMax}
                  onChange={(event) => setTotalMax(event.target.value)}
                />
              </label>
              <label className="modal-field">
                <span>{t('monitoring.currency')}</span>
                <input
                  value={currency}
                  maxLength={3}
                  placeholder={t('monitoring.currencyPlaceholder')}
                  onChange={(event) => setCurrency(event.target.value)}
                />
              </label>
              {currency.trim()
                ? groupCurrencies
                    .filter(
                      (code) =>
                        code.toUpperCase() !== currency.trim().toUpperCase(),
                    )
                    .map((code) => (
                      <label className="modal-field" key={code}>
                        <span>{t('monitoring.fxRate', { currency: code })}</span>
                        <input
                          type="number"
                          min="0"
                          step="0.0001"
                          placeholder="1.00"
                          value={fx[code] ?? ''}
                          onChange={(event) =>
                            setFx((current) => ({
                              ...current,
                              [code]: event.target.value,
                            }))
                          }
                        />
                      </label>
                    ))
                : null}
            </div>
            <label className="monitor-checkbox">
              <input
                type="checkbox"
                checked={allowIncomplete}
                onChange={(event) => setAllowIncomplete(event.target.checked)}
              />
              {t('monitoring.allowIncomplete')}
            </label>
          </>
        ) : null}

        {kind === 'group' && selectedGroup ? (
          <div className="monitor-item-rules">
            <h4>{t('monitoring.itemRules')}</h4>
            <p className="monitor-muted">{t('monitoring.itemRulesHint')}</p>
            {selectedGroup.items.map((item) => {
              const product = library.products.find(
                (candidate) => candidate.id === item.product_id,
              )
              const draft = draftFor(item.product_id)
              return (
                <div className="monitor-item-rule" key={item.product_id}>
                  <div className="monitor-item-rule-head">
                    <label className="monitor-checkbox">
                      <input
                        type="checkbox"
                        checked={draft.enabled}
                        onChange={(event) =>
                          updateDraft(item.product_id, {
                            enabled: event.target.checked,
                          })
                        }
                      />
                      {product?.snapshot.title ?? item.product_id}
                    </label>
                    <span>
                      {formatPrice(
                        product?.price_last ?? product?.snapshot.price_amount,
                        product?.currency,
                        '—',
                        languageLocale,
                      )}
                      {' · '}
                      {t('monitoring.quantity')} {item.quantity}
                    </span>
                  </div>
                  {draft.enabled ? (
                    <div className="monitor-editor-grid">
                      <label className="modal-field">
                        <span>{t('monitoring.maxPrice')}</span>
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={draft.maxPrice}
                          onChange={(event) =>
                            updateDraft(item.product_id, {
                              maxPrice: event.target.value,
                            })
                          }
                        />
                      </label>
                      <label className="modal-field">
                        <span>{t('monitoring.dropPercent')}</span>
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="1"
                          value={draft.dropPercent}
                          onChange={(event) =>
                            updateDraft(item.product_id, {
                              dropPercent: event.target.value,
                            })
                          }
                        />
                      </label>
                      <label className="modal-field">
                        <span>{t('monitoring.risePercent')}</span>
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="1"
                          value={draft.risePercent}
                          onChange={(event) =>
                            updateDraft(item.product_id, {
                              risePercent: event.target.value,
                            })
                          }
                        />
                      </label>
                      <label className="modal-field">
                        <span>{t('monitoring.baseline')}</span>
                        <select
                          value={draft.baseline}
                          onChange={(event) =>
                            updateDraft(item.product_id, {
                              baseline: event.target.value,
                            })
                          }
                        >
                          <option value="">{t('monitoring.inherit')}</option>
                          <option value="original">{t('monitoring.baselineOriginal')}</option>
                          <option value="first_seen">{t('monitoring.baselineFirstSeen')}</option>
                          <option value="max_seen">{t('monitoring.baselineMaxSeen')}</option>
                          <option value="last_seen">{t('monitoring.baselineLastSeen')}</option>
                          <option value="fixed">{t('monitoring.baselineFixed')}</option>
                        </select>
                      </label>
                      {draft.baseline === 'fixed' ? (
                        <label className="modal-field">
                          <span>{t('monitoring.baselinePrice')}</span>
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={draft.baselinePrice}
                            onChange={(event) =>
                              updateDraft(item.product_id, {
                                baselinePrice: event.target.value,
                              })
                            }
                          />
                        </label>
                      ) : null}
                      <label className="modal-field">
                        <span>{t('monitoring.cooldown')}</span>
                        <input
                          type="number"
                          min="0"
                          step="60"
                          value={draft.cooldown}
                          onChange={(event) =>
                            updateDraft(item.product_id, {
                              cooldown: event.target.value,
                            })
                          }
                        />
                      </label>
                    </div>
                  ) : null}
                </div>
              )
            })}
          </div>
        ) : null}
      </section>

      <section className="monitor-editor-section">
        <h3>{t('monitoring.runtime')}</h3>
        <div className="monitor-editor-grid">
          <label className="modal-field">
            <span>{t('monitoring.timer')}</span>
            <input
              type="number"
              min="10"
              step="60"
              value={timerSeconds}
              onChange={(event) => setTimerSeconds(event.target.value)}
            />
          </label>
          <label className="modal-field">
            <span>{t('monitoring.requestDelay')}</span>
            <input
              type="number"
              min="6"
              step="1"
              value={requestDelay}
              onChange={(event) => setRequestDelay(event.target.value)}
            />
          </label>
        </div>
        <label className="monitor-checkbox">
          <input
            type="checkbox"
            checked={notify}
            onChange={(event) => setNotify(event.target.checked)}
          />
          {t('monitoring.notify')}
        </label>

        <details className="monitor-advanced">
          <summary>{t('monitoring.advanced')}</summary>
          <p className="monitor-muted">{t('monitoring.advancedHint')}</p>
          <div className="monitor-editor-grid">
            <label className="modal-field">
              <span>{t('monitoring.jitter')}</span>
              <input
                type="number"
                min="0"
                max="3600"
                step="1"
                value={jitter}
                onChange={(event) => setJitter(event.target.value)}
              />
            </label>
            <label className="modal-field">
              <span>{t('monitoring.savePath')}</span>
              <input
                value={savePath}
                placeholder={t('monitoring.defaultValue')}
                onChange={(event) => setSavePath(event.target.value)}
              />
            </label>
            <label className="modal-field">
              <span>{t('monitoring.saveFile')}</span>
              <input
                value={saveFile}
                onChange={(event) => setSaveFile(event.target.value)}
              />
            </label>
            <label className="modal-field">
              <span>{t('monitoring.saveFormat')}</span>
              <select
                value={saveFormat}
                onChange={(event) => setSaveFormat(event.target.value)}
              >
                <option value="json">json/jsonl</option>
                <option value="csv">csv</option>
              </select>
            </label>
            <label className="modal-field">
              <span>{t('monitoring.layout')}</span>
              <select
                value={layout}
                onChange={(event) => setLayout(event.target.value)}
              >
                <option value="auto">auto</option>
                <option value="combined">combined</option>
                <option value="per-group">per-group</option>
                <option value="single">single</option>
              </select>
            </label>
            <label className="modal-field">
              <span>{t('monitoring.notifyCooldown')}</span>
              <input
                type="number"
                min="60"
                max="604800"
                step="60"
                value={notifyCooldown}
                onChange={(event) => setNotifyCooldown(event.target.value)}
              />
            </label>
          </div>
          <div className="monitor-notify-events">
            <span>{t('monitoring.notifyOn')}</span>
            {(['alerts', 'errors', 'blocks', 'fatal'] as const).map((event) => (
              <label className="monitor-checkbox" key={event}>
                <input
                  type="checkbox"
                  checked={notifyOn.includes(event)}
                  onChange={() => toggleNotifyEvent(event)}
                />
                {t(`monitoring.notifyEvent.${event}`)}
              </label>
            ))}
          </div>
          <label className="monitor-checkbox">
            <input
              type="checkbox"
              checked={noAutoUpdate}
              onChange={(event) => setNoAutoUpdate(event.target.checked)}
            />
            {t('monitoring.noAutoUpdate')}
          </label>
        </details>
      </section>

      <section className="monitor-editor-section">
        <h3>{t('monitoring.startup')}</h3>
        <label className="monitor-checkbox">
          <input
            type="checkbox"
            checked={startupEnabled}
            disabled={
              !(binding?.startup.available ?? startupAvailable) &&
              !startupEnabled
            }
            onChange={(event) => setStartupEnabled(event.target.checked)}
          />
          {t('monitoring.startupToggle')}
        </label>
        <p className="monitor-muted">{t('monitoring.startupHint')}</p>
      </section>

      {!binding ? (
        <label className="monitor-checkbox">
          <input
            type="checkbox"
            checked={startNow}
            onChange={(event) => setStartNow(event.target.checked)}
          />
          {t('monitoring.startNow')}
        </label>
      ) : null}
    </Modal>
  )
}

function MonitorStatus({ status }: { status: string }) {
  const { t } = useI18n()
  return (
    <span className={`monitor-status monitor-status--${status}`}>
      <span className="monitor-status-dot" aria-hidden="true" />
      {statusLabel(status, t)}
    </span>
  )
}

function statusLabel(status: string, t: Translate) {
  switch (status) {
    case 'running':
      return t('monitoring.statusRunning')
    case 'stale':
      return t('monitoring.statusStale')
    case 'configured':
      return t('monitoring.statusConfigured')
    case 'invalid':
      return t('monitoring.statusInvalid')
    default:
      return t('monitoring.statusStopped')
  }
}

function MonitorError({ message }: { message: string }) {
  const { t } = useI18n()
  return (
    <div className="notice notice-warning" role="alert">
      <Icon name="alert" />
      <div>
        <strong>{t('monitoring.actionFailed')}</strong>
        <p>{message}</p>
      </div>
    </div>
  )
}

function Stat({
  label,
  value,
  mono,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div>
      <dt>{label}</dt>
      <dd className={mono ? 'monitor-mono' : undefined}>{value}</dd>
    </div>
  )
}

function formatMonitorValue(binding: MonitorBinding, locale: string): string {
  const value = binding.kind === 'group'
    ? binding.stats.last_total
    : binding.stats.last_price
  const currency = binding.stats.currency ?? binding.currency ?? binding.target.currency
  return formatStat(value, currency, locale)
}

function formatStat(
  value: number | null | undefined,
  currency: string | null | undefined,
  locale: string,
): string {
  if (value == null) {
    return '—'
  }
  return formatPrice(value, currency ?? '', '', locale)
}

function formatTimer(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600}h`
  }
  if (seconds % 60 === 0) {
    return `${seconds / 60}m`
  }
  return `${seconds}s`
}

function formatDate(value: string | null | undefined, locale: string): string {
  if (!value) {
    return '—'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return new Intl.DateTimeFormat(locale, {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

function parseNumber(value: string): number | null {
  const trimmed = value.trim()
  if (!trimmed) {
    return null
  }
  const parsed = Number(trimmed.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}
