import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
} from 'react'
import {
  ApiError,
  addCompareItems,
  addHistoryProduct,
  addLibraryGroupProducts,
  applyUpdate,
  addLibraryProduct,
  checkForUpdates,
  clearCompareItems,
  clearHistory,
  deleteHistoryEntry,
  cancelJob,
  createLibraryGroup as createLibraryGroupRequest,
  deleteLibraryGroup as deleteLibraryGroupRequest,
  deleteLibraryProduct,
  emptyCompareState,
  ensureSession,
  getCompare,
  getDealOptions,
  getHistory,
  getHistoryCache,
  getJob,
  getLibrary,
  getMarketplaces,
  getProduct,
  getUiState,
  getUpdateStatus,
  refreshProductDetails,
  removeCompareItem,
  removeLibraryGroupProduct,
  saveUiPreferences,
  updateLibraryGroup,
  saveUiRecentSearches,
  shutdown,
  startDeals,
  startSearch,
  type DealCategory,
  type DealType,
  type CompareState,
  type HistoryEntry,
  type Library,
  type LibraryEnvelope,
  type LibraryGroup,
  type Marketplace,
  type Meta,
  type ProductResponse,
  type SearchInput,
  type SearchItem,
  type SearchJob,
  type SearchResultPayload,
  type ThemePreference,
  type UiPreferences,
  type UiState,
  type UpdateStatus,
} from './lib/api'
import { DealsView } from './components/DealsView'
import { CompareView } from './components/CompareView'
import { HistoryView, type HistoryFilter } from './components/HistoryView'
import { MonitoringView } from './components/MonitoringView'
import { ProductView } from './components/ProductView'
import { SavedProductsView } from './components/SavedProductsView'
import { SearchView } from './components/SearchView'
import { SettingsView } from './components/SettingsView'
import { Sidebar } from './components/Sidebar'
import { StatusView } from './components/StatusView'
import { Icon } from './components/ui'
import { useElasticOverscroll } from './lib/useElasticOverscroll'
import { useI18n } from './i18n/I18nProvider'
import {
  isLanguageCode,
  languageCodes,
  type LanguageCode,
} from './i18n/types'
import { rememberSearch, type RecentSearch } from './lib/recent'
import {
  defaultSearchFilters,
  filtersFromPersisted,
  filtersToPersisted,
  sortOptions,
  type SearchFilterState,
} from './lib/searchFilters'
import type { ResultsLayout } from './components/ResultsList'
import {
  historyFiltersFromPersisted,
  historyProductSnapshot,
  historySearchFiltersFromEntry,
} from './lib/history'
import { looksLikeAsin } from './lib/asin'
import { updateStageLabel } from './lib/update'
import {
  createLibraryConnection,
  type HistoryChangedEvent,
  type LibraryChangedEvent,
  type MonitorChangedEvent,
  type CompareChangedEvent,
  type UpdateChangedEvent,
  type UpdateProgressEvent,
} from './lib/realtime'
import {
  createSavedProductFromDetail,
  createSavedProductFromSearch,
  isProductSaved,
  libraryProductId,
} from './lib/library'

type Theme = 'light' | 'dark'
type View =
  | 'search'
  | 'detail'
  | 'deals'
  | 'compare'
  | 'saved'
  | 'history'
  | 'monitoring'
  | 'status'
  | 'settings'
type SavedTab = 'groups' | 'products'
type Notice = { code: string; message: string }

function systemTheme(): Theme {
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light'
}

function preferencesJson(preferences: UiPreferences): string {
  return JSON.stringify(preferences)
}

function messageOf(
  error: unknown,
  fallback = 'Unexpected error.',
): string {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message
  }
  return fallback
}

function noticeOf(error: unknown, fallback = 'Unexpected error.'): Notice {
  if (error instanceof ApiError) {
    return { code: error.code, message: error.message }
  }
  return { code: 'unexpected', message: messageOf(error, fallback) }
}

function historyUrl(): string {
  return '?view=history'
}

function monitoringUrl(): string {
  return '?view=monitoring'
}

function statusUrl(): string {
  return '?view=status'
}

function settingsUrl(): string {
  return '?view=settings'
}

function compareUrl(): string {
  return '?view=compare'
}

function savedUrl(tab: SavedTab, groupId?: string | null): string {
  const params = new URLSearchParams({ view: 'saved', tab })
  if (groupId) {
    params.set('group', groupId)
  }
  return `?${params.toString()}`
}

function newGroupId(): string {
  const random =
    globalThis.crypto?.randomUUID?.().replace(/-/g, '') ??
    `${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`
  return `g_${random.slice(0, 12)}`
}

function searchUrl(input: SearchInput): string {
  const params = new URLSearchParams({
    q: input.query,
    domain: input.domains[0] ?? input.domain,
    domains: input.domains.join(','),
  })
  if (input.sort !== 'relevance') {
    params.set('sort', input.sort)
  }
  return `${location.pathname}?${params.toString()}`
}

function dealsUrl(
  domain: string,
  type: DealType,
  category: string | null,
  page = 1,
): string {
  const params = new URLSearchParams({
    deals: '1',
    domain,
    deal_type: type,
    page: String(page),
  })
  if (category) {
    params.set('category', category)
  }
  return `${location.pathname}?${params.toString()}`
}

const PAGE_CACHE_TTL_MS = 15 * 60 * 1000
const PAGE_CACHE_MAX_ENTRIES = 60

type CachedSearchPage = {
  at: number
  result: SearchResultPayload
}

function pageCacheKey(input: SearchInput, page: number): string {
  return [
    input.query.trim().toLowerCase(),
    input.domains.join(','),
    input.no_ads ? 1 : 0,
    input.delivery ? 1 : 0,
    input.enrich ? 1 : 0,
    input.sort,
    input.delivery_country,
    input.delivery_postal_code,
    page,
  ].join('|')
}

function pageCachePrefix(input: SearchInput): string {
  return [
    input.query.trim().toLowerCase(),
    input.domains.join(','),
    input.no_ads ? 1 : 0,
    input.delivery ? 1 : 0,
    input.enrich ? 1 : 0,
    input.sort,
    input.delivery_country,
    input.delivery_postal_code,
  ].join('|')
}

const DEAL_CACHE_TTL_MS = 2 * 60 * 1000
const DEAL_CACHE_MAX_ENTRIES = 40

type DealCacheInput = {
  domain: string
  type: DealType
  category: string | null
  page: number
  delivery_country: string
  delivery_postal_code: string
}

type CachedDealPage = {
  at: number
  result: SearchResultPayload
}

function dealCacheKey(input: DealCacheInput): string {
  return [
    input.domain,
    input.type,
    input.category ?? '',
    input.page,
    input.delivery_country,
    input.delivery_postal_code,
  ].join('|')
}

