import type { TranslationKey } from '../i18n/types'
import type { SearchItem } from './api'
import { parseRating } from './format'

export type SearchSort =
  | 'relevance'
  | 'price-asc'
  | 'price-desc'
  | 'reviews'
  | 'newest'
  | 'bestsellers'

export interface SearchFilterState {
  noAds: boolean
  delivery: boolean
  enrich: boolean
  minRating: number
  priceMin: string
  priceMax: string
  onlyDeals: boolean
  freeShipping: boolean
  amazonChoice: boolean
  sort: SearchSort
}

export interface PersistedSearchFilters {
  no_ads: boolean
  delivery: boolean
  enrich: boolean
  min_rating: number
  price_min: number | null
  price_max: number | null
  only_deals: boolean
  free_shipping: boolean
  amazon_choice: boolean
  sort: SearchSort
}

export const sortOptions: Array<{
  value: SearchSort
  labelKey: TranslationKey
  amazon: string
}> = [
  { value: 'relevance', labelKey: 'sort.featured', amazon: 'relevanceblender' },
  {
    value: 'price-asc',
    labelKey: 'sort.priceAsc',
    amazon: 'price-asc-rank',
  },
  {
    value: 'price-desc',
    labelKey: 'sort.priceDesc',
    amazon: 'price-desc-rank',
  },
  { value: 'reviews', labelKey: 'sort.reviews', amazon: 'review-rank' },
  { value: 'newest', labelKey: 'sort.newest', amazon: 'date-desc-rank' },
  {
    value: 'bestsellers',
    labelKey: 'sort.bestsellers',
    amazon: 'exact-aware-popularity-rank',
  },
]

export const defaultSearchFilters: SearchFilterState = {
  noAds: true,
  delivery: true,
  enrich: false,
  minRating: 0,
  priceMin: '',
  priceMax: '',
  onlyDeals: false,
  freeShipping: false,
  amazonChoice: false,
  sort: 'relevance',
}

export function amazonSort(sort: SearchSort): string {
  return sortOptions.find((option) => option.value === sort)?.amazon ??
    'relevanceblender'
}

