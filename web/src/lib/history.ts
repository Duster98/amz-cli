import type {
  HistoryEntry,
  HistoryProductSnapshot,
  HistorySearchFilters,
  ProductResponse,
} from './api'
import type { PersistedSearchFilters } from './searchFilters'
import type { Translate } from '../i18n/types'
import { defaultSearchFilters, filtersFromPersisted } from './searchFilters'
import { formatPrice } from './format'

export function historyFiltersToPersisted(
  filters: HistorySearchFilters,
): PersistedSearchFilters {
  return {
    no_ads: filters.no_ads,
    delivery: filters.delivery,
    enrich: filters.enrich,
    min_rating: filters.min_rating,
    price_min: filters.price_min ?? null,
    price_max: filters.price_max ?? null,
    only_deals: filters.only_deals,
    free_shipping: filters.free_shipping,
    amazon_choice: filters.amazon_choice,
    sort: filters.sort,
  }
}

export function historyFiltersFromPersisted(
  filters: PersistedSearchFilters,
) {
  return filtersFromPersisted(filters)
}

export function hasHistoryFilters(filters?: HistorySearchFilters): boolean {
  if (!filters) {
    return false
  }
  return (
    filters.no_ads !== defaultSearchFilters.noAds
    || filters.delivery !== defaultSearchFilters.delivery
    || filters.enrich !== defaultSearchFilters.enrich
    || filters.min_rating !== defaultSearchFilters.minRating
    || filters.price_min != null
    || filters.price_max != null
    || filters.only_deals
    || filters.free_shipping
    || filters.amazon_choice
    || filters.sort !== defaultSearchFilters.sort
  )
}

export function historyFilterLabels(
  filters: HistorySearchFilters | undefined,
  t: Translate,
  currency = 'EUR',
  locale = 'it-IT',
): string[] {
  if (!filters) {
    return []
  }
  const labels: string[] = []
  if (filters.no_ads === false) {
    labels.push(t('history.filterAds'))
  }
  if (filters.delivery === false) {
    labels.push(t('history.filterDelivery'))
  }
  if (filters.enrich) {
    labels.push(t('history.filterEnrich'))
  }
  if (filters.min_rating > 0) {
    labels.push(t('history.filterRating', { rating: filters.min_rating }))
  }
  if (filters.price_min != null && filters.price_max != null) {
    labels.push(
      t('history.filterPriceRange', {
        from: formatPrice(filters.price_min, currency, '', locale),
        to: formatPrice(filters.price_max, currency, '', locale),
      }),
    )
  } else if (filters.price_min != null) {
    labels.push(
      t('history.filterPriceFrom', {
        from: formatPrice(filters.price_min, currency, '', locale),
      }),
    )
  } else if (filters.price_max != null) {
    labels.push(
      t('history.filterPriceTo', {
        to: formatPrice(filters.price_max, currency, '', locale),
      }),
    )
  }
  if (filters.only_deals) {
    labels.push(t('search.onSaleOnly'))
  }
  if (filters.free_shipping) {
    labels.push(t('search.freeShipping'))
  }
  if (filters.amazon_choice) {
    labels.push(t('search.amazonChoice'))
  }
  if (filters.sort && filters.sort !== 'relevance') {
    const key =
      filters.sort === 'price-asc'
        ? 'sort.priceAsc'
        : filters.sort === 'price-desc'
          ? 'sort.priceDesc'
          : filters.sort === 'reviews'
            ? 'sort.reviews'
            : filters.sort === 'newest'
              ? 'sort.newest'
              : filters.sort === 'bestsellers'
                ? 'sort.bestsellers'
                : 'sort.featured'
    labels.push(t(key))
  }
  return labels
}

export function historyProductSnapshot(
  response: ProductResponse,
): HistoryProductSnapshot {
  const product = response.product
  return {
    title: product.title,
    image: product.image,
    price: product.price,
    price_amount: response.price_amount ?? null,
    currency: response.currency || response.marketplace.currency,
    original_price: product.original_price ?? '',
    discount: product.discount ?? '',
    rating: product.rating,
    reviews: product.reviews ?? '',
    availability: product.availability ?? '',
    url: product.url,
  }
}

export function historySearchFiltersFromEntry(
  entry: HistoryEntry,
): PersistedSearchFilters {
  return historyFiltersToPersisted(
    entry.filters ?? {
      no_ads: true,
      delivery: true,
      enrich: false,
      min_rating: 0,
      price_min: null,
      price_max: null,
      only_deals: false,
      free_shipping: false,
      amazon_choice: false,
      sort: 'relevance',
    },
  )
}
