import { useMemo, useState } from 'react'
import type { Meta, UpdateStatus } from '../lib/api'
import { useI18n } from '../i18n/I18nProvider'
import { Icon, Tag } from './ui'
import { ConfirmDialog } from './SavedProductsView'
import { ReleaseNotes } from './ReleaseNotes'
import { updateStageLabel } from '../lib/update'

function formatBytes(bytes: number | undefined): string {
  if (!bytes || bytes <= 0) {
    return '—'
  }
  if (bytes >= 1024 * 1024) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }
  if (bytes >= 1024) {
    return `${(bytes / 1024).toFixed(0)} KB`
  }
  return `${bytes} B`
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
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}


export function StatusView({
  meta,
  status,
  loading,
  checking,
  starting,
  error,
  onCheck,
  onApply,
}: {
  meta: Meta
  status: UpdateStatus | null
  loading: boolean
  checking: boolean
  starting: boolean
  error: string | null
  onCheck: () => void
  onApply: () => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const [confirmApply, setConfirmApply] = useState(false)
  const currentVersion = status?.current_version ?? meta.version
  const channel = status?.channel ?? meta.channel ?? 'dev'
  const check = status?.check ?? null
  const job = status?.job ?? null
  const busy =
    checking ||
    starting ||
    job?.phase === 'checking' ||
    job?.phase === 'preparing' ||
    job?.phase === 'scheduled'
  const canApply =
    Boolean(check?.can_apply) &&
    !busy &&
    job?.phase !== 'failed'
  const showRelease =
    check?.status === 'update_available'
    || check?.status === 'manual_required'
  const checkMessage = check
    ? check.status === 'update_available'
      ? t('status.messageAvailable', {
          current: check.current_version,
          latest: check.latest_version ?? '—',
        })
      : check.status === 'up_to_date'
        ? t('status.messageUpToDate', { version: check.current_version })
        : check.status === 'manual_required'
          ? t('status.messageManual', {
              version: check.latest_version ?? '—',
              min: check.min_supported_version ?? '—',
            })
          : t('status.messageNoRelease')
    : ''
  const stageLabel = updateStageLabel(job?.progress?.stage, t)
  const progressDetail = stageLabel
    ? `${stageLabel}${job?.progress?.percent != null ? ` · ${job.progress.percent}%` : ''}`
    : job?.progress?.message ?? job?.message ?? t('status.preparing')
  const updateTag = useMemo(() => {
    if (!check) {
      return null
    }
    if (check.status === 'update_available') {
      return (
        <Tag tone="blue">
          {t('status.updateAvailableTag', {
            version: check.latest_version ?? '—',
          })}
        </Tag>
      )
    }
    if (check.status === 'up_to_date') {
      return <Tag tone="quiet">{t('status.upToDateTag')}</Tag>
    }
    if (check.status === 'manual_required') {
      return <Tag tone="rust">{t('status.manualTag')}</Tag>
    }
    return <Tag tone="quiet">{t('status.noReleaseTag')}</Tag>
  }, [check, t])

  return (
    <section className="status-view">
      <header className="saved-header status-header">
        <div className="saved-heading">
          <h1>{t('status.title')}</h1>
          <p className="saved-summary">
            {[
              `amz-cli ${currentVersion}`,
              channel,
              status?.platform ?? meta.platform,
            ].join(' · ')}
          </p>
        </div>
      </header>

      {error ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('status.loadFailed')}</strong>
            <p>{error}</p>
          </div>
        </div>
      ) : null}

      <div className="status-grid">
        <section className="status-card">
          <div className="status-card-heading">
            <span className="status-card-icon">
              <Icon name="info" />
            </span>
            <div>
              <h2>{t('status.systemTitle')}</h2>
              <p>{t('status.systemBody')}</p>
            </div>
          </div>
          <dl className="status-facts">
            <div>
              <dt>{t('status.version')}</dt>
              <dd>{currentVersion}</dd>
            </div>
            <div>
              <dt>{t('status.channel')}</dt>
              <dd>{channel}</dd>
            </div>
            <div>
              <dt>{t('status.platform')}</dt>
              <dd>{status?.platform ?? meta.platform}</dd>
            </div>
            <div>
              <dt>{t('status.installDir')}</dt>
              <dd className="status-path">
                {status?.install_dir ?? meta.install_dir}
              </dd>
            </div>
            <div>
              <dt>{t('status.webPort')}</dt>
              <dd>{formatNumber(meta.web.port)}</dd>
            </div>
            <div>
              <dt>{t('status.uptime')}</dt>
              <dd>
                {t('status.uptimeValue', {
                  minutes: formatNumber(
                    Math.max(1, Math.round(meta.web.uptime_seconds / 60)),
                  ),
                })}
              </dd>
            </div>
            <div>
              <dt>{t('status.build')}</dt>
              <dd className="status-path">{meta.web.build}</dd>
            </div>
            <div>
              <dt>{t('status.source')}</dt>
              <dd>{meta.source}</dd>
            </div>
          </dl>
          {status?.curl ? (
            <div className="status-dependencies">
              <div className="status-dependency-heading">
                <span>{t('status.dependencies')}</span>
                <Tag
                  tone={
                    status.curl.available && !status.curl.error
                      ? 'blue'
                      : 'rust'
                  }
                >
                  {!status.curl.available
                    ? t('status.curlUnavailable')
                    : status.curl.mode === 'system'
                      ? t('status.curlSystem')
                      : status.curl.mode === 'bundled'
                        ? t('status.curlBundled')
                        : t('status.curlUnavailable')}
                </Tag>
              </div>
              <div className="status-curl-name">
                <strong>curl-impersonate</strong>
                <span>{status.curl.version}</span>
              </div>
              <div className="status-curl-path">
                <span>{t('status.curlPath')}</span>
                <code>{status.curl.path}</code>
              </div>
              {status.curl.files.length > 0 ? (
                <div className="status-curl-files">
                  <span>{t('status.curlFiles')}</span>
                  {status.curl.files.map((file) => (
                    <div key={file.name}>
                      <code>{file.name}</code>
                      <Tag tone={file.exists ? 'quiet' : 'rust'}>
                        {file.exists
                          ? t('status.curlOk')
                          : t('status.curlMissing')}
                      </Tag>
                    </div>
                  ))}
                </div>
              ) : null}
              {status.curl.error ? (
                <p className="status-curl-error">{status.curl.error}</p>
              ) : null}
              {!status.curl.available ? (
                <p className="status-curl-hint">
                  {status.curl.mode === 'system'
                    ? t('status.curlLinuxHint')
                    : t('status.curlWindowsHint')}
                </p>
              ) : null}
            </div>
          ) : null}
        </section>

        <section className="status-card status-card--update">
          <div className="status-card-heading">
            <span className="status-card-icon">
              <Icon name="refresh" />
            </span>
            <div>
              <h2>{t('status.updateTitle')}</h2>
              <p>{t('status.updateBody')}</p>
            </div>
            {updateTag}
          </div>

          {loading ? (
            <div className="status-loading" aria-busy="true">
              <span className="skeleton status-loading-line" />
              <span className="skeleton status-loading-line short" />
            </div>
          ) : (
            <>
              {job?.phase === 'preparing' || job?.phase === 'scheduled' ? (
                <div className="status-update-progress" role="status">
                  <div className="status-progress-head">
                    <strong>
                      {job.phase === 'scheduled'
                        ? t('status.restarting')
                        : t('status.updating')}
                    </strong>
                    <span>
                      {job.progress?.percent != null
                        ? `${job.progress.percent}%`
                        : t('status.preparing')}
                    </span>
                  </div>
                  <div
                    className="status-progress-track"
                    role="progressbar"
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-valuenow={job.progress?.percent ?? undefined}
                  >
                    <span
                      className={
                        job.progress?.percent == null
                          ? 'is-indeterminate'
                          : ''
                      }
                      style={
                        job.progress?.percent != null
                          ? { width: `${job.progress.percent}%` }
                          : undefined
                      }
                    />
                  </div>
                  <p>{progressDetail}</p>
                  {job.phase === 'scheduled' ? (
                    <p className="status-restart-hint">
                      {t('status.restartHint')}
                    </p>
                  ) : null}
                </div>
              ) : null}

              {check ? (
                <div className="status-update-body">
                  {showRelease ? (
                    <div className="status-version-line">
                      <span>
                        <span className="status-version-label">
                          {t('status.currentVersion')}
                        </span>
                        <strong>{check.current_version}</strong>
                      </span>
                      <Icon name="arrow-right" />
                      <span>
                        <span className="status-version-label">
                          {t('status.latestVersion')}
                        </span>
                        <strong>{check.latest_version ?? '—'}</strong>
                      </span>
                    </div>
                  ) : null}
                  <p className="status-message">{checkMessage}</p>
                  {showRelease ? (
                    <>
                      <div className="status-update-meta">
                        {check.published_at ? (
                          <span>
                            {t('status.published', {
                              date: formatDate(
                                check.published_at,
                                languageLocale,
                              ),
                            })}
                          </span>
                        ) : null}
                        {check.download_size > 0 ? (
                          <span>
                            {t('status.downloadSize', {
                              size: formatBytes(check.download_size),
                              files: formatNumber(check.file_count),
                            })}
                          </span>
                        ) : null}
                      </div>
                      {check.notes_markdown || check.notes ? (
                        <details className="status-release-notes" open>
                          <summary>{t('status.releaseNotes')}</summary>
                          <ReleaseNotes
                            markdown={check.notes_markdown}
                            plain={check.notes}
                          />
                        </details>
                      ) : null}
                    </>
                  ) : null}
                </div>
              ) : null}

              {job?.phase === 'failed' ? (
                <div className="notice notice-warning status-inline-error" role="alert">
                  <Icon name="alert" />
                  <div>
                    <strong>{t('status.updateFailed')}</strong>
                    <p>{job.error ?? job.message}</p>
                  </div>
                </div>
              ) : null}

              <div className="status-update-actions">
                <button
                  className="secondary-button"
                  type="button"
                  onClick={onCheck}
                  disabled={busy || loading}
                >
                  <Icon name="refresh" />
                  {checking || job?.phase === 'checking'
                    ? t('status.checking')
                    : t('status.check')}
                </button>
                {canApply ? (
                  <button
                    className="primary-button"
                    type="button"
                    onClick={() => setConfirmApply(true)}
                    disabled={busy}
                  >
                    <Icon name="arrow-right" />
                    {t('status.apply', {
                      version: check?.latest_version ?? '',
                    })}
                  </button>
                ) : null}
              </div>
            </>
          )}

          {status?.auto_update ? (
            <div className="status-auto-update">
              <div>
                <span>{t('status.autoUpdate')}</span>
                <strong>
                  {status.auto_update.enabled
                    ? t('status.enabled')
                    : t('status.disabled')}
                </strong>
              </div>
              <div>
                <span>{t('status.autoChannel')}</span>
                <strong>{status.auto_update.channel}</strong>
              </div>
              {status.auto_state?.last_check ? (
                <div>
                  <span>{t('status.lastCheck')}</span>
                  <strong>
                    {formatDate(status.auto_state.last_check, languageLocale)}
                  </strong>
                </div>
              ) : null}
            </div>
          ) : null}
        </section>
      </div>

      <section className="status-card status-card--history">
        <div className="status-card-heading">
          <span className="status-card-icon">
            <Icon name="clock" />
          </span>
          <div>
            <h2>{t('status.lastUpdateTitle')}</h2>
            <p>{t('status.lastUpdateBody')}</p>
          </div>
        </div>
        {status?.journal ? (
          <div className="status-journal">
            <div>
              <span>{t('status.journalPhase')}</span>
              <strong>{status.journal.phase}</strong>
            </div>
            <div>
              <span>{t('status.journalVersion')}</span>
              <strong>
                {status.journal.from_version ?? '—'} →{' '}
                {status.journal.to_version ?? '—'}
              </strong>
            </div>
            <div>
              <span>{t('status.journalDate')}</span>
              <strong>
                {formatDate(status.journal.updated_at, languageLocale)}
              </strong>
            </div>
            {status.journal.error ? (
              <p className="status-journal-error">{status.journal.error}</p>
            ) : null}
            {status.journal.restart_errors?.length ? (
              <p className="status-journal-error">
                {status.journal.restart_errors.join(' · ')}
              </p>
            ) : null}
            {status.journal.web_restart_error ? (
              <p className="status-journal-error">
                {status.journal.web_restart_error}
              </p>
            ) : null}
          </div>
        ) : (
          <p className="status-muted">{t('status.noJournal')}</p>
        )}

        {status?.backups?.length ? (
          <div className="status-backups">
            <span>{t('status.backups')}</span>
            {status.backups.map((backup) => (
              <div key={`${backup.version}-${backup.created_at}`}>
                <strong>{backup.version}</strong>
                <span>{formatDate(backup.created_at, languageLocale)}</span>
                <span>{formatBytes(backup.size)}</span>
              </div>
            ))}
          </div>
        ) : null}
      </section>

      {confirmApply && check?.can_apply ? (
        <ConfirmDialog
          title={t('status.confirmTitle', {
            version: check.latest_version ?? '',
          })}
          body={t('status.confirmBody')}
          confirmLabel={t('status.applyConfirm', {
            version: check.latest_version ?? '',
          })}
          onConfirm={() => {
            setConfirmApply(false)
            onApply()
          }}
          onClose={() => setConfirmApply(false)}
        />
      ) : null}
    </section>
  )
}
