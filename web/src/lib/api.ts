import type { LanguageCode } from '../i18n/types'
import type { RecentSearch } from './recent'
import { amazonSort } from './searchFilters'
import type { PersistedSearchFilters, SearchSort } from './searchFilters'

export type ThemePreference = 'system' | 'light' | 'dark'

export type HttpTransport = 'auto' | 'curl' | 'http'

export interface HttpSettings {
  schema?: number
  transport: HttpTransport
  browser: string
  user_agent: string
  browser_headers: boolean
  timeout_seconds: number
  fallback_to_curl: boolean
  updated_at?: string
}

export const defaultHttpSettings: HttpSettings = {
  transport: 'auto',
  browser: 'auto',
  user_agent: '',
  browser_headers: false,
  timeout_seconds: 30,
  fallback_to_curl: true,
}

export interface HttpCurlDiagnostics {
  available: boolean
  version: string
  runtime: string
  mode: string
  transport: string
  error: string
}

export interface HttpCooldownDiagnostics {
  domain: string
  until: string
  reason: string
}

export interface EffectiveHttpDiagnostics {
  domain: string
  transport: 'curl' | 'http'
  browser: string
  browser_deals: string
  user_agent: string | null
  browser_headers: boolean
  timeout_seconds: number
}

export interface HttpSettingsDiagnostics {
  settings: HttpSettings
  curl: HttpCurlDiagnostics
  profiles: string[]
  cooldowns: HttpCooldownDiagnostics[]
  effective: EffectiveHttpDiagnostics[]
}

export interface CompareItem {
  id: string
  domain: string
  asin: string
  source: string
  added_at: string
}

export interface CompareState {
  schema: number
  revision: number
  updated_at: string
  items: CompareItem[]
}

export const emptyCompareState: CompareState = {
  schema: 1,
  revision: 0,
  updated_at: '',
  items: [],
}

export interface UiPreferences {
  theme: ThemePreference
  language: LanguageCode
  delivery_country: string
  delivery_postal_code: string
  domain: string
  domains: string[]
  deals_domain: string
  rail_pinned: boolean
  results_layout: 'list' | 'grid'
  monitoring_layout: 'list' | 'grid'
  filters: PersistedSearchFilters
}

export interface UiState {
  schema: number
  updated_at: string
  preferences: UiPreferences
  recent_searches: RecentSearch[]
  saved_searches: unknown[]
  search_history: unknown[]
  comparisons: unknown[]
  monitoring: Record<string, unknown>
  settings: Record<string, unknown>
}

export type DealType = 'all' | 'today' | 'coupons'

export interface DealCategory {
  label: string
  node_id: string
}

export interface DealOptions {
  domain: string
  marketplace_name: string
  currency: string
  country_code: string
  deal_type: DealType
  deal_types: DealType[]
  categories: DealCategory[]
  warnings: string[]
}

export interface DealInput {
  domain: string
  deal_type: DealType
  category?: string | null
  page: number
  no_ads: boolean
  delivery: boolean
  enrich: boolean
  delivery_country: string
  delivery_postal_code: string
}

export type MonitorKind = 'product' | 'group'

export interface MonitorTarget {
  kind: MonitorKind
  id?: string | null
  title?: string | null
  description?: string | null
  image?: string | null
  asin?: string | null
  domain?: string | null
  marketplace?: string | null
  url?: string | null
  domains?: string[]
  currency?: string | null
  price_last?: number | null
  products: number
  available: boolean
  error?: string | null
}

export interface MonitorRun {
  binding_id?: string | null
  status: string
  pid: number
  started_at?: string | null
  last_seen_at?: string | null
  mode?: string | null
  startup?: string | null
  startup_enabled: boolean
  startup_registered?: boolean | null
  startup_name?: string | null
  startup_id?: string | null
  startup_config_path?: string | null
  startup_command_preview?: string | null
  targets: number
  timer_seconds: number
  schedule_mode?: string | null
  runtime_hash?: string | null
  window_seconds?: number | null
  max_requests?: number | null
  min_gap_seconds?: number | null
  max_gap_seconds?: number | null
  log_path?: string | null
  pid_file?: string | null
  save_path?: string | null
  save_file?: string | null
  data_files: string[]
}

export interface MonitorRuntime {
  timer_seconds: number
  request_delay_seconds: number
  jitter_seconds: number
  save_path: string
  save_file: string
  save_format: string
  layout: string
  notify: boolean
  notify_on: string[]
  notify_cooldown_seconds?: number | null
  no_auto_update: boolean
}