export function parsePriceInput(value: string): number | null {
  const cleaned = value.trim().replace(/\s/g, '')
  if (cleaned.length === 0) {
    return null
  }

  let normalized = cleaned
  if (cleaned.includes(',') && cleaned.includes('.')) {
    normalized =
      cleaned.lastIndexOf(',') > cleaned.lastIndexOf('.')
        ? cleaned.replace(/\./g, '').replace(',', '.')
        : cleaned.replace(/,/g, '')
  } else if (cleaned.includes(',')) {
    normalized = cleaned.replace(',', '.')
  }

  const parsed = Number.parseFloat(normalized)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function priceRange(filters: SearchFilterState): {
  min: number | null
  max: number | null
} {
  let min = parsePriceInput(filters.priceMin)
  let max = parsePriceInput(filters.priceMax)
  if (min !== null && max !== null && min > max) {
    ;[min, max] = [max, min]
  }
  return { min, max }
}

export function hasLocalFilters(filters: SearchFilterState): boolean {
  return (
    filters.minRating > 0 ||
    filters.priceMin.trim().length > 0 ||
    filters.priceMax.trim().length > 0 ||
    filters.onlyDeals ||
    filters.freeShipping ||
    filters.amazonChoice
  )
}

export function activeFilterCount(
  filters: SearchFilterState,
  domains: string[],
): number {
  let count = 0
  if (!filters.noAds) count++
  if (!filters.delivery) count++
  if (filters.enrich) count++
  if (filters.minRating > 0) count++
  if (
    filters.priceMin.trim().length > 0 ||
    filters.priceMax.trim().length > 0
  ) {
    count++
  }
  if (filters.onlyDeals) count++
  if (filters.freeShipping) count++
  if (filters.amazonChoice) count++
  if (filters.sort !== 'relevance') count++
  if (domains.length > 1) count++
  return count
}

function looksFreeDelivery(text: string | null | undefined): boolean {
  if (!text) {
    return false
  }
  return /(?:gratuit|free|kostenlos|gratis|gr[aá]tis|送料無料|無料配送)/i.test(
    text,
  )
}

export function isLocalSort(sort: SearchSort): boolean {
  return sort === 'price-asc' || sort === 'price-desc' || sort === 'reviews'
}

function parseReviewCount(value: string | null | undefined): number {
  if (!value) {
    return 0
  }
  const digits = value.replace(/[^\d]/g, '')
  const parsed = Number.parseInt(digits, 10)
  return Number.isFinite(parsed) ? parsed : 0
}

export function sortSearchItems(
  items: SearchItem[],
  sort: SearchSort,
): SearchItem[] {
  if (!isLocalSort(sort)) {
    return items
  }

  const decorated = items.map((item, index) => ({ item, index }))
  decorated.sort((left, right) => {
    if (sort === 'reviews') {
      return (
        parseReviewCount(right.item.reviews) -
          parseReviewCount(left.item.reviews) ||
        left.index - right.index
      )
    }

    const leftPrice = left.item.price_amount
    const rightPrice = right.item.price_amount
    if (leftPrice === null || leftPrice === undefined) {
      return rightPrice === null || rightPrice === undefined
        ? left.index - right.index
        : 1
    }
    if (rightPrice === null || rightPrice === undefined) {
      return -1
    }

    const difference =
      sort === 'price-asc'
        ? leftPrice - rightPrice
        : rightPrice - leftPrice
    return difference || left.index - right.index
  })
  return decorated.map((entry) => entry.item)
}

export function applySearchFilters(
  items: SearchItem[],
  filters: SearchFilterState,
): SearchItem[] {
  const { min, max } = priceRange(filters)
  const filtered = items.filter((item) => {
    if (filters.noAds && item.sponsored) {
      return false
    }

    if (filters.minRating > 0 && parseRating(item.rating) < filters.minRating) {
      return false
    }

    if (min !== null || max !== null) {
      const price = item.price_amount
      if (price === null || price === undefined) {
        return false
      }
      if (min !== null && price < min) {
        return false
      }
      if (max !== null && price > max) {
        return false
      }
    }

    if (
      filters.onlyDeals &&
      !item.original_price &&
      !item.discount &&
      !item.deal &&
      !item.coupon
    ) {
      return false
    }

    if (
      filters.freeShipping &&
      item.free_delivery !== true &&
      !looksFreeDelivery(item.delivery_text)
    ) {
      return false
    }

    if (filters.amazonChoice && item.amazon_choice !== true) {
      return false
    }

    return true
  })

  return sortSearchItems(filtered, filters.sort)
}

export function filtersToPersisted(
  filters: SearchFilterState,
): PersistedSearchFilters {
  return {
    no_ads: filters.noAds,
    delivery: filters.delivery,
    enrich: filters.enrich,
    min_rating: filters.minRating,
    price_min: parsePriceInput(filters.priceMin),
    price_max: parsePriceInput(filters.priceMax),
    only_deals: filters.onlyDeals,
    free_shipping: filters.freeShipping,
    amazon_choice: filters.amazonChoice,
    sort: filters.sort,
  }
}

export function filtersFromPersisted(
  filters: Partial<PersistedSearchFilters> | undefined,
): SearchFilterState {
  const sort = sortOptions.some((option) => option.value === filters?.sort)
    ? (filters!.sort as SearchSort)
    : 'relevance'
  const minRating = [0, 3, 4, 4.5].includes(filters?.min_rating ?? 0)
    ? filters?.min_rating ?? 0
    : 0
  const priceToInput = (value: number | null | undefined): string =>
    value === null || value === undefined
      ? ''
      : String(value).replace('.', ',')

  return {
    noAds: filters?.no_ads ?? true,
    delivery: filters?.delivery ?? true,
    enrich: filters?.enrich ?? false,
    minRating,
    priceMin: priceToInput(filters?.price_min),
    priceMax: priceToInput(filters?.price_max),
    onlyDeals: filters?.only_deals ?? false,
    freeShipping: filters?.free_shipping ?? false,
    amazonChoice: filters?.amazon_choice ?? false,
    sort,
  }
}