export default function App() {
  const { language, setLanguage, t } = useI18n()
  const [themePreference, setThemePreference] =
    useState<ThemePreference>('system')
  const [deliveryCountry, setDeliveryCountry] = useState('')
  const [deliveryPostalCode, setDeliveryPostalCode] = useState('')
  const [theme, setTheme] = useState<Theme>(systemTheme)
  const [booting, setBooting] = useState(true)
  const [sessionError, setSessionError] = useState<string | null>(null)
  const [meta, setMeta] = useState<Meta | null>(null)
  const [marketplaces, setMarketplaces] = useState<Marketplace[]>([])
  const availableLanguages = useMemo<LanguageCode[]>(() => {
    const available = new Set<LanguageCode>(
      marketplaces.map((marketplace) => marketplace.language),
    )
    if (available.size === 0) {
      available.add('en')
    }
    return languageCodes.filter((code) => available.has(code))
  }, [marketplaces])
  const [domain, setDomain] = useState('it')
  const [domains, setDomains] = useState<string[]>(['it'])
  const [query, setQuery] = useState(
    () => new URLSearchParams(location.search).get('q') ?? '',
  )
  const [filters, setFilters] =
    useState<SearchFilterState>(defaultSearchFilters)
  const [resultsLayout, setResultsLayout] = useState<ResultsLayout>('list')
  const [monitoringLayout, setMonitoringLayout] =
    useState<ResultsLayout>('grid')
  const [jobId, setJobId] = useState<string | null>(null)
  const [job, setJob] = useState<SearchJob | null>(null)
  const [results, setResults] = useState<SearchResultPayload | null>(null)
  const [error, setError] = useState<Notice | null>(null)
  const [view, setView] = useState<View>('search')
  const [library, setLibrary] = useState<Library>({ products: [], groups: [] })
  const [historyEntries, setHistoryEntries] = useState<HistoryEntry[]>([])
  const [historyFilter, setHistoryFilter] = useState<HistoryFilter>('all')
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [updateStatus, setUpdateStatus] = useState<UpdateStatus | null>(null)
  const [updateLoading, setUpdateLoading] = useState(false)
  const [updateChecking, setUpdateChecking] = useState(false)
  const [updateStarting, setUpdateStarting] = useState(false)
  const [updateError, setUpdateError] = useState<string | null>(null)
  const [updateOverlayStuck, setUpdateOverlayStuck] = useState(false)
  const [updateBadge, setUpdateBadge] = useState(false)
  const [updateOverlay, setUpdateOverlay] = useState(false)
  const [cachedSearchNotice, setCachedSearchNotice] = useState<string | null>(
    null,
  )
  const [savedTab, setSavedTab] = useState<SavedTab>('groups')
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null)
  const [savedBadge, setSavedBadge] = useState(false)
  const [savedError, setSavedError] = useState<string | null>(null)
  const [compare, setCompare] = useState<CompareState>(emptyCompareState)
  const [compareBadge, setCompareBadge] = useState(false)
  const [compareLoading, setCompareLoading] = useState(false)
  const [compareError, setCompareError] = useState<string | null>(null)
  const [compareLoadError, setCompareLoadError] = useState<string | null>(null)
  const [monitorRevision, setMonitorRevision] = useState(0)
  const [realtimeReady, setRealtimeReady] = useState(false)
  const libraryRef = useRef<Library>({ products: [], groups: [] })
  const libraryRevisionRef = useRef(0)
  const libraryPolicyRef = useRef({
    autoRefreshAfterMinutes: 7 * 24 * 60,
    groupRefreshCooldownMinutes: 24 * 60,
    groupRefreshDelaySeconds: 6,
  })
  const savedTabRef = useRef<SavedTab>('groups')
  const selectedGroupIdRef = useRef<string | null>(null)
  const compareIdsRef = useRef<Set<string>>(new Set())
  const compareRevisionRef = useRef(0)
  const [dealDomain, setDealDomain] = useState('it')
  const [dealType, setDealType] = useState<DealType>('today')
  const [dealTypes, setDealTypes] = useState<DealType[]>(['all'])
  const [dealCategory, setDealCategory] = useState<string | null>(null)
  const [dealCategories, setDealCategories] = useState<DealCategory[]>([])
  const [dealOptionsLoading, setDealOptionsLoading] = useState(false)
  const [dealOptionsError, setDealOptionsError] = useState<string | null>(null)
  const [dealJobId, setDealJobId] = useState<string | null>(null)
  const [dealJob, setDealJob] = useState<SearchJob | null>(null)
  const [dealStarting, setDealStarting] = useState(false)
  const [dealResults, setDealResults] =
    useState<SearchResultPayload | null>(null)
  const [dealError, setDealError] = useState<Notice | null>(null)
  const [selectedAsin, setSelectedAsin] = useState<string | null>(null)
  const [product, setProduct] = useState<ProductResponse | null>(null)
  const [productLoading, setProductLoading] = useState(false)
  const [productError, setProductError] = useState<string | null>(null)
  const [productRefreshing, setProductRefreshing] = useState(false)
  const [productRefreshError, setProductRefreshError] = useState<string | null>(
    null,
  )
  const [closed, setClosed] = useState(false)
  const [closeConfirm, setCloseConfirm] = useState(false)
  const [closeError, setCloseError] = useState<string | null>(null)
  const [recentSearches, setRecentSearches] = useState<RecentSearch[]>([])
  const [railPinned, setRailPinned] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  const searchRef = useRef<HTMLInputElement>(null)
  const workspaceRef = useRef<HTMLDivElement>(null)
  const pageCacheRef = useRef(new Map<string, CachedSearchPage>())
  const activeJobRef = useRef<string | null>(null)
  const activeDealJobRef = useRef<string | null>(null)
  const dealOptionsAbortRef = useRef<AbortController | null>(null)
  const dealCacheRef = useRef(new Map<string, CachedDealPage>())
  const dealJobRequestRef = useRef<{
    jobId: string
    request: DealCacheInput
  } | null>(null)
  const dealSessionRef = useRef(0)
  const dealRunRef = useRef(0)
  const dealStartChainRef = useRef<Promise<void>>(Promise.resolve())
  const dealCancelChainRef = useRef<Promise<void>>(Promise.resolve())
  const productOriginRef = useRef<'search' | 'deals' | 'saved' | 'history' | 'compare'>('search')
  const productAbortRef = useRef<AbortController | null>(null)
  const productRefreshAbortRef = useRef<AbortController | null>(null)
  const updateStartVersionRef = useRef<string | null>(null)
  const updateReloadingRef = useRef(false)
  const lastSearchRef = useRef<SearchInput | null>(null)
  const domainRef = useRef(domain)
  const dealDomainRef = useRef(dealDomain)
  const viewRef = useRef<View>(view)
  const scrollPositionsRef = useRef<Record<View, number>>({
    search: 0,
    deals: 0,
    compare: 0,
    detail: 0,
    saved: 0,
    history: 0,
    monitoring: 0,
    status: 0,
    settings: 0,
  })
  const uiReadyRef = useRef(false)
  const bootedRef = useRef(false)

  const clearNetworkCaches = useCallback(() => {
    pageCacheRef.current.clear()
    dealCacheRef.current.clear()
  }, [])
  const deliveryCountryRef = useRef('')
  const deliveryPostalCodeRef = useRef('')
  const lastSavedPreferencesRef = useRef<string | null>(null)
  const lastSavedRecentRef = useRef<RecentSearch[] | null>(null)

  useEffect(() => {
    const policy = meta?.library
    if (!policy) {
      return
    }
    libraryPolicyRef.current = {
      autoRefreshAfterMinutes: policy.auto_refresh_after_minutes,
      groupRefreshCooldownMinutes:
        policy.group_refresh_cooldown_minutes,
      groupRefreshDelaySeconds: policy.group_refresh_delay_seconds,
    }
  }, [meta])

  useElasticOverscroll(
    workspaceRef,
    Boolean(meta) && !closed && !sessionError,
  )

  useEffect(() => {
    const primary = domains[0] ?? 'it'
    setDomain(primary)
    domainRef.current = primary
  }, [domains])

  const updateFilters = useCallback(
    (patch: Partial<SearchFilterState>) => {
      setFilters((current) => ({ ...current, ...patch }))
    },
    [],
  )

  const updateDomains = useCallback((next: string[]) => {
    if (next.length > 0) {
      setDomains(next)
    }
  }, [])

  const updateDeliveryLocation = useCallback(
    (country: string, postalCode: string) => {
      deliveryCountryRef.current = country
      deliveryPostalCodeRef.current = postalCode
      setDeliveryCountry(country)
      setDeliveryPostalCode(postalCode)
    },
    [],
  )

  const toggleDomain = useCallback((id: string) => {
    setDomains((current) => {
      const selected = current.includes(id)
      if (selected && current.length === 1) {
        return current
      }
      return selected
        ? current.filter((item) => item !== id)
        : [...current, id]
    })
  }, [])

  useEffect(() => {
    document.documentElement.dataset.theme = theme
  }, [theme])

  useEffect(() => {
    const media = window.matchMedia?.('(prefers-color-scheme: dark)')
    const apply = () => {
      setTheme(themePreference === 'system'
        ? (media?.matches ? 'dark' : 'light')
        : themePreference)
    }
    apply()
    if (!media || themePreference !== 'system') {
      return
    }
    media.addEventListener('change', apply)
    return () => media.removeEventListener('change', apply)
  }, [themePreference])

  useEffect(() => {
    if (!uiReadyRef.current) {
      return
    }

    const preferences: UiPreferences = {
      theme: themePreference,
      language,
      delivery_country: deliveryCountry,
      delivery_postal_code: deliveryPostalCode,
      domain: domains[0] ?? domain,
      domains,
      deals_domain: dealDomain,
      rail_pinned: railPinned,
      results_layout: resultsLayout,
      monitoring_layout: monitoringLayout,
      filters: filtersToPersisted(filters),
    }
    const serialized = preferencesJson(preferences)
    if (lastSavedPreferencesRef.current === serialized) {
      return
    }

    const timer = window.setTimeout(() => {
      lastSavedPreferencesRef.current = serialized
      void saveUiPreferences(preferences).catch(() => {
        // Preferences are non-critical; the current session keeps working.
      })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [
    dealDomain,
    deliveryCountry,
    deliveryPostalCode,
    domain,
    domains,
    filters,
    language,
    railPinned,
    resultsLayout,
    monitoringLayout,
    themePreference,
  ])

  useEffect(() => {
    if (!uiReadyRef.current) {
      return
    }
    if (lastSavedRecentRef.current === recentSearches) {
      return
    }

    const timer = window.setTimeout(() => {
      lastSavedRecentRef.current = recentSearches
      void saveUiRecentSearches(recentSearches).catch(() => {
        // Keep the in-memory history if the local state file is read-only.
      })
    }, 250)
    return () => window.clearTimeout(timer)
  }, [recentSearches])

  useEffect(() => {
    setMobileMenuOpen(false)
  }, [view, selectedAsin])

  useEffect(() => {
    savedTabRef.current = savedTab
  }, [savedTab])

  useEffect(() => {
    selectedGroupIdRef.current = selectedGroupId
  }, [selectedGroupId])

  const applyLibrary = useCallback((envelope: LibraryEnvelope) => {
    const next = {
      products: envelope.products,
      groups: envelope.groups,
    }
    libraryRef.current = next
    setLibrary(next)
    if (Number.isFinite(envelope.revision)) {
      libraryRevisionRef.current = envelope.revision
    }
    setSavedError(null)
  }, [])

  const libraryQueueRef = useRef<Promise<void>>(Promise.resolve())

  const refreshLibrary = useCallback(
    async (notify = false, knownRevision?: number) => {
      if (
        knownRevision !== undefined
        && knownRevision <= libraryRevisionRef.current
      ) {
        return
      }
      try {
        await libraryQueueRef.current.catch(() => undefined)
        const envelope = await getLibrary()
        const previousIds = new Set(
          libraryRef.current.products.map((product) => product.id),
        )
        applyLibrary(envelope)
        if (
          notify &&
          envelope.products.some((product) => !previousIds.has(product.id)) &&
          !(viewRef.current === 'saved' && savedTabRef.current === 'products')
        ) {
          setSavedBadge(true)
        }
      } catch (caught) {
        setSavedError(messageOf(caught, t('app.unexpectedError')))
      }
    },
    [applyLibrary, t],
  )

  useEffect(() => {
    const handleFocus = () => {
      if (document.visibilityState === 'visible' && uiReadyRef.current) {
        void refreshLibrary(true)
      }
    }
    window.addEventListener('focus', handleFocus)
    document.addEventListener('visibilitychange', handleFocus)
    return () => {
      window.removeEventListener('focus', handleFocus)
      document.removeEventListener('visibilitychange', handleFocus)
    }
  }, [refreshLibrary])


  const refreshHistory = useCallback(
    async (background = false) => {
      if (!background) {
        setHistoryLoading(true)
      }
      setHistoryError(null)
      try {
        const next = await getHistory()
        setHistoryEntries(next.entries)
      } catch (caught) {
        setHistoryError(messageOf(caught, t('app.unexpectedError')))
      } finally {
        if (!background) {
          setHistoryLoading(false)
        }
      }
    },
    [t],
  )

  const refreshUpdateStatus = useCallback(
    async (background = false) => {
      if (!background) {
        setUpdateLoading(true)
      }
      setUpdateError(null)
      try {
        const next = await getUpdateStatus()
        setUpdateStatus(next)
        setUpdateBadge(Boolean(next.check?.can_apply))
      } catch (caught) {
        if (!background) {
          setUpdateError(messageOf(caught, t('app.unexpectedError')))
        }
      } finally {
        if (!background) {
          setUpdateLoading(false)
        }
      }
    },
    [t],
  )

  const openStatusView = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setView('status')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    setMobileMenuOpen(false)
    history.pushState({ view: 'status' }, '', statusUrl())
    void refreshUpdateStatus()
  }, [refreshUpdateStatus])

  const openSettingsView = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setView('settings')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    setMobileMenuOpen(false)
    history.pushState({ view: 'settings' }, '', settingsUrl())
    void refreshUpdateStatus()
    void refreshHistory()
  }, [refreshHistory, refreshUpdateStatus])

  const openMonitoringView = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setView('monitoring')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    setMobileMenuOpen(false)
    history.pushState({ view: 'monitoring' }, '', monitoringUrl())
  }, [])

  const applyCompareState = useCallback(
    (next: CompareState, markBadge = false) => {
      if (next.revision < compareRevisionRef.current) {
        return false
      }
      const previous = compareIdsRef.current
      const nextIds = new Set(next.items.map((item) => item.id))
      const added = next.items.some((item) => !previous.has(item.id))
      compareRevisionRef.current = next.revision
      compareIdsRef.current = nextIds
      setCompare(next)
      if (markBadge && added && viewRef.current !== 'compare') {
        setCompareBadge(true)
      }
      return added
    },
    [],
  )

  const refreshCompare = useCallback(
    async (initial = false) => {
      setCompareLoading(true)
      try {
        const next = await getCompare()
        applyCompareState(next, !initial)
        setCompareLoadError(null)
      } catch (caught) {
        if (caught instanceof ApiError && caught.status === 401) {
          setSessionError(caught.message)
        } else {
          setCompareLoadError(
            messageOf(caught, t('app.unexpectedError')),
          )
        }
      } finally {
        setCompareLoading(false)
      }
    },
    [applyCompareState, t],
  )

  const openCompareView = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setView('compare')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    setMobileMenuOpen(false)
    setCompareBadge(false)
    history.pushState({ view: 'compare' }, '', compareUrl())
    void refreshCompare(true)
  }, [refreshCompare])

  const toggleCompareProduct = useCallback(
    async (productDomain: string, asin: string) => {
      setCompareError(null)
      const id = `${productDomain}:${asin}`
      try {
        if (compareIdsRef.current.has(id)) {
          applyCompareState(
            await removeCompareItem(productDomain, asin),
          )
        } else {
          applyCompareState(
            await addCompareItems([
              { domain: productDomain, asin },
            ]),
            true,
          )
        }
        setCompareLoadError(null)
      } catch (caught) {
        if (caught instanceof ApiError && caught.code === 'compare_full') {
          setCompareError(t('compare.full'))
        } else {
          setCompareError(
            messageOf(caught, t('app.unexpectedError')),
          )
        }
      }
    },
    [applyCompareState, t],
  )

  const removeCompareProduct = useCallback(
    async (productDomain: string, asin: string) => {
      setCompareError(null)
      try {
        applyCompareState(
          await removeCompareItem(productDomain, asin),
        )
      } catch (caught) {
        setCompareError(
          messageOf(caught, t('app.unexpectedError')),
        )
      }
    },
    [applyCompareState, t],
  )

  const clearCompareView = useCallback(async () => {
    setCompareError(null)
    try {
      applyCompareState(await clearCompareItems())
      setCompareBadge(false)
    } catch (caught) {
      setCompareError(
        messageOf(caught, t('app.unexpectedError')),
      )
    }
  }, [applyCompareState, t])

  useEffect(() => {
    const handleCompareFocus = () => {
      if (document.visibilityState === 'visible' && uiReadyRef.current) {
        void refreshCompare()
      }
    }
    window.addEventListener('focus', handleCompareFocus)
    document.addEventListener('visibilitychange', handleCompareFocus)
    return () => {
      window.removeEventListener('focus', handleCompareFocus)
      document.removeEventListener('visibilitychange', handleCompareFocus)
    }
  }, [refreshCompare])

  const checkUpdates = useCallback(async () => {
    setUpdateChecking(true)
    setUpdateError(null)
    try {
      await checkForUpdates()
      for (let attempt = 0; attempt < 90; attempt += 1) {
        await new Promise((resolve) => window.setTimeout(resolve, 700))
        const next = await getUpdateStatus()
        setUpdateStatus(next)
        setUpdateBadge(Boolean(next.check?.can_apply))
        if (next.job.phase !== 'checking' && next.job.phase !== 'idle') {
          return
        }
      }
    } catch (caught) {
      setUpdateError(messageOf(caught, t('app.unexpectedError')))
    } finally {
      setUpdateChecking(false)
    }
  }, [t])

  const applyUpdateNow = useCallback(async () => {
    setUpdateStarting(true)
    setUpdateError(null)
    setUpdateOverlayStuck(false)
    updateStartVersionRef.current = updateStatus?.current_version ?? null
    updateReloadingRef.current = false
    setUpdateOverlay(true)
    try {
      await applyUpdate()
      const next = await getUpdateStatus()
      setUpdateStatus(next)
    } catch (caught) {
      setUpdateOverlay(false)
      setUpdateError(messageOf(caught, t('app.unexpectedError')))
    } finally {
      setUpdateStarting(false)
    }
  }, [t, updateStatus?.current_version])

  useLayoutEffect(() => {
    if (!updateOverlay) {
      return
    }
    let disposed = false
    let whileDisconnected = 0
    let timer = 0
    const poll = async () => {
      try {
        const next = await getUpdateStatus()
        if (disposed) {
          return
        }
        whileDisconnected = 0
        setUpdateOverlayStuck(false)
        setUpdateStatus(next)
        setUpdateError(null)
        const startVersion = updateStartVersionRef.current
        const versionChanged =
          startVersion !== null
          && next.current_version !== startVersion
        const journalCompleted =
          next.journal?.phase === 'completed'
          && Boolean(next.journal.to_version)
          && next.journal.to_version !== startVersion
          && next.current_version === next.journal.to_version
        if (versionChanged || journalCompleted) {
          if (!updateReloadingRef.current) {
            updateReloadingRef.current = true
            window.setTimeout(() => location.reload(), 1200)
          }
          return
        }
        if (next.job.phase === 'failed' || next.job.phase === 'cancelled') {
          setUpdateOverlay(false)
          setUpdateError(
            next.job.error ?? next.job.message ?? t('status.updateFailed'),
          )
          return
        }
      } catch {
        // The Web process is restarting. Keep the overlay visible and retry.
        whileDisconnected += 1
        if (whileDisconnected >= 45) {
          setUpdateOverlayStuck(true)
        }
      }
      if (!disposed) {
        timer = window.setTimeout(poll, 1000)
      }
    }
    void poll()
    return () => {
      disposed = true
      window.clearTimeout(timer)
    }
  }, [t, updateOverlay])

  const openHistoryView = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setView('history')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    setMobileMenuOpen(false)
    history.pushState({ view: 'history' }, '', historyUrl())
    void refreshHistory()
  }, [refreshHistory])

  const removeHistoryEntry = useCallback(
    async (entry: HistoryEntry) => {
      try {
        const next = await deleteHistoryEntry(entry.id)
        setHistoryEntries(next.entries)
        setHistoryError(null)
      } catch (caught) {
        setHistoryError(messageOf(caught, t('app.unexpectedError')))
      }
    },
    [t],
  )

  const clearHistoryEntries = useCallback(async () => {
    try {
      const next = await clearHistory()
      setHistoryEntries(next.entries)
      setHistoryError(null)
      setCachedSearchNotice(null)
    } catch (caught) {
      setHistoryError(messageOf(caught, t('app.unexpectedError')))
    }
  }, [t])

  useEffect(() => {
    if (!realtimeReady) {
      return
    }
    let disposed = false
    const connection = createLibraryConnection(
      (event: LibraryChangedEvent) => {
        if (disposed) {
          return
        }
        if (typeof event?.revision === 'number') {
          void refreshLibrary(true, event.revision)
        } else {
          void refreshLibrary(true)
        }
      },
      () => {
        if (!disposed) {
          void refreshLibrary(true)
          if (viewRef.current === 'history') {
            void refreshHistory(true)
          }
          void refreshUpdateStatus(true)
        }
      },
      (event: HistoryChangedEvent) => {
        if (!disposed && viewRef.current === 'history') {
          void refreshHistory(typeof event?.revision === 'number')
        }
      },
      (event: UpdateChangedEvent) => {
        if (disposed) {
          return
        }
        if (event?.phase === 'update_available') {
          setUpdateBadge(true)
        }
        if (event?.phase === 'preparing' || event?.phase === 'scheduled') {
          setUpdateOverlay(true)
        }
        void refreshUpdateStatus(true)
      },
      (event: UpdateProgressEvent) => {
        if (disposed) {
          return
        }
        setUpdateStatus((current) => {
          if (!current) {
            return current
          }
          return {
            ...current,
            job: {
              ...current.job,
              revision: event.revision ?? current.job.revision,
              phase: event.phase ?? current.job.phase,
              message: event.message ?? current.job.message,
              progress: {
                stage: event.stage,
                message: event.message,
                percent: event.percent ?? null,
                bytes: event.bytes,
                total_bytes: event.total_bytes,
              },
              updated_at: event.updated_at,
            },
          }
        })
      },
      (event: MonitorChangedEvent) => {
        if (!disposed && typeof event?.revision === 'number') {
          setMonitorRevision((current) => Math.max(current + 1, event.revision))
        }
      },
      (event: CompareChangedEvent) => {
        if (!disposed && typeof event?.revision === 'number') {
          if (event.revision > compareRevisionRef.current) {
            void refreshCompare()
          }
        }
      },
    )
    void connection.start().catch(() => {
      // The focus refresh remains the fallback when SignalR is unavailable.
    })
    return () => {
      disposed = true
      void connection.stop()
    }
  }, [
    realtimeReady,
    refreshCompare,
    refreshHistory,
    refreshLibrary,
    refreshUpdateStatus,
  ])

  const runLibraryOperation = useCallback(
    (optimistic: Library, operation: () => Promise<LibraryEnvelope>) => {
      libraryRef.current = optimistic
      setLibrary(optimistic)
      setSavedError(null)
      libraryQueueRef.current = libraryQueueRef.current
        .catch(() => undefined)
        .then(async () => {
          try {
            applyLibrary(await operation())
          } catch (caught) {
            setSavedError(messageOf(caught, t('app.unexpectedError')))
            try {
              applyLibrary(await getLibrary())
            } catch {
              // Keep the optimistic state and show the error.
            }
          }
        })
    },
    [applyLibrary, t],
  )

  const removeSavedProduct = useCallback(
    (productId: string) => {
      const current = libraryRef.current
      runLibraryOperation(
        {
          products: current.products.filter(
            (product) => product.id !== productId,
          ),
          groups: current.groups.map((group) => ({
            ...group,
            items: group.items.filter(
              (item) => item.product_id !== productId,
            ),
          })),
        },
        () => deleteLibraryProduct(productId),
      )
    },
    [runLibraryOperation],
  )

  const toggleSavedItem = useCallback(
    (item: SearchItem) => {
      const itemDomain = item.marketplace_id ?? domainRef.current
      const id = libraryProductId(itemDomain, item.asin)
      const current = libraryRef.current
      const existing = current.products.find((product) => product.id === id)
      if (existing) {
        removeSavedProduct(id)
        return
      }

      const product = createSavedProductFromSearch(item, itemDomain)
      runLibraryOperation(
        {
          products: [product, ...current.products],
          groups: current.groups,
        },
        () => addLibraryProduct(product),
      )
      setSavedBadge(true)
    },
    [removeSavedProduct, runLibraryOperation],
  )

  const toggleSavedDetail = useCallback(() => {
    if (!product) {
      return
    }
    const id = libraryProductId(
      product.marketplace.id,
      product.product.asin,
    )
    const current = libraryRef.current
    const existing = current.products.find((item) => item.id === id)
    if (existing) {
      removeSavedProduct(id)
      return
    }

    const savedProduct = createSavedProductFromDetail(product)
    runLibraryOperation(
      {
        products: [savedProduct, ...current.products],
        groups: current.groups,
      },
      () => addLibraryProduct(savedProduct),
    )
    setSavedBadge(true)
  }, [product, removeSavedProduct, runLibraryOperation])

  const createLibraryGroup = useCallback(
    (
      name: string,
      accent: LibraryGroup['accent'],
      description: string,
      productIds: string[],
    ) => {
      const normalizedName = name.trim()
      if (!normalizedName) {
        return
      }
      const current = libraryRef.current
      const validIds = new Set(current.products.map((product) => product.id))
      const items = [...new Set(productIds)]
        .filter((id) => validIds.has(id))
        .map((productId) => ({ product_id: productId, quantity: 1 }))
      const now = new Date().toISOString()
      const group: LibraryGroup = {
        id: newGroupId(),
        name: normalizedName,
        accent,
        description: description.trim() || null,
        created_at: now,
        updated_at: now,
        items,
      }
      runLibraryOperation(
        {
          products: current.products,
          groups: [...current.groups, group],
        },
        () => createLibraryGroupRequest(group),
      )
    },
    [runLibraryOperation],
  )

  const renameLibraryGroup = useCallback(
    (
      groupId: string,
      name: string,
      accent: LibraryGroup['accent'],
      description: string,
    ) => {
      const normalizedName = name.trim()
      if (!normalizedName) {
        return
      }
      const current = libraryRef.current
      const now = new Date().toISOString()
      const normalizedDescription = description.trim() || null
      runLibraryOperation(
        {
          products: current.products,
          groups: current.groups.map((group) =>
            group.id === groupId
              ? {
                  ...group,
                  name: normalizedName,
                  accent,
                  description: normalizedDescription,
                  updated_at: now,
                }
              : group,
          ),
        },
        () =>
          updateLibraryGroup(
            groupId,
            normalizedName,
            accent,
            normalizedDescription ?? '',
          ),
      )
    },
    [runLibraryOperation],
  )

  const deleteLibraryGroup = useCallback(
    (groupId: string) => {
      const current = libraryRef.current
      runLibraryOperation(
        {
          products: current.products,
          groups: current.groups.filter((group) => group.id !== groupId),
        },
        () => deleteLibraryGroupRequest(groupId),
      )
      if (selectedGroupIdRef.current === groupId) {
        setSelectedGroupId(null)
        selectedGroupIdRef.current = null
      }
    },
    [runLibraryOperation],
  )

  const addProductsToGroup = useCallback(
    (groupId: string, productIds: string[]) => {
      const current = libraryRef.current
      const requested = new Set(productIds)
      const now = new Date().toISOString()
      runLibraryOperation(
        {
          products: current.products,
          groups: current.groups.map((group) => {
            if (group.id !== groupId) {
              return group
            }
            const existing = new Set(
              group.items.map((item) => item.product_id),
            )
            const additions = current.products
              .filter(
                (product) =>
                  requested.has(product.id) && !existing.has(product.id),
              )
              .map((product) => ({
                product_id: product.id,
                quantity: 1,
              }))
            return {
              ...group,
              items: [...group.items, ...additions],
              updated_at: now,
            }
          }),
        },
        () => addLibraryGroupProducts(groupId, productIds),
      )
    },
    [runLibraryOperation],
  )

  const removeGroupProduct = useCallback(
    (groupId: string, productId: string) => {
      const current = libraryRef.current
      const now = new Date().toISOString()
      runLibraryOperation(
        {
          products: current.products,
          groups: current.groups.map((group) =>
            group.id === groupId
              ? {
                  ...group,
                  items: group.items.filter(
                    (item) => item.product_id !== productId,
                  ),
                  updated_at: now,
                }
              : group,
          ),
        },
        () => removeLibraryGroupProduct(groupId, productId),
      )
    },
    [runLibraryOperation],
  )

  const openSavedView = useCallback((tab: SavedTab = 'groups') => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setProductLoading(false)
    setProductRefreshing(false)
    productOriginRef.current = 'saved'
    setView('saved')
    setSavedTab(tab)
    savedTabRef.current = tab
    setSelectedGroupId(null)
    selectedGroupIdRef.current = null
    if (tab === 'products') {
      setSavedBadge(false)
    }
    setMobileMenuOpen(false)
    history.pushState({ view: 'saved', tab }, '', savedUrl(tab))
  }, [])

  const openSavedGroup = useCallback((groupId: string) => {
    setView('saved')
    setSavedTab('groups')
    savedTabRef.current = 'groups'
    setSelectedGroupId(groupId)
    selectedGroupIdRef.current = groupId
    setSavedBadge(false)
    setMobileMenuOpen(false)
    history.pushState(
      { view: 'saved', tab: 'groups', groupId },
      '',
      savedUrl('groups', groupId),
    )
  }, [])

  const closeSavedGroup = useCallback(() => {
    setSelectedGroupId(null)
    selectedGroupIdRef.current = null
    history.replaceState(
      { view: 'saved', tab: 'groups' },
      '',
      savedUrl('groups'),
    )
  }, [])

  const changeSavedTab = useCallback((tab: SavedTab) => {
    setSavedTab(tab)
    savedTabRef.current = tab
    setSelectedGroupId(null)
    selectedGroupIdRef.current = null
    if (tab === 'products') {
      setSavedBadge(false)
    }
    history.replaceState({ view: 'saved', tab }, '', savedUrl(tab))
  }, [])

  const goBack = useCallback(() => {
    productAbortRef.current?.abort()
    productRefreshAbortRef.current?.abort()
    setProductRefreshing(false)
    setProductRefreshError(null)
    if (
      history.state?.view === 'detail' &&
      (history.state?.fromSearch === true ||
        history.state?.fromDeals === true ||
        history.state?.fromSaved === true ||
        history.state?.fromCompare === true ||
        history.state?.fromHistory === true)
    ) {
      history.back()
      return
    }

    if (productOriginRef.current === 'compare') {
      setView('compare')
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
      history.replaceState({ view: 'compare' }, '', compareUrl())
      return
    }

    if (productOriginRef.current === 'saved') {
      setView('saved')
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
      history.replaceState(
        {
          view: 'saved',
          tab: savedTabRef.current,
          groupId: selectedGroupIdRef.current,
        },
        '',
        savedUrl(savedTabRef.current, selectedGroupIdRef.current),
      )
      return
    }

    if (productOriginRef.current === 'history') {
      setView('history')
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
      history.replaceState({ view: 'history' }, '', historyUrl())
      void refreshHistory()
      return
    }

    if (productOriginRef.current === 'deals') {
      setView('deals')
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
      history.replaceState(
        { view: 'deals' },
        '',
        dealsUrl(
          dealDomain,
          dealType,
          dealCategory,
          dealResults?.requested_pages[0] ?? 1,
        ),
      )
      return
    }

    setView('search')
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    const last = lastSearchRef.current
    history.replaceState(
      { view: 'search' },
      '',
      last ? searchUrl(last) : location.pathname,
    )
  }, [dealCategory, dealDomain, dealResults, dealType])

  const storeSearchPage = (
    request: SearchInput,
    page: number,
    result: SearchResultPayload,
  ) => {
    const cache = pageCacheRef.current
    cache.set(pageCacheKey(request, page), {
      at: Date.now(),
      result,
    })
    if (cache.size > PAGE_CACHE_MAX_ENTRIES) {
      const oldest = [...cache.entries()]
        .sort((left, right) => left[1].at - right[1].at)
        .slice(0, cache.size - PAGE_CACHE_MAX_ENTRIES)
      for (const [key] of oldest) {
        cache.delete(key)
      }
    }
  }

  const readSearchPage = (
    request: SearchInput,
    page: number,
  ): SearchResultPayload | null => {
    const cache = pageCacheRef.current
    const key = pageCacheKey(request, page)
    const cached = cache.get(key)
    if (!cached) {
      return null
    }
    if (Date.now() - cached.at > PAGE_CACHE_TTL_MS) {
      cache.delete(key)
      return null
    }
    return cached.result
  }

  const clearSearchPages = (request: SearchInput) => {
    const prefix = pageCachePrefix(request)
    for (const key of pageCacheRef.current.keys()) {
      if (key.startsWith(`${prefix}|`)) {
        pageCacheRef.current.delete(key)
      }
    }
  }

  const refreshSavedProduct = useCallback(
    (asin: string, productDomain: string, signal: AbortSignal) =>
      refreshProductDetails(
        asin,
        productDomain,
        signal,
        deliveryCountryRef.current,
        deliveryPostalCodeRef.current,
      ),
    [],
  )

  const openProduct = useCallback(
    async (
      asin: string,
      productDomain: string,
      push = true,
      replaceHistory = false,
      historyEntry?: HistoryEntry,
    ) => {
      productAbortRef.current?.abort()
      productRefreshAbortRef.current?.abort()
      const controller = new AbortController()
      productAbortRef.current = controller
      setView('detail')
      setSelectedAsin(asin)
      setProduct(null)
      setProductError(null)
      setProductRefreshError(null)
      setCompareError(null)
      setProductRefreshing(false)
      setProductLoading(true)
      if (push) {
        const params = new URLSearchParams({ asin, domain: productDomain })
        const origin = productOriginRef.current
        const state = {
          view: 'detail',
          fromSearch: origin === 'search' && lastSearchRef.current !== null,
          fromDeals: origin === 'deals',
          fromSaved: origin === 'saved',
          fromHistory: origin === 'history',
          fromCompare: origin === 'compare',
        }
        if (replaceHistory) {
          history.replaceState(state, '', `?${params.toString()}`)
        } else {
          history.pushState(state, '', `?${params.toString()}`)
        }
      }

      try {
        const data = await getProduct(
          asin,
          productDomain,
          controller.signal,
          deliveryCountryRef.current,
          deliveryPostalCodeRef.current,
        )
        if (!controller.signal.aborted) {
          setProduct(data)
          if (!historyEntry) {
            void addHistoryProduct({
              asin: data.product.asin,
              domain: data.marketplace.id,
              snapshot: historyProductSnapshot(data),
            }).catch(() => {
              // History is best effort and must not block the product page.
            })
          }
          if (
            productOriginRef.current === 'saved'
            && data.cached
          ) {
            const productId = libraryProductId(
              data.marketplace.id,
              data.product.asin,
            )
            const savedProduct = libraryRef.current.products.find(
              (item) => item.id === productId,
            )
            const policy = libraryPolicyRef.current
            const checked = savedProduct?.last_checked_at
              ? Date.parse(savedProduct.last_checked_at)
              : Number.NaN
            const cacheAgeMs = Math.max(
              0,
              (data.cache_age_seconds ?? 0) * 1000,
            )
            const effectiveChecked = Math.max(
              Number.isFinite(checked) ? checked : 0,
              Date.now() - cacheAgeMs,
            )
            const stale =
              effectiveChecked === 0
              || Date.now() - effectiveChecked
                >= policy.autoRefreshAfterMinutes * 60_000
            if (savedProduct && stale) {
              const refreshController = new AbortController()
              productRefreshAbortRef.current = refreshController
              setProductRefreshing(true)
              setProductRefreshError(null)
              void (async () => {
                try {
                  const refreshed = await refreshProductDetails(
                    data.product.asin,
                    data.marketplace.id,
                    refreshController.signal,
                    deliveryCountryRef.current,
                    deliveryPostalCodeRef.current,
                  )
                  if (!refreshController.signal.aborted) {
                    setProduct(refreshed)
                  }
                } catch (caught) {
                  if (refreshController.signal.aborted) {
                    return
                  }
                  if (caught instanceof ApiError && caught.status === 401) {
                    setSessionError(caught.message)
                  } else {
                    setProductRefreshError(
                      messageOf(caught, t('app.unexpectedError')),
                    )
                  }
                } finally {
                  if (productRefreshAbortRef.current === refreshController) {
                    setProductRefreshing(false)
                    productRefreshAbortRef.current = null
                  }
                }
              })()
            }
          }
        }
      } catch (caught) {
        if (controller.signal.aborted) {
          return
        }
        if (caught instanceof ApiError && caught.status === 401) {
          setSessionError(caught.message)
        } else {
          setProductError(messageOf(caught, t('app.unexpectedError')))
        }
      } finally {
        if (productAbortRef.current === controller) {
          setProductLoading(false)
          productAbortRef.current = null
        }
      }
    },
    [t],
  )

  const refreshCurrentProduct = useCallback(async () => {
    if (!selectedAsin || !product) {
      return
    }

    productRefreshAbortRef.current?.abort()
    const controller = new AbortController()
    productRefreshAbortRef.current = controller
    setProductRefreshing(true)
    setProductRefreshError(null)
    try {
      const data = await refreshProductDetails(
        selectedAsin,
        product.marketplace.id,
        controller.signal,
        deliveryCountryRef.current,
        deliveryPostalCodeRef.current,
      )
      if (!controller.signal.aborted) {
        setProduct(data)
      }
    } catch (caught) {
      if (controller.signal.aborted) {
        return
      }
      if (caught instanceof ApiError && caught.status === 401) {
        setSessionError(caught.message)
      } else {
        setProductRefreshError(messageOf(caught, t('app.unexpectedError')))
      }
    } finally {
      if (productRefreshAbortRef.current === controller) {
        setProductRefreshing(false)
        productRefreshAbortRef.current = null
      }
    }
  }, [product, selectedAsin, t])

  const beginSearch = useCallback(
    async (input: SearchInput, push = true) => {
      productAbortRef.current?.abort()
      if (activeJobRef.current) {
        const previous = activeJobRef.current
        activeJobRef.current = null
        setJobId(null)
        try {
          await cancelJob(previous)
        } catch {
          // The previous job may already have finished.
        }
      }

      setError(null)
      setJob(null)
      setCachedSearchNotice(null)
      setView('search')
      setSelectedAsin(null)
      setProduct(null)
      lastSearchRef.current = input
      if (push) {
        history.pushState({ view: 'search' }, '', searchUrl(input))
      }

      try {
        const created = await startSearch(input)
        activeJobRef.current = created.job_id
        setJobId(created.job_id)
      } catch (caught) {
        if (caught instanceof ApiError && caught.status === 401) {
          setSessionError(caught.message)
          return
        }
        setError(noticeOf(caught, t('app.unexpectedError')))
      }
    },
    [],
  )

  const openHistorySearch = useCallback(
    async (entry: HistoryEntry) => {
      if (entry.status === 'running') {
        return
      }
      const persisted = historySearchFiltersFromEntry(entry)
      const nextFilters = historyFiltersFromPersisted(persisted)
      const entryDomain = entry.domain ?? domainRef.current
      const entryDomains = entry.domains?.length
        ? entry.domains
        : [entryDomain]
      if (entry.cached) {
        try {
          const cached = await getHistoryCache(entry.id)
          setFilters(nextFilters)
          setQuery(entry.query ?? '')
          setDomain(entryDomain)
          setDomains(entryDomains)
          setResults(cached.result)
          setJob(null)
          setJobId(null)
          setError(null)
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
          setView('search')
          setCachedSearchNotice(cached.cached_at)
          lastSearchRef.current = {
            query: entry.query ?? '',
            domain: entryDomain,
            domains: entryDomains,
            pages: 1,
            page: 1,
            no_ads: persisted.no_ads,
            delivery: persisted.delivery || persisted.free_shipping,
            enrich: persisted.enrich,
            sort: persisted.sort,
            delivery_country: deliveryCountryRef.current,
            delivery_postal_code: deliveryPostalCodeRef.current,
            history: { filters: persisted, entry_id: entry.id },
          }
          history.pushState(
            { view: 'search' },
            '',
            searchUrl(lastSearchRef.current),
          )
          return
        } catch {
          // Cache expired or missing: fall through to a fresh search.
        }
      }

      setFilters(nextFilters)
      setQuery(entry.query ?? '')
      setDomain(entryDomain)
      setDomains(entryDomains)
      setCachedSearchNotice(null)
      const input: SearchInput = {
        query: entry.query ?? '',
        domain: entryDomain,
        domains: entryDomains,
        pages: 1,
        page: 1,
        no_ads: persisted.no_ads,
        delivery: persisted.delivery || persisted.free_shipping,
        enrich: persisted.enrich,
        sort: persisted.sort,
        delivery_country: deliveryCountryRef.current,
        delivery_postal_code: deliveryPostalCodeRef.current,
        history: { filters: persisted, entry_id: entry.id },
      }
      void beginSearch(input, true)
    },
    [beginSearch],
  )

  const openHistoryProduct = useCallback(
    (entry: HistoryEntry) => {
      if (!entry.asin || !entry.domain) {
        return
      }
      productOriginRef.current = 'history'
      setMobileMenuOpen(false)
      void openProduct(entry.asin, entry.domain, true, false, entry)
    },
    [openProduct],
  )

  const runSearch = useCallback(
    (value: string, targetDomain = domain, targetDomains = domains) => {
      const trimmed = value.trim()
      if (trimmed.length === 0) {
        return
      }
      setMobileMenuOpen(false)
      if (looksLikeAsin(trimmed)) {
        void openProduct(trimmed.toUpperCase(), targetDomain)
        return
      }

      const names = targetDomains.map(
        (id) =>
          marketplaces.find((item) => item.id === id)?.display_name ??
          id.toUpperCase(),
      )
      const marketplaceName = names[0] ?? targetDomain.toUpperCase()
      const recentMarketplace =
        names.length > 1
          ? `${marketplaceName} +${names.length - 1}`
          : marketplaceName
      setRecentSearches((current) => {
        const next = rememberSearch(current, {
          query: trimmed,
          domain: targetDomain,
          domains: targetDomains,
          marketplace: recentMarketplace,
        })
        return next
      })
      clearSearchPages({
        query: trimmed,
        domain: targetDomain,
        domains: targetDomains,
        pages: 1,
        no_ads: filters.noAds,
        delivery: filters.delivery || filters.freeShipping,
        enrich: filters.enrich,
        sort: filters.sort,
        delivery_country: deliveryCountry,
        delivery_postal_code: deliveryPostalCode,
      })

      void beginSearch({
        query: trimmed,
        domain: targetDomain,
        domains: targetDomains,
        pages: 1,
        page: 1,
        no_ads: filters.noAds,
        delivery: filters.delivery || filters.freeShipping,
        enrich: filters.enrich,
        sort: filters.sort,
        delivery_country: deliveryCountry,
        delivery_postal_code: deliveryPostalCode,
        history: {
          filters: filtersToPersisted(filters),
        },
      })
    },
    [
      beginSearch,
      deliveryCountry,
      deliveryPostalCode,
      domain,
      domains,
      filters,
      marketplaces,
      openProduct,
    ],
  )

  const goToSearchPage = useCallback(
    (page: number) => {
      const last = lastSearchRef.current
      if (!last || page < 1) {
        return
      }

      const request = { ...last, page, pages: 1, history: undefined }
      const cached = readSearchPage(request, page)
      if (cached) {
        setError(null)
        setJob(null)
        setJobId(null)
        lastSearchRef.current = request
        setResults(cached)
        return
      }

      void beginSearch(request, false)
    },
    [beginSearch],
  )

  const cancelDealJob = useCallback((): Promise<void> => {
    const active = activeDealJobRef.current
    activeDealJobRef.current = null
    if (active && dealJobRequestRef.current?.jobId === active) {
      dealJobRequestRef.current = null
    }
    setDealJobId(null)
    setDealJob(null)
    const task = dealCancelChainRef.current.then(async () => {
      if (active) {
        try {
          await cancelJob(active)
        } catch {
          // The deal job may already have completed.
        }
      }
    })
    dealCancelChainRef.current = task.catch(() => {
      // Keep the cancellation queue usable.
    })
    return task
  }, [])

  const readDealCache = useCallback(
    (input: DealCacheInput): SearchResultPayload | null => {
      const cache = dealCacheRef.current
      const key = dealCacheKey(input)
      const entry = cache.get(key)
      if (!entry) {
        return null
      }
      if (Date.now() - entry.at > DEAL_CACHE_TTL_MS) {
        cache.delete(key)
        return null
      }
      return entry.result
    },
    [],
  )

  const storeDealCache = useCallback(
    (input: DealCacheInput, result: SearchResultPayload) => {
      const cache = dealCacheRef.current
      cache.set(dealCacheKey(input), { at: Date.now(), result })
      if (cache.size > DEAL_CACHE_MAX_ENTRIES) {
        const oldest = [...cache.entries()]
          .sort((left, right) => left[1].at - right[1].at)
          .slice(0, cache.size - DEAL_CACHE_MAX_ENTRIES)
        for (const [key] of oldest) {
          cache.delete(key)
        }
      }
    },
    [],
  )

  const runDeals = useCallback(
    (
      input: {
        domain: string
        type: DealType
        category: string | null
        page: number
      },
      force = false,
    ) => {
      const cacheInput: DealCacheInput = {
        domain: input.domain,
        type: input.type,
        category: input.category,
        page: input.page,
        delivery_country: deliveryCountry,
        delivery_postal_code: deliveryPostalCode,
      }
      const cached = force ? null : readDealCache(cacheInput)
      const runToken = dealRunRef.current + 1
      dealRunRef.current = runToken
      if (cached) {
        void cancelDealJob()
        setDealError(null)
        setDealResults(cached)
        setDealStarting(false)
        return Promise.resolve()
      }

      setDealStarting(true)
      const run = dealStartChainRef.current.then(async () => {
        try {
          if (runToken !== dealRunRef.current) {
            return
          }
          await cancelDealJob()
          if (runToken !== dealRunRef.current) {
            return
          }

          setDealError(null)
          setDealResults(null)
          try {
            const created = await startDeals({
              domain: input.domain,
              deal_type: input.type,
              category: input.category,
              page: input.page,
              no_ads: true,
              delivery: true,
              enrich: false,
              delivery_country: deliveryCountry,
              delivery_postal_code: deliveryPostalCode,
            })
            if (runToken !== dealRunRef.current) {
              try {
                await cancelJob(created.job_id)
              } catch {
                // The job may already have completed.
              }
              return
            }
            activeDealJobRef.current = created.job_id
            dealJobRequestRef.current = {
              jobId: created.job_id,
              request: cacheInput,
            }
            setDealJobId(created.job_id)
          } catch (caught) {
            if (runToken !== dealRunRef.current) {
              return
            }
            if (caught instanceof ApiError && caught.status === 401) {
              setSessionError(caught.message)
              return
            }
            setDealError(noticeOf(caught, t('app.unexpectedError')))
          }
        } finally {
          if (runToken === dealRunRef.current) {
            setDealStarting(false)
          }
        }
      })
      dealStartChainRef.current = run.catch(() => {
        // Keep the queue usable after a failed run.
      })
      return run
    },
    [cancelDealJob, deliveryCountry, deliveryPostalCode, readDealCache],
  )

  const loadDealOptions = useCallback(
    async (targetDomain: string, preferredType: DealType) => {
      dealOptionsAbortRef.current?.abort()
      const controller = new AbortController()
      dealOptionsAbortRef.current = controller
      setDealOptionsLoading(true)
      setDealOptionsError(null)
      try {
        const options = await getDealOptions(
          targetDomain,
          preferredType,
          controller.signal,
        )
        if (controller.signal.aborted) {
          return null
        }
        setDealTypes(options.deal_types)
        setDealCategories(options.categories)
        setDealType(options.deal_type)
        return options.deal_type
      } catch (caught) {
        if (controller.signal.aborted) {
          return null
        }
        setDealTypes(['all'])
        setDealCategories([])
        setDealType('all')
        setDealOptionsError(messageOf(caught, t('app.unexpectedError')))
        return 'all' as DealType
      } finally {
        if (dealOptionsAbortRef.current === controller) {
          dealOptionsAbortRef.current = null
          setDealOptionsLoading(false)
        }
      }
    },
    [],
  )

  const configureDeals = useCallback(
    async (
      targetDomain: string,
      preferredType: DealType,
      category: string | null,
      page: number,
      session: number,
      force = false,
    ) => {
      const chosen = await loadDealOptions(targetDomain, preferredType)
      if (chosen === null || session !== dealSessionRef.current) {
        return
      }
      setDealCategory(category)
      history.replaceState(
        { view: 'deals' },
        '',
        dealsUrl(targetDomain, chosen, category, page),
      )
      void runDeals(
        {
          domain: targetDomain,
          type: chosen,
          category,
          page,
        },
        force,
      )
    },
    [loadDealOptions, runDeals],
  )

  const openDeals = useCallback(
    async (
      input: {
        domain?: string
        type?: DealType
        category?: string | null
        page?: number
        push?: boolean
      } = {},
    ) => {
      const targetDomain = input.domain ?? dealDomainRef.current
      const preferredType = input.type ?? 'today'
      const category = input.category ?? null
      const page = input.page ?? 1
      const push = input.push ?? true

      dealOptionsAbortRef.current?.abort()
      dealSessionRef.current += 1
      dealRunRef.current += 1
      const session = dealSessionRef.current

      setView('deals')
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
      setDealDomain(targetDomain)
      dealDomainRef.current = targetDomain
      setDealType(preferredType)
      setDealCategory(category)
      setDealCategories([])
      setDealResults(null)
      setDealError(null)
      setDealOptionsError(null)
      setDealStarting(false)
      setMobileMenuOpen(false)
      if (push) {
        history.pushState(
          { view: 'deals' },
          '',
          dealsUrl(targetDomain, preferredType, category, page),
        )
      }

      await cancelDealJob()
      if (session !== dealSessionRef.current) {
        return
      }
      await configureDeals(
        targetDomain,
        preferredType,
        category,
        page,
        session,
      )
    },
    [cancelDealJob, configureDeals],
  )

  const changeDealDomain = (nextDomain: string) => {
    void cancelDealJob()
    dealOptionsAbortRef.current?.abort()
    dealSessionRef.current += 1
    dealRunRef.current += 1
    const session = dealSessionRef.current
    setDealDomain(nextDomain)
    dealDomainRef.current = nextDomain
    setDealType('today')
    setDealCategory(null)
    setDealCategories([])
    setDealResults(null)
    setDealError(null)
    setDealOptionsError(null)
    setDealStarting(false)
    void configureDeals(nextDomain, 'today', null, 1, session)
  }

  const changeDealType = (nextType: DealType) => {
    void cancelDealJob()
    dealOptionsAbortRef.current?.abort()
    dealSessionRef.current += 1
    dealRunRef.current += 1
    const session = dealSessionRef.current
    setDealType(nextType)
    setDealCategory(null)
    setDealCategories([])
    setDealResults(null)
    setDealError(null)
    setDealOptionsError(null)
    setDealStarting(false)
    void configureDeals(dealDomain, nextType, null, 1, session)
  }

  const changeDealCategory = (category: string | null) => {
    setDealCategory(category)
    setDealResults(null)
    setDealError(null)
    history.replaceState(
      { view: 'deals' },
      '',
      dealsUrl(dealDomain, dealType, category, 1),
    )
    void runDeals({
      domain: dealDomain,
      type: dealType,
      category,
      page: 1,
    })
  }

  const changeDealPage = (page: number) => {
    setDealResults(null)
    setDealError(null)
    history.replaceState(
      { view: 'deals' },
      '',
      dealsUrl(dealDomain, dealType, dealCategory, page),
    )
    void runDeals({
      domain: dealDomain,
      type: dealType,
      category: dealCategory,
      page,
    })
  }

  const refreshDeals = () => {
    void cancelDealJob()
    dealOptionsAbortRef.current?.abort()
    dealSessionRef.current += 1
    dealRunRef.current += 1
    const session = dealSessionRef.current
    const page = dealResults?.requested_pages[0] ?? 1
    setDealResults(null)
    setDealError(null)
    setDealOptionsError(null)
    void configureDeals(
      dealDomain,
      dealType,
      dealCategory,
      page,
      session,
      true,
    )
  }

  const openSearchView = useCallback(() => {
    if (view === 'search') {
      return
    }
    void cancelDealJob()
    dealOptionsAbortRef.current?.abort()
    dealOptionsAbortRef.current = null
    dealSessionRef.current += 1
    dealRunRef.current += 1
    productOriginRef.current = 'search'
    setCachedSearchNotice(null)
    setView('search')
    setDealStarting(false)
    setSelectedAsin(null)
    setProduct(null)
    setProductError(null)
    const last = lastSearchRef.current
    history.pushState(
      { view: 'search' },
      '',
      last ? searchUrl(last) : location.pathname,
    )
  }, [cancelDealJob, view])

  useEffect(() => {
    if (bootedRef.current) {
      return
    }
    bootedRef.current = true
    let alive = true
    const boot = async () => {
      try {
        const nextMeta = await ensureSession()
        const nextMarketplaces = await getMarketplaces()
        let nextUi: UiState | null = null
        let nextLibrary: LibraryEnvelope | null = null
        let nextCompare: CompareState | null = null
        try {
          nextUi = await getUiState()
        } catch (caught) {
          if (caught instanceof ApiError && caught.status === 401) {
            throw caught
          }
          // The UI can start with defaults and persist them on first change.
        }
        try {
          nextLibrary = await getLibrary()
        } catch (caught) {
          if (caught instanceof ApiError && caught.status === 401) {
            throw caught
          }
          // A missing library is not fatal; mutations will retry it.
        }
        try {
          nextCompare = await getCompare()
        } catch (caught) {
          if (caught instanceof ApiError && caught.status === 401) {
            throw caught
          }
          // The comparison is optional; the view can load it later.
        }
        if (!alive) {
          return
        }
        setMeta(nextMeta)
        setMarketplaces(nextMarketplaces.marketplaces)

        const preferences = nextUi?.preferences
        const loadedFilters = filtersFromPersisted(preferences?.filters)
        if (preferences) {
          setThemePreference(preferences.theme)
          setLanguage(
            isLanguageCode(preferences.language) ? preferences.language : 'en',
          )
          const storedCountry = preferences.delivery_country ?? ''
          const storedPostal = preferences.delivery_postal_code ?? ''
          deliveryCountryRef.current = storedCountry
          deliveryPostalCodeRef.current = storedPostal
          setDeliveryCountry(storedCountry)
          setDeliveryPostalCode(storedPostal)
          setRailPinned(preferences.rail_pinned)
          setResultsLayout(
            preferences.results_layout === 'grid' ? 'grid' : 'list',
          )
          setMonitoringLayout(
            preferences.monitoring_layout === 'list' ? 'list' : 'grid',
          )
          setRecentSearches(nextUi!.recent_searches)
          lastSavedRecentRef.current = nextUi!.recent_searches
        }
        const loadedLibrary: Library = nextLibrary
          ? {
              products: nextLibrary.products,
              groups: nextLibrary.groups,
            }
          : { products: [], groups: [] }
        libraryRef.current = loadedLibrary
        libraryRevisionRef.current = nextLibrary?.revision ?? 0
        setLibrary(loadedLibrary)
        setSavedError(null)
        applyCompareState(nextCompare ?? emptyCompareState)

        const knownMarketplace = (id: string) =>
          nextMarketplaces.marketplaces.some((item) => item.id === id)
        const params = new URLSearchParams(location.search)
        const requestedDomains = (params.get('domains') ?? '')
          .split(',')
          .map((item) => item.trim())
          .filter((item) => item.length > 0 && knownMarketplace(item))
        const requestedDomain = params.get('domain')
        const preferenceDomains = (
          preferences?.domains?.length
            ? preferences.domains
            : [preferences?.domain ?? nextMarketplaces.default_id]
        ).filter(knownMarketplace)
        const initialDomains =
          requestedDomains.length > 0
            ? requestedDomains
            : requestedDomain && knownMarketplace(requestedDomain)
              ? [requestedDomain]
              : preferenceDomains.length > 0
                ? preferenceDomains
                : [nextMarketplaces.default_id]
        const initialDomain = initialDomains[0]
        setDomains(initialDomains)
        setDomain(initialDomain)
        domainRef.current = initialDomain
        const storedDealDomain =
          preferences?.deals_domain &&
          knownMarketplace(preferences.deals_domain)
            ? preferences.deals_domain
            : initialDomain
        setDealDomain(storedDealDomain)
        dealDomainRef.current = storedDealDomain

        const requestedSort = params.get('sort')
        const initialFilters: SearchFilterState = {
          ...loadedFilters,
          sort: sortOptions.some((option) => option.value === requestedSort)
            ? (requestedSort as SearchFilterState['sort'])
            : loadedFilters.sort,
        }
        setFilters(initialFilters)

        const statusParam = params.get('view') === 'status'
        const settingsParam = params.get('view') === 'settings'
        const historyParam = params.get('view') === 'history'
        const savedParam = params.get('view') === 'saved'
        const monitoringParam = params.get('view') === 'monitoring'
        const compareParam = params.get('view') === 'compare'
        const dealParam = params.get('deals') === '1'
        if (settingsParam) {
          setView('settings')
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
          void refreshUpdateStatus()
          void refreshHistory()
        } else if (statusParam) {
          setView('status')
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
          void refreshUpdateStatus()
        } else if (historyParam) {
          setView('history')
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
          void refreshHistory()
        } else if (monitoringParam) {
          setView('monitoring')
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
        } else if (savedParam) {
          const tab: SavedTab =
            params.get('tab') === 'products' ? 'products' : 'groups'
          const groupId = params.get('group')
          setView('saved')
          setSavedTab(tab)
          savedTabRef.current = tab
          setSelectedGroupId(groupId)
          selectedGroupIdRef.current = groupId
          if (tab === 'products') {
            setSavedBadge(false)
          }
        } else if (compareParam) {
          setView('compare')
          setSelectedAsin(null)
          setProduct(null)
          setProductError(null)
          setCompareBadge(false)
        } else if (dealParam) {
          const requestedDealType = params.get('deal_type')
          const initialDealType: DealType =
            requestedDealType === 'today' ||
            requestedDealType === 'coupons' ||
            requestedDealType === 'all'
              ? requestedDealType
              : 'today'
          const initialCategory =
            params.get('category')?.trim() || null
          const requestedPage = Number.parseInt(
            params.get('page') ?? '1',
            10,
          )
          void openDeals({
            domain: initialDomain,
            type: initialDealType,
            category: initialCategory,
            page:
              Number.isFinite(requestedPage) && requestedPage >= 1
                ? Math.min(20, requestedPage)
                : 1,
            push: false,
          })
        } else {
          const initialQuery = params.get('q')?.trim()
          if (initialQuery) {
            setQuery(initialQuery)
            if (looksLikeAsin(initialQuery)) {
              void openProduct(initialQuery.toUpperCase(), initialDomain, false)
            } else {
              void beginSearch(
                {
                  query: initialQuery,
                  domain: initialDomain,
                  domains: initialDomains,
                  pages: 1,
                  page: 1,
                  no_ads: initialFilters.noAds,
                  delivery:
                    initialFilters.delivery || initialFilters.freeShipping,
                  enrich: initialFilters.enrich,
                  sort: initialFilters.sort,
                  delivery_country: deliveryCountryRef.current,
                  delivery_postal_code: deliveryPostalCodeRef.current,
                  history: {
                    filters: filtersToPersisted(initialFilters),
                  },
                },
                false,
              )
            }
          }

          const initialAsin = params.get('asin')
          if (initialAsin) {
            void openProduct(initialAsin, initialDomain, false)
          }
        }
        uiReadyRef.current = true
        setRealtimeReady(true)
      } catch (caught) {
        if (alive) {
          setSessionError(messageOf(caught, t('app.unexpectedError')))
        }
      } finally {
        if (alive) {
          setBooting(false)
        }
      }
    }

    void boot()
    return () => {
      alive = false
    }
  }, [applyCompareState, beginSearch, openDeals, openProduct])

  useEffect(() => {
    if (!jobId) {
      return
    }

    let alive = true
    let timer = 0
    const poll = async () => {
      try {
        const next = await getJob(jobId)
        if (!alive) {
          return
        }
        setJob(next)
        if (next.state === 'succeeded') {
          activeJobRef.current = null
          if (next.result) {
            const request = lastSearchRef.current
            if (request) {
              storeSearchPage(
                request,
                next.result.requested_pages[0] ?? 1,
                next.result,
              )
            }
          }
          setResults(next.result ?? null)
          setJobId(null)
          return
        }
        if (next.state === 'failed') {
          activeJobRef.current = null
          setError({
            code: next.error?.code ?? 'search_failed',
            message:
              next.error?.message ?? t('app.searchFailedFallback'),
          })
          setJobId(null)
          return
        }
        if (next.state === 'cancelled') {
          activeJobRef.current = null
          setJobId(null)
          return
        }
        timer = window.setTimeout(poll, 450)
      } catch (caught) {
        if (!alive) {
          return
        }
        activeJobRef.current = null
        if (caught instanceof ApiError && caught.status === 401) {
          setSessionError(caught.message)
          setJobId(null)
        } else {
          setError(noticeOf(caught, t('app.unexpectedError')))
          setJobId(null)
        }
      }
    }

    void poll()
    return () => {
      alive = false
      window.clearTimeout(timer)
    }
  }, [jobId])

  useEffect(() => {
    if (!dealJobId) {
      return
    }

    let alive = true
    let timer = 0
    const poll = async () => {
      try {
        const next = await getJob(dealJobId)
        if (!alive) {
          return
        }
        if (activeDealJobRef.current !== dealJobId) {
          setDealJobId(null)
          return
        }
        setDealJob(next)
        if (next.state === 'succeeded') {
          activeDealJobRef.current = null
          const jobRequest = dealJobRequestRef.current
          if (next.result && jobRequest?.jobId === dealJobId) {
            storeDealCache(jobRequest.request, next.result)
            dealJobRequestRef.current = null
          }
          setDealResults(next.result ?? null)
          setDealError(null)
          setDealJobId(null)
          return
        }
        if (next.state === 'failed') {
          activeDealJobRef.current = null
          if (dealJobRequestRef.current?.jobId === dealJobId) {
            dealJobRequestRef.current = null
          }
          setDealError({
            code: next.error?.code ?? 'deals_failed',
            message:
              next.error?.message ??
              t('app.dealsFailedFallback'),
          })
          setDealJobId(null)
          return
        }
        if (next.state === 'cancelled') {
          activeDealJobRef.current = null
          if (dealJobRequestRef.current?.jobId === dealJobId) {
            dealJobRequestRef.current = null
          }
          setDealJobId(null)
          return
        }
        timer = window.setTimeout(poll, 450)
      } catch (caught) {
        if (!alive) {
          return
        }
        activeDealJobRef.current = null
        if (dealJobRequestRef.current?.jobId === dealJobId) {
          dealJobRequestRef.current = null
        }
        if (caught instanceof ApiError && caught.status === 401) {
          setSessionError(caught.message)
          setDealJobId(null)
        } else {
          setDealError(noticeOf(caught, t('app.unexpectedError')))
          setDealJobId(null)
        }
      }
    }

    void poll()
    return () => {
      alive = false
      window.clearTimeout(timer)
    }
  }, [dealJobId, storeDealCache])

  useEffect(() => {
    const onPopState = () => {
      const params = new URLSearchParams(location.search)
      const asin = params.get('asin')
      if (asin) {
        productOriginRef.current =
          history.state?.fromDeals === true
            ? 'deals'
            : history.state?.fromSaved === true
              ? 'saved'
              : history.state?.fromHistory === true
                ? 'history'
                : history.state?.fromCompare === true
                  ? 'compare'
                  : 'search'
        void openProduct(asin, params.get('domain') ?? domainRef.current, false)
        return
      }

      if (params.get('view') === 'settings') {
        setView('settings')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        setMobileMenuOpen(false)
        void refreshUpdateStatus()
        void refreshHistory()
        return
      }

      if (params.get('view') === 'monitoring') {
        setView('monitoring')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        setMobileMenuOpen(false)
        return
      }

      if (params.get('view') === 'status') {
        setView('status')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        setMobileMenuOpen(false)
        void refreshUpdateStatus()
        return
      }

      if (params.get('view') === 'history') {
        setView('history')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        setMobileMenuOpen(false)
        void refreshHistory()
        return
      }

      if (params.get('view') === 'saved') {
        setView('saved')
        setSavedTab(params.get('tab') === 'products' ? 'products' : 'groups')
        setSelectedGroupId(params.get('group'))
        setSavedBadge(false)
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        return
      }

      if (params.get('view') === 'compare') {
        setView('compare')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        setCompareBadge(false)
        setMobileMenuOpen(false)
        void refreshCompare()
        return
      }

      if (params.get('deals') === '1') {
        setView('deals')
        setSelectedAsin(null)
        setProduct(null)
        setProductError(null)
        if (!dealResults && !dealJobId) {
          const requestedDealType = params.get('deal_type')
          const type: DealType =
            requestedDealType === 'today' ||
            requestedDealType === 'coupons' ||
            requestedDealType === 'all'
              ? requestedDealType
              : 'today'
          const page = Number.parseInt(params.get('page') ?? '1', 10)
          void openDeals({
            domain: params.get('domain') ?? dealDomainRef.current,
            type,
            category: params.get('category')?.trim() || null,
            page:
              Number.isFinite(page) && page >= 1
                ? Math.min(20, page)
                : 1,
            push: false,
          })
        }
        return
      }

      void cancelDealJob()
      dealOptionsAbortRef.current?.abort()
      dealSessionRef.current += 1
      dealRunRef.current += 1
      productOriginRef.current = 'search'
      setView('search')
      setDealStarting(false)
      setSelectedAsin(null)
      setProduct(null)
      setProductError(null)
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [cancelDealJob, dealJobId, dealResults, openDeals, openProduct, refreshCompare])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      const typing =
        target?.tagName === 'INPUT' ||
        target?.tagName === 'TEXTAREA' ||
        target?.tagName === 'SELECT' ||
        target?.isContentEditable
      if (event.key === '/' && !typing) {
        event.preventDefault()
        searchRef.current?.focus()
      }
      if (event.key === 'Escape') {
        if (mobileMenuOpen) {
          setMobileMenuOpen(false)
        } else if (closeConfirm) {
          setCloseConfirm(false)
        } else if (view === 'detail') {
          goBack()
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [closeConfirm, mobileMenuOpen, view, goBack])

  useEffect(() => {
    if (!mobileMenuOpen) {
      return
    }

    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previousOverflow
    }
  }, [mobileMenuOpen])

  useEffect(() => {
    const updateScroll = () => {
      scrollPositionsRef.current[viewRef.current] = window.scrollY
    }
    updateScroll()
    window.addEventListener('scroll', updateScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', updateScroll)
    }
  }, [])

  useLayoutEffect(() => {
    viewRef.current = view
    window.scrollTo(
      0,
      view === 'detail' ? 0 : scrollPositionsRef.current[view],
    )
  }, [view, selectedAsin])

  useEffect(() => {
    if (view === 'detail' && product) {
      document.title = `${product.product.title} · AMZ CLI`
      return
    }
    if (view === 'settings') {
      document.title = `${t('nav.settings')} · AMZ CLI`
      return
    }
    if (view === 'compare') {
      document.title = `${t('nav.comparisons')} · AMZ CLI`
      return
    }
    if (view === 'saved') {
      const group = library.groups.find((item) => item.id === selectedGroupId)
      document.title = group
        ? `${group.name} · ${t('saved.title')} · AMZ CLI`
        : `${t('saved.title')} · AMZ CLI`
      return
    }
    if (view === 'deals') {
      const category = dealCategories.find(
        (item) => item.node_id === dealCategory,
      )?.label
      document.title = category
        ? `${category} · ${t('nav.deals')} · AMZ CLI`
        : t('app.dealsTitle')
      return
    }
    if (results) {
      document.title = `${results.query} · AMZ CLI`
      return
    }
    document.title = `AMZ CLI · ${t('app.priceObservatory')}`
  }, [
    dealCategories,
    dealCategory,
    library.groups,
    product,
    results,
    selectedGroupId,
    t,
    view,
  ])

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    runSearch(query)
  }

  const runExample = (example: string) => {
    setQuery(example)
    runSearch(example)
  }

  const openRecentSearch = (item: RecentSearch) => {
    const targetDomains =
      item.domains && item.domains.length > 0 ? item.domains : [item.domain]
    updateDomains(targetDomains)
    setQuery(item.query)
    setDomain(item.domain)
    runSearch(item.query, item.domain, targetDomains)
  }

  const removeRecentSearch = (target: RecentSearch) => {
    setRecentSearches((current) =>
      current.filter(
        (item) =>
          item.domain !== target.domain || item.query !== target.query,
      ),
    )
  }

  const clearRecentSearches = () => {
    setRecentSearches([])
  }

  const cancelSearch = () => {
    const current = activeJobRef.current
    activeJobRef.current = null
    setJobId(null)
    setJob(null)
    if (current) {
      void cancelJob(current).catch(() => {
        // The job may have completed between the click and the request.
      })
    }
  }

  const retrySearch = () => {
    const last = lastSearchRef.current
    if (last) {
      void beginSearch(last)
    }
  }

  const confirmClose = async () => {
    setCloseError(null)
    try {
      await shutdown()
      setClosed(true)
    } catch (caught) {
      setCloseError(messageOf(caught, t('app.unexpectedError')))
    }
  }

  if (booting) {
    return (
      <div className="boot-screen">
        <span className="brand-mark">A</span>
        <p>{t('app.opening')}</p>
      </div>
    )
  }

  if (sessionError) {
    return (
      <div className="boot-screen boot-error">
        <span className="brand-mark">A</span>
        <h1>{t('app.interfaceNotConnected')}</h1>
        <p>{sessionError}</p>
        <p className="boot-hint">
          {t('app.startSessionPrefix')} <code>amz-cli web --open</code>{' '}
          {t('app.startSessionSuffix')}
        </p>
        <button
          className="primary-button"
          type="button"
          onClick={() => location.reload()}
        >
          {t('app.tryAgain')}
        </button>
      </div>
    )
  }

  if (closed) {
    return (
      <div className="boot-screen">
        <span className="brand-mark">A</span>
        <h1>{t('app.interfaceClosed')}</h1>
        <p>{t('app.interfaceClosedHint')}</p>
      </div>
    )
  }

  if (!meta) {
    return null
  }

  const mobileBarTitle =
    view === 'detail'
      ? t('app.productDetail')
      : view === 'saved'
        ? t('saved.title')
        : view === 'history'
          ? t('history.title')
          : view === 'monitoring'
            ? t('monitoring.title')
            : view === 'status'
              ? t('status.title')
              : view === 'settings'
                ? t('nav.settings')
                : view === 'compare'
                  ? t('nav.comparisons')
                  : view === 'deals'
                    ? t('nav.deals')
                    : t('nav.search')

  return (
    <div
      className={`app-shell${railPinned ? ' rail-pinned' : ''}${mobileMenuOpen ? ' rail-menu-open' : ''}`}
    >
      {updateOverlay ? (
        <div className="update-overlay" role="status" aria-live="polite">
          <div className="update-overlay-card">
            <span className="update-overlay-icon">
              <Icon name="refresh" />
            </span>
            <p className="update-overlay-eyebrow">AMZ CLI</p>
            <h2>
              {updateStatus?.job.phase === 'scheduled'
                ? t('status.overlayRestartingTitle')
                : t('status.overlayUpdatingTitle')}
            </h2>
            <p>
              {updateStatus?.job.phase === 'scheduled'
                ? t('status.overlayRestartingBody')
                : t('status.overlayUpdatingBody')}
            </p>
            <div
              className="update-overlay-track"
              role="progressbar"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={updateStatus?.job.progress?.percent ?? undefined}
            >
              <span
                className={
                  updateStatus?.job.progress?.percent == null
                    ? 'is-indeterminate'
                    : ''
                }
                style={
                  updateStatus?.job.progress?.percent != null
                    ? { width: `${updateStatus.job.progress.percent}%` }
                    : undefined
                }
              />
            </div>
            <p className="update-overlay-detail">
              {updateStageLabel(updateStatus?.job.progress?.stage, t)
                ? `${updateStageLabel(updateStatus?.job.progress?.stage, t)}${
                    updateStatus?.job.progress?.percent != null
                      ? ` · ${updateStatus.job.progress.percent}%`
                      : ''
                  }`
                : updateStatus?.job.progress?.message ??
                  updateStatus?.job.message ??
                  t('status.preparing')}
            </p>
            {updateOverlayStuck ? (
              <p className="update-overlay-timeout">
                {t('status.overlayStuck')}
              </p>
            ) : null}
          </div>
        </div>
      ) : null}

      <Sidebar
        meta={meta}
        theme={theme}
        languages={availableLanguages}
        pinned={railPinned}
        mobileOpen={mobileMenuOpen}
        view={
          view === 'detail'
            ? productOriginRef.current === 'deals'
              ? 'deals'
              : productOriginRef.current === 'saved'
                ? 'saved'
                : productOriginRef.current === 'history'
                  ? 'history'
                  : productOriginRef.current === 'compare'
                    ? 'compare'
                    : 'search'
            : view
        }
        savedBadge={savedBadge}
        compareBadge={compareBadge}
        updateBadge={updateBadge}
        onTogglePin={() => setRailPinned((current) => !current)}
        onToggleTheme={() =>
          setThemePreference(theme === 'dark' ? 'light' : 'dark')
        }
        onRequestClose={() => {
          setCloseError(null)
          setCloseConfirm(true)
        }}
        onHome={openSearchView}
        onOpenDeals={() => void openDeals()}
        onOpenCompare={openCompareView}
        onOpenSaved={() => openSavedView(savedTab)}
        onOpenHistory={openHistoryView}
        onOpenMonitoring={openMonitoringView}
        onOpenStatus={openStatusView}
        onOpenSettings={openSettingsView}
        onMobileClose={() => setMobileMenuOpen(false)}
      />

      <div className="app-workspace" ref={workspaceRef}>
        <header className="mobile-bar">
          <button
            className="icon-button mobile-menu-button"
            type="button"
            onClick={() => setMobileMenuOpen(true)}
            aria-label={t('app.openMenu')}
            aria-expanded={mobileMenuOpen}
          >
            <Icon name="panel-left" />
          </button>
          <span className="mobile-bar-title">{mobileBarTitle}</span>
        </header>

        <main className="workspace-main">
          {view === 'settings' ? (
            <SettingsView
              meta={meta}
              themePreference={themePreference}
              onThemePreferenceChange={setThemePreference}
              language={language}
              languages={availableLanguages}
              onLanguageChange={setLanguage}
              railPinned={railPinned}
              onRailPinnedChange={setRailPinned}
              resultsLayout={resultsLayout}
              onResultsLayoutChange={setResultsLayout}
              monitoringLayout={monitoringLayout}
              onMonitoringLayoutChange={setMonitoringLayout}
              marketplaces={marketplaces}
              domains={domains}
              onDomainsChange={updateDomains}
              onDomainToggle={toggleDomain}
              dealDomain={dealDomain}
              onDealDomainChange={(nextDomain) => {
                setDealDomain(nextDomain)
                dealDomainRef.current = nextDomain
              }}
              filters={filters}
              onFiltersChange={updateFilters}
              deliveryCountry={deliveryCountry}
              deliveryPostalCode={deliveryPostalCode}
              onDeliveryLocationChange={updateDeliveryLocation}
              library={library}
              historyCount={historyEntries.length}
              updateStatus={updateStatus}
              updateChecking={updateChecking}
              onCheckUpdates={() => void checkUpdates()}
              onOpenStatus={openStatusView}
              onOpenMonitoring={openMonitoringView}
              onNetworkSettingsChange={clearNetworkCaches}
            />
          ) : view === 'monitoring' ? (
            <MonitoringView
              library={library}
              revision={monitorRevision}
              layout={monitoringLayout}
              onLayoutChange={setMonitoringLayout}
            />
          ) : view === 'status' && meta ? (
            <StatusView
              meta={meta}
              status={updateStatus}
              loading={updateLoading}
              checking={updateChecking}
              starting={updateStarting}
              error={updateError}
              onCheck={() => void checkUpdates()}
              onApply={() => void applyUpdateNow()}
            />
          ) : view === 'history' ? (
            <HistoryView
              entries={historyEntries}
              loading={historyLoading}
              error={historyError}
              filter={historyFilter}
              layout={resultsLayout}
              onFilterChange={setHistoryFilter}
              onLayoutChange={setResultsLayout}
              onOpenSearch={(entry) => void openHistorySearch(entry)}
              onOpenProduct={openHistoryProduct}
              onDelete={(entry) => void removeHistoryEntry(entry)}
              onClear={() => void clearHistoryEntries()}
            />
          ) : view === 'saved' ? (
            <SavedProductsView
              library={library}
              layout={resultsLayout}
              tab={savedTab}
              activeGroupId={selectedGroupId}
              error={savedError}
              savedBadge={savedBadge}
              onTabChange={changeSavedTab}
              onLayoutChange={setResultsLayout}
              onOpenProduct={(asin, productDomain) => {
                productOriginRef.current = 'saved'
                void openProduct(asin, productDomain)
              }}
              onOpenGroup={openSavedGroup}
              onCloseGroup={closeSavedGroup}
              onCreateGroup={createLibraryGroup}
              onRenameGroup={renameLibraryGroup}
              onDeleteGroup={deleteLibraryGroup}
              onAddProductsToGroup={addProductsToGroup}
              onRemoveGroupProduct={removeGroupProduct}
              onRemoveProduct={removeSavedProduct}
              onRefreshSavedProduct={refreshSavedProduct}
              groupRefreshCooldownMinutes={
                meta?.library?.group_refresh_cooldown_minutes ?? 24 * 60
              }
              groupRefreshDelaySeconds={
                meta?.library?.group_refresh_delay_seconds ?? 6
              }
              autoRefreshAfterMinutes={
                meta?.library?.auto_refresh_after_minutes ?? 7 * 24 * 60
              }
            />
          ) : view === 'compare' ? (
            <CompareView
              compare={compare}
              loading={compareLoading}
              error={compareError ?? compareLoadError}
              deliveryCountry={deliveryCountry}
              deliveryPostalCode={deliveryPostalCode}
              onCloseItem={removeCompareProduct}
              onClear={() => void clearCompareView()}
              onOpenSearch={openSearchView}
              onOpenProduct={(asin, productDomain) => {
                productOriginRef.current = 'compare'
                void openProduct(asin, productDomain)
              }}
            />
          ) : view === 'search' ? (
            <SearchView
              form={{
                query,
                onQueryChange: setQuery,
                domain,
                domains,
                onDomainsChange: updateDomains,
                onDomainToggle: toggleDomain,
                filters,
                onFiltersChange: updateFilters,
                marketplaces,
                deliveryCountry,
                deliveryPostalCode,
                onDeliveryLocationChange: updateDeliveryLocation,
                running: jobId !== null,
                onSubmit: submitSearch,
                onSearch: runSearch,
                onOpenDeals: () =>
                  void openDeals({ type: 'today', domain }),
                searchRef,
              }}
              results={results}
              lastSearch={lastSearchRef.current}
              job={job}
              error={error}
              layout={resultsLayout}
              onLayoutChange={setResultsLayout}
              onPageChange={goToSearchPage}
              recentSearches={recentSearches}
              onCancel={cancelSearch}
              onOpenProduct={(asin, marketplaceId) => {
                productOriginRef.current = 'search'
                void openProduct(
                  asin,
                  marketplaceId ?? results?.marketplace_id ?? domain,
                )
              }}
              onRetry={retrySearch}
              cacheNotice={cachedSearchNotice}
              onRefreshCache={() => {
                setCachedSearchNotice(null)
                retrySearch()
              }}
              onExample={runExample}
              onRecentSearch={openRecentSearch}
              onRemoveRecentSearch={removeRecentSearch}
              onClearRecentSearches={clearRecentSearches}
              isSaved={(item) =>
                isProductSaved(
                  library,
                  item.marketplace_id ?? domain,
                  item.asin,
                )
              }
              onToggleSave={toggleSavedItem}
            />
          ) : view === 'deals' ? (
            <DealsView
              domain={dealDomain}
              marketplaces={marketplaces}
              dealType={dealType}
              dealTypes={dealTypes}
              categories={dealCategories}
              category={dealCategory}
              categoriesLoading={dealOptionsLoading}
              categoriesError={dealOptionsError}
              results={dealResults}
              job={dealJob}
              running={
                dealJobId !== null ||
                dealOptionsLoading ||
                dealStarting
              }
              error={dealError}
              layout={resultsLayout}
              onDomainChange={changeDealDomain}
              onTypeChange={changeDealType}
              onCategoryChange={changeDealCategory}
              onPageChange={changeDealPage}
              onRefresh={refreshDeals}
              onLayoutChange={setResultsLayout}
              onOpenProduct={(asin, marketplaceId) => {
                productOriginRef.current = 'deals'
                void openProduct(
                  asin,
                  marketplaceId ?? dealDomain,
                )
              }}
              onOpenSearch={openSearchView}
              onRetry={refreshDeals}
              isSaved={(item) =>
                isProductSaved(
                  library,
                  item.marketplace_id ?? dealDomain,
                  item.asin,
                )
              }
              onToggleSave={toggleSavedItem}
            />
          ) : (
            <ProductView
              product={product}
              loading={productLoading}
              error={productError}
              refreshing={productRefreshing}
              refreshError={productRefreshError}
              saved={
                product
                  ? isProductSaved(
                      library,
                      product.marketplace.id,
                      product.product.asin,
                    )
                  : false
              }
              onToggleSave={toggleSavedDetail}
              compared={
                product
                  ? compare.items.some(
                      (item) =>
                        item.domain === product.marketplace.id &&
                        item.asin === product.product.asin,
                    )
                  : false
              }
              onToggleCompare={() => {
                if (product) {
                  void toggleCompareProduct(
                    product.marketplace.id,
                    product.product.asin,
                  )
                }
              }}
              compareError={compareError}
              backLabel={
                productOriginRef.current === 'saved'
                  ? t('product.backToSaved')
                  : productOriginRef.current === 'history'
                    ? t('product.backToHistory')
                    : productOriginRef.current === 'compare'
                      ? t('compare.backToCompare')
                      : t('product.backToResults')
              }
              onRefresh={() => void refreshCurrentProduct()}
              onBack={goBack}
              onSelectVariant={(asin) =>
                void openProduct(
                  asin,
                  product?.marketplace.id ?? domain,
                  true,
                  true,
                )
              }
              onRetry={() => {
                if (selectedAsin) {
                  void openProduct(
                    selectedAsin,
                    product?.marketplace.id ?? domain,
                    false,
                  )
                }
              }}
            />
          )}
        </main>
      </div>

      {closeConfirm ? (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              setCloseConfirm(false)
            }
          }}
        >
          <div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="close-title"
          >
            <h2 id="close-title">{t('app.closeTitle')}</h2>
            <p>
              {t('app.closeBodyPrefix')} <code>amz-cli web --open</code>
              {t('app.closeBodySuffix')}
            </p>
            {closeError ? (
              <p className="modal-error">{closeError}</p>
            ) : null}
            <div className="modal-actions">
              <button
                className="text-button"
                type="button"
                onClick={() => setCloseConfirm(false)}
              >
                {t('app.cancel')}
              </button>
              <button
                className="primary-button"
                type="button"
                onClick={() => void confirmClose()}
              >
                {t('app.closeInterface')}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}