export interface MonitorStats {
  first_price?: number | null
  min_price?: number | null
  max_price?: number | null
  last_price?: number | null
  last_total?: number | null
  min_total?: number | null
  max_total?: number | null
  currency?: string | null
  observations: number
  alerted_rules: number
  last_seen_at?: string | null
}

export interface MonitorBinding {
  id: string
  kind: MonitorKind
  name?: string | null
  product_id?: string | null
  group_id?: string | null
  max_price?: number | null
  drop_percent?: number | null
  rise_percent?: number | null
  baseline?: string | null
  baseline_price?: number | null
  cooldown_seconds?: number | null
  total_max?: number | null
  allow_incomplete?: boolean | null
  currency?: string | null
  fx: Record<string, number>
  item_rules: Record<string, MonitorItemRule>
  created_at: string
  updated_at: string
  target: MonitorTarget
  runtime: MonitorRuntime
  startup: MonitorStartup
  run?: MonitorRun | null
  stats: MonitorStats
  status: string
  can_start: boolean
  can_stop: boolean
  restart_required: boolean
  error?: string | null
}

export interface MonitorExternalRun {
  run: MonitorRun
  title?: string | null
}

export interface MonitorStartup {
  available: boolean
  backend?: string | null
  error?: string | null
  configured: boolean
  enabled: boolean
  registered: boolean
  managed: boolean
  id?: string | null
  name?: string | null
  command_preview?: string | null
  message?: string | null
}

export interface MonitorNotificationTestResult {
  success: boolean
  backend: string
  detail?: string | null
  error?: string | null
}

export interface MonitorOverview {
  bindings: number
  running: number
  alerts: number
  startup_available: boolean
  startup_backend?: string | null
  startup_error?: string | null
  monitors: MonitorBinding[]
  other_runs: MonitorExternalRun[]
}

export interface MonitorItemRule {
  max_price?: number | null
  drop_percent?: number | null
  rise_percent?: number | null
  baseline?: string | null
  baseline_price?: number | null
  cooldown_seconds?: number | null
}

export interface MonitorRuntimeInput {
  timer_seconds?: number | null
  request_delay_seconds?: number | null
  jitter_seconds?: number | null
  save_path?: string | null
  save_file?: string | null
  save_format?: string | null
  layout?: string | null
  notify?: boolean | null
  notify_on?: string[]
  notify_cooldown_seconds?: number | null
  no_auto_update?: boolean | null
}

export interface MonitorBindingInput {
  kind: MonitorKind
  product_id?: string | null
  group_id?: string | null
  name?: string | null
  max_price?: number | null
  drop_percent?: number | null
  rise_percent?: number | null
  baseline?: string | null
  baseline_price?: number | null
  cooldown_seconds?: number | null
  total_max?: number | null
  allow_incomplete?: boolean | null
  currency?: string | null
  fx?: Record<string, number>
  item_rules?: Record<string, MonitorItemRule>
  runtime?: MonitorRuntimeInput | null
  start?: boolean
}

export interface MonitorSeriesPoint {
  observed_at: string
  price: number
  min?: number | null
  max?: number | null
  complete: boolean
  count: number
}

export interface MonitorSeriesAlert {
  at: string
  type: string
  message: string
  observed?: number | null
  currency?: string | null
}

export interface MonitorThreshold {
  label: string
  value: number
  currency?: string | null
}

export interface MonitorSeriesResponse {
  binding_id: string
  kind: MonitorKind
  title?: string | null
  currency?: string | null
  from?: string | null
  to?: string | null
  points: MonitorSeriesPoint[]
  alerts: MonitorSeriesAlert[]
  thresholds: MonitorThreshold[]
  observations: number
  truncated: boolean
  warnings: string[]
  generated_at: string
}

export interface MonitorStartInput {
  timer_seconds?: number | null
  request_delay_seconds?: number | null
  notify?: boolean | null
}

export interface Meta {
  version: string
  channel: string
  released_at?: string
  released_at_iso?: string
  platform: string
  install_dir: string
  source: string
  web: {
    build: string
    development: boolean
    port: number
    started_at: string
    uptime_seconds: number
  }
  library?: {
    auto_refresh_after_minutes: number
    group_refresh_cooldown_minutes: number
    group_refresh_delay_seconds: number
  }
}

