import type { Translate } from '../i18n/types'

export type RecentSearch = {
  query: string
  domain: string
  domains?: string[]
  marketplace: string
  at: string
}

const MAX_ITEMS = 5

export function rememberSearch(
  current: RecentSearch[],
  entry: Omit<RecentSearch, 'at'> & { at?: string },
): RecentSearch[] {
  const next: RecentSearch = {
    ...entry,
    at: entry.at ?? new Date().toISOString(),
  }
  return [
    next,
    ...current.filter(
      (item) =>
        item.query.toLocaleLowerCase() !== next.query.toLocaleLowerCase() ||
        item.domain !== next.domain,
    ),
  ].slice(0, MAX_ITEMS)
}

export function formatRecentTime(
  at: string,
  t: Translate,
  now = Date.now(),
): string {
  const timestamp = Date.parse(at)
  if (Number.isNaN(timestamp)) {
    return ''
  }
  const seconds = Math.max(0, Math.floor((now - timestamp) / 1000))
  if (seconds < 60) {
    return t('common.now')
  }
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) {
    return t('common.minutesShort', { count: minutes })
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return t('common.hoursShort', { count: hours })
  }
  const days = Math.floor(hours / 24)
  if (days === 1) {
    return t('common.yesterday')
  }
  return t('common.days', { count: days })
}