export interface LibrarySnapshot {
  title: string
  image: string
  price: string
  price_amount?: number | null
  currency: string
  original_price?: string | null
  discount?: string | null
  rating?: string | null
  reviews?: string | null
  delivery_text?: string | null
  availability?: string | null
  url?: string | null
  charger?: ChargerInfo | null
}

export interface LibraryProduct {
  id: string
  asin: string
  domain: string
  saved_at: string
  updated_at: string
  last_checked_at?: string | null
  price_at_save?: number | null
  price_last?: number | null
  currency: string
  snapshot: LibrarySnapshot
}

export interface LibraryGroupItem {
  product_id: string
  quantity: number
}

export type GroupAccent =
  | 'accent'
  | 'blue'
  | 'gold'
  | 'rust'
  | 'green'
  | 'teal'
  | 'violet'
  | 'rose'
  | 'slate'
  | 'orange'
  | 'plum'
  | 'sky'
  | `#${string}`

export interface LibraryGroup {
  id: string
  name: string
  accent: GroupAccent
  description?: string | null
  created_at: string
  updated_at: string
  items: LibraryGroupItem[]
}

export interface Library {
  products: LibraryProduct[]
  groups: LibraryGroup[]
}

export interface LibraryEnvelope extends Library {
  schema: number
  revision: number
  updated_at: string
}

export type HistoryKind = 'search' | 'product'
export type HistorySource = 'web' | 'cli' | 'agent'
export type HistoryStatus =
  | 'running'
  | 'succeeded'
  | 'failed'
  | 'cancelled'
  | 'interrupted'

export interface HistorySearchFilters {
  no_ads: boolean
  delivery: boolean
  enrich: boolean
  min_rating: number
  price_min?: number | null
  price_max?: number | null
  only_deals: boolean
  free_shipping: boolean
  amazon_choice: boolean
  sort: SearchSort
}

export interface HistoryProductSnapshot {
  title: string
  image: string
  price: string
  price_amount?: number | null
  currency: string
  original_price: string
  discount: string
  rating: string
  reviews: string
  availability: string
  url: string
}

export interface HistoryEntry {
  id: string
  kind: HistoryKind
  source: HistorySource
  status: HistoryStatus
  at: string
  started_at?: string
  finished_at?: string
  query?: string
  domain?: string
  domains?: string[]
  filters?: HistorySearchFilters
  result_count?: number
  error?: string
  asin?: string
  snapshot?: HistoryProductSnapshot
  cached?: boolean
}

export interface HistoryResponse {
  schema: number
  revision: number
  updated_at: string
  entries: HistoryEntry[]
}

export interface HistoryCacheResponse<T = SearchResultPayload> {
  cached_at: string
  result: T
}

export type UpdateCheckStatus =
  | 'update_available'
  | 'up_to_date'
  | 'manual_required'
  | 'no_release'

export interface UpdateCheck {
  status: UpdateCheckStatus
  can_apply: boolean
  current_version: string
  latest_version?: string | null
  platform: string
  channel: string
  published_at?: string | null
  min_supported_version?: string | null
  download_size: number
  file_count: number
  notes?: string | null
  notes_markdown?: string | null
  message: string
}

export interface UpdateProgress {
  stage: string
  message: string
  percent?: number | null
  bytes: number
  total_bytes: number
}

export interface UpdateJob {
  revision: number
  phase: string
  message: string
  progress?: UpdateProgress | null
  error?: string | null
  updated_at: string
}

export interface UpdateJournal {
  schema: number
  phase: string
  from_version?: string | null
  to_version?: string | null
  platform?: string | null
  channel?: string | null
  install_dir?: string | null
  backup_path?: string | null
  notes?: string | null
  helper_process_id?: number | null
  monitors_planned: string[]
  monitors_stopped: string[]
  monitors_restarted: string[]
  restart_errors: string[]
  deferred_reason?: string | null
  error?: string | null
  web_pid?: number | null
  web_restart_error?: string | null
  web_restart_attempted_at?: string | null
  updated_at: string
}

export interface UpdateBackup {
  version: string
  platform: string
  created_at: string
  size: number
}

export interface AutoUpdateConfig {
  enabled: boolean
  channel: string
  interval_seconds: number
  auto_apply: boolean
  restart_monitors: boolean
  defer_foreground_monitor: boolean
  notify: boolean
}

export interface AutoUpdateState {
  last_check?: string | null
  latest_version?: string | null
  last_status?: string | null
  last_trigger?: string | null
}

export interface CurlRuntimeFile {
  name: string
  path: string
  exists: boolean
  size: number
}

export interface CurlStatus {
  available: boolean
  mode: string
  version: string
  path: string
  runtime_id: string
  error: string
  files: CurlRuntimeFile[]
}

export interface UpdateStatus {
  current_version: string
  platform: string
  channel: string
  install_dir: string
  source: string
  check?: UpdateCheck | null
  job: UpdateJob
  journal?: UpdateJournal | null
  backups: UpdateBackup[]
  auto_update?: AutoUpdateConfig | null
  auto_state?: AutoUpdateState | null
  curl: CurlStatus
}

export interface Marketplace {
  id: string
  display_name: string
  host: string
  language: LanguageCode
  currency: string
  country: string
  country_only_delivery: boolean
}

export interface MarketplaceResponse {
  default_id: string
  marketplaces: Marketplace[]
}

export interface SuggestionsResponse {
  query: string
  marketplace_id: string
  suggestions: string[]
  unavailable?: boolean
}

export interface SearchItem {
  asin: string
  title: string
  price: string
  price_amount?: number | null
  currency?: string
  rating: string
  reviews: string
  image: string
  coupon: string
  sponsored: boolean
  delivery_text?: string | null
  free_delivery?: boolean | null
  delivery_country?: string | null
  marketplace_id?: string | null
  marketplace_name?: string | null
  deal?: string | null
  deal_id?: string | null
  original_price?: string | null
  discount?: string | null
  amazon_choice?: boolean | null
  amazon_choice_text?: string | null
  amazon_choice_for?: string | null
  condition?: string | null
}

export interface SearchMarketplaceSummary {
  id: string
  name: string
  currency: string
  result_count: number
  total_available_pages: number
  completed_pages: number[]
  warnings: string[]
}

export interface SearchResultPayload {
  query: string
  marketplace_id: string
  marketplace_name: string
  currency: string
  duplicate_count: number
  elapsed_ms: number
  total_available_pages: number
  requested_pages: number[]
  completed_pages: number[]
  warnings: string[]
  marketplaces?: SearchMarketplaceSummary[]
  results: SearchItem[]
}

export interface SearchProgress {
  stage: string
  completed_pages: number
  total_pages: number
  message: string
}

export interface SearchJob {
  job_id: string
  state: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled'
  progress?: SearchProgress
  result?: SearchResultPayload
  error?: { code: string; message: string }
  created_at: string
  started_at?: string
  completed_at?: string
}

export interface SearchInput {
  query: string
  domain: string
  domains: string[]
  pages: number
  page?: number
  no_ads: boolean
  delivery: boolean
  enrich: boolean
  sort: SearchSort
  delivery_country: string
  delivery_postal_code: string
  history?: {
    filters: PersistedSearchFilters
    entry_id?: string
  }
}

export interface DeliveryInfo {
  location?: string
  country?: string
  postal_code?: string
  type?: string
  cost?: string
  time?: string
  cutoff?: string
  options?: Array<{
    type?: string
    cost?: string
    time?: string
    cutoff?: string
  }>
}

export interface RatingDetails {
  average?: number
  count?: number
  histogram: Record<string, number>
}

export interface ProductReview {
  id: string
  author: string
  rating: string
  rating_value?: number
  title: string
  date: string
  source: string
  verified_purchase: boolean
  helpful: string
  variant: string
  body: string
  url: string
  images?: string[] | null
}

export interface TopReviews {
  count: number
  local_count: number
  international_count: number
  items: ProductReview[]
}

export interface BuyingOption {
  offer_listing_id: string
  seller: string
  seller_id: string
  price: string
  shipping: string
  condition: string
  condition_type?: string | null
  condition_grade?: string | null
  delivery: string
  detail_url: string
  is_featured?: boolean
}

export interface BuyingOptionsSummary {
  count: number
  lowest_price: string
  condition?: string
  options: BuyingOption[]
}

export interface ProductVariant {
  asin: string
  selected: boolean
  attributes: Record<string, string>
  url: string
  price?: string | null
  original_price?: string | null
  discount?: string | null
  availability?: string | null
}

export interface ProductVariants {
  parent_asin: string
  selected_asin: string
  dimensions: Record<string, string>
  items: ProductVariant[]
}

export interface ProductDetailExtra {
  name: string
  value: string
}

export interface PrimePricing {
  price: string
  original_price: string
  discount: string
  only: boolean
}

export interface ChargerInfo {
  included?: boolean | null
  label?: string | null
  note?: string | null
  box_contents?: string | null
}

export interface ProductDetail {
  asin: string
  title: string
  subtitle: string
  model_name?: string | null
  best_seller_rank?: number | null
  best_seller_category?: string | null
  best_seller_label?: string | null
  best_seller_url?: string | null
  brand: string
  price: string
  original_price: string
  discount: string
  prime?: PrimePricing | null
  deal?: string | null
  deal_id?: string | null
  deal_ends_text?: string | null
  deal_ends_on?: string | null
  deal_ends_in_days?: number | null
  deal_has_countdown?: boolean | null
  rating: string
  reviews: string
  seller: string
  shipped_by: string
  returns: string
  availability: string
  coupon: string
  image: string
  frequently_returned: string
  usually_kept?: string | null
  images: string[]
  description: string[]
  specs: Record<string, string>
  amazon_details?: ProductDetailExtra[] | null
  charger?: ChargerInfo | null
  safety_warnings: string[]
  url: string
  delivery?: DeliveryInfo | null
  variants?: ProductVariants | null
  buying_options?: BuyingOptionsSummary | null
  rating_details?: RatingDetails | null
  top_reviews?: TopReviews | null
  amazon_choice?: boolean | null
  amazon_choice_text?: string | null
  amazon_choice_for?: string | null
}

export interface ProductResponse {
  marketplace: {
    id: string
    display_name: string
    currency: string
    country: string
  }
  product: ProductDetail
  price_amount?: number | null
  prime_price_amount?: number | null
  price_is_from?: boolean
  currency: string
  warnings?: string[]
  cached?: boolean
  stale?: boolean
  cache_age_seconds?: number
  fetched_at: string
}

export function getMonitors(): Promise<MonitorOverview> {
  return request<MonitorOverview>('/monitors')
}

export function getMonitor(bindingId: string): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}`,
  )
}

export function createMonitor(
  input: MonitorBindingInput,
): Promise<MonitorBinding> {
  return request<MonitorBinding>('/monitors', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateMonitor(
  bindingId: string,
  input: MonitorBindingInput,
): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(input),
    },
  )
}

export function deleteMonitor(bindingId: string): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>(
    `/monitors/${encodeURIComponent(bindingId)}`,
    { method: 'DELETE' },
  )
}

export function startMonitor(
  bindingId: string,
  input: MonitorStartInput = {},
): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/start`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  )
}

export function stopMonitor(bindingId: string): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/stop`,
    { method: 'POST' },
  )
}

export function restartMonitor(bindingId: string): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/restart`,
    { method: 'POST' },
  )
}

export function installMonitorStartup(
  bindingId: string,
): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/startup`,
    { method: 'POST' },
  )
}

export function removeMonitorStartup(
  bindingId: string,
): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/startup`,
    { method: 'DELETE' },
  )
}

export function verifyMonitorStartup(
  bindingId: string,
): Promise<MonitorBinding> {
  return request<MonitorBinding>(
    `/monitors/${encodeURIComponent(bindingId)}/startup/verify`,
    { method: 'POST' },
  )
}

export function testMonitorNotification(
  bindingId: string,
): Promise<MonitorNotificationTestResult> {
  return request<MonitorNotificationTestResult>(
    `/monitors/${encodeURIComponent(bindingId)}/notify-test`,
    { method: 'POST' },
  )
}

export function getMonitorSeries(
  bindingId: string,
  range = '30d',
  maxPoints = 500,
): Promise<MonitorSeriesResponse> {
  const params = new URLSearchParams({
    range,
    max_points: String(maxPoints),
  })
  return request<MonitorSeriesResponse>(
    `/monitors/${encodeURIComponent(bindingId)}/series?${params.toString()}`,
  )
}

const API = '/api/v1'

export class ApiError extends Error {
  status: number
  code: string

  constructor(message: string, status: number, code = 'request_failed') {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

async function request<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }
  if (init.method && !['GET', 'HEAD'].includes(init.method)) {
    headers.set('X-Amz-Cli-Web', '1')
  }

  const response = await fetch(`${API}${path}`, {
    ...init,
    headers,
    credentials: 'same-origin',
  })
  const text = await response.text()
  let data: unknown = null
  if (text.length > 0) {
    try {
      data = JSON.parse(text)
    } catch {
      data = null
    }
  }

  if (!response.ok) {
    const error = data as
      | { error?: { code?: string; message?: string } }
      | null
    throw new ApiError(
      error?.error?.message ?? `Request failed (HTTP ${response.status}).`,
      response.status,
      error?.error?.code ?? 'request_failed',
    )
  }

  return data as T
}

export function getMeta(): Promise<Meta> {
  return request<Meta>('/meta')
}

export function getHttpSettings(): Promise<HttpSettingsDiagnostics> {
  return request<HttpSettingsDiagnostics>('/http-settings')
}

export function saveHttpSettings(
  settings: HttpSettings,
): Promise<HttpSettingsDiagnostics> {
  return request<HttpSettingsDiagnostics>('/http-settings', {
    method: 'PUT',
    body: JSON.stringify(settings),
  })
}

export function clearHttpCooldown(
  domain: string,
): Promise<HttpSettingsDiagnostics> {
  return request<HttpSettingsDiagnostics>(
    `/http-settings/cooldowns/${encodeURIComponent(domain)}`,
    { method: 'DELETE' },
  )
}

export function getCompare(): Promise<CompareState> {
  return request<CompareState>('/compare')
}

export function addCompareItems(
  items: Array<{ domain: string; asin: string }>,
  source = 'web',
): Promise<CompareState> {
  return request<CompareState>('/compare/items', {
    method: 'POST',
    body: JSON.stringify({ items, source }),
  })
}

export function removeCompareItem(
  domain: string,
  asin: string,
): Promise<CompareState> {
  return request<CompareState>(
    `/compare/items/${encodeURIComponent(domain)}/${encodeURIComponent(asin)}`,
    { method: 'DELETE' },
  )
}

export function clearCompareItems(): Promise<CompareState> {
  return request<CompareState>('/compare', { method: 'DELETE' })
}

export function getMarketplaces(): Promise<MarketplaceResponse> {
  return request<MarketplaceResponse>('/marketplaces')
}

export function getSuggestions(
  query: string,
  domain: string,
  signal?: AbortSignal,
): Promise<SuggestionsResponse> {
  const params = new URLSearchParams({ q: query, domain })
  return request<SuggestionsResponse>(`/suggestions?${params.toString()}`, {
    signal,
  })
}

export async function ensureSession(): Promise<Meta> {
  const fragment = new URLSearchParams(location.hash.replace(/^#/, ''))
  const token = fragment.get('t')
  if (token) {
    await request<{ ok: boolean }>('/session', {
      method: 'POST',
      body: JSON.stringify({ token }),
    })
    history.replaceState(null, '', location.pathname + location.search)
  }
  return getMeta()
}

export function startSearch(
  input: SearchInput,
): Promise<{ job_id: string; history_id?: string | null }> {
  return request<{ job_id: string; history_id?: string | null }>('/search', {
    method: 'POST',
    body: JSON.stringify({ ...input, sort: amazonSort(input.sort) }),
  })
}

export function getLibrary(): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>('/library')
}

export function addLibraryProduct(
  product: LibraryProduct,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>('/library/products', {
    method: 'POST',
    body: JSON.stringify(product),
  })
}

export function updateLibraryProduct(
  productId: string,
  product: LibraryProduct,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/products/${encodeURIComponent(productId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(product),
    },
  )
}

export function deleteLibraryProduct(
  productId: string,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/products/${encodeURIComponent(productId)}`,
    { method: 'DELETE' },
  )
}

export function createLibraryGroup(
  group: LibraryGroup,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>('/library/groups', {
    method: 'POST',
    body: JSON.stringify(group),
  })
}

export function updateLibraryGroup(
  groupId: string,
  name: string,
  accent: LibraryGroup['accent'],
  description?: string | null,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/groups/${encodeURIComponent(groupId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify({ name, accent, description }),
    },
  )
}

export function deleteLibraryGroup(
  groupId: string,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/groups/${encodeURIComponent(groupId)}`,
    { method: 'DELETE' },
  )
}

export function addLibraryGroupProducts(
  groupId: string,
  productIds: string[],
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/groups/${encodeURIComponent(groupId)}/items`,
    {
      method: 'POST',
      body: JSON.stringify({ product_ids: productIds, quantity: 1 }),
    },
  )
}

export function removeLibraryGroupProduct(
  groupId: string,
  productId: string,
): Promise<LibraryEnvelope> {
  return request<LibraryEnvelope>(
    `/library/groups/${encodeURIComponent(groupId)}/items/` +
      encodeURIComponent(productId),
    { method: 'DELETE' },
  )
}

export function getHistory(): Promise<HistoryResponse> {
  return request<HistoryResponse>('/history')
}

export function getHistoryCache<T = SearchResultPayload>(
  entryId: string,
): Promise<HistoryCacheResponse<T>> {
  return request<HistoryCacheResponse<T>>(
    `/history/${encodeURIComponent(entryId)}/cache`,
  )
}

export function addHistoryProduct(input: {
  asin: string
  domain: string
  snapshot: HistoryProductSnapshot
}): Promise<HistoryEntry> {
  return request<HistoryEntry>('/history/product', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function deleteHistoryEntry(entryId: string): Promise<HistoryResponse> {
  return request<HistoryResponse>(
    `/history/${encodeURIComponent(entryId)}`,
    { method: 'DELETE' },
  )
}

export function clearHistory(): Promise<HistoryResponse> {
  return request<HistoryResponse>('/history', { method: 'DELETE' })
}

export function getUpdateStatus(): Promise<UpdateStatus> {
  return request<UpdateStatus>('/update')
}

export function checkForUpdates(): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>('/update/check', { method: 'POST' })
}

export function applyUpdate(): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>('/update/apply', { method: 'POST' })
}

export function getDealOptions(
  domain: string,
  dealType?: DealType,
  signal?: AbortSignal,
): Promise<DealOptions> {
  const params = new URLSearchParams({ domain })
  if (dealType) {
    params.set('deal_type', dealType)
  }
  return request<DealOptions>(`/deals/options?${params.toString()}`, {
    signal,
  })
}

export function startDeals(input: DealInput): Promise<{ job_id: string }> {
  return request<{ job_id: string }>('/deals', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getJob(id: string): Promise<SearchJob> {
  return request<SearchJob>(`/jobs/${encodeURIComponent(id)}`)
}

export function cancelJob(id: string): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>(`/jobs/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export function getProduct(
  asin: string,
  domain: string,
  signal?: AbortSignal,
  country = '',
  cap = '',
): Promise<ProductResponse> {
  const query = new URLSearchParams({
    domain,
    rating: 'true',
    reviews: 'true',
    options: 'true',
  })
  if (cap.trim().length > 0) {
    query.set('country', country)
    query.set('cap', cap)
  }
  return request<ProductResponse>(
    `/products/${encodeURIComponent(asin)}?${query.toString()}`,
    { signal },
  )
}

export interface VariantPricesResponse {
  variants?: ProductVariants | null
  warnings?: string[]
}

export function getVariantPrices(
  asin: string,
  domain: string,
  signal?: AbortSignal,
): Promise<VariantPricesResponse> {
  const query = new URLSearchParams({ domain })
  return request<VariantPricesResponse>(
    `/products/${encodeURIComponent(asin)}/variants?${query.toString()}`,
    { signal },
  )
}

export function refreshProductDetails(
  asin: string,
  domain: string,
  signal?: AbortSignal,
  country = '',
  cap = '',
): Promise<ProductResponse> {
  const query = new URLSearchParams({
    domain,
    rating: 'true',
    reviews: 'true',
    options: 'true',
  })
  if (cap.trim().length > 0) {
    query.set('country', country)
    query.set('cap', cap)
  }
  return request<ProductResponse>(
    `/products/${encodeURIComponent(asin)}/refresh?${query.toString()}`,
    { method: 'POST', signal },
  )
}

export function getUiState(): Promise<UiState> {
  return request<UiState>('/ui-state')
}

export function saveUiPreferences(
  preferences: UiPreferences,
): Promise<UiState> {
  return request<UiState>('/ui-state/preferences', {
    method: 'PUT',
    body: JSON.stringify(preferences),
  })
}

export function saveUiRecentSearches(
  recentSearches: RecentSearch[],
): Promise<UiState> {
  return request<UiState>('/ui-state/recent-searches', {
    method: 'PUT',
    body: JSON.stringify({ recent_searches: recentSearches }),
  })
}

export function shutdown(): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>('/shutdown', { method: 'POST' })
}
