import type { CSSProperties } from 'react'
import type {
  Library,
  LibraryGroup,
  LibraryProduct,
  LibrarySnapshot,
  ProductResponse,
  SearchItem,
} from './api'

export function libraryProductId(domain: string, asin: string): string {
  return `${domain.trim().toLowerCase()}:${asin.trim().toUpperCase()}`
}

export function isProductSaved(
  library: Library,
  domain: string,
  asin: string,
): boolean {
  const id = libraryProductId(domain, asin)
  return library.products.some((product) => product.id === id)
}

export function snapshotFromSearch(item: SearchItem): LibrarySnapshot {
  return {
    title: item.title ?? '',
    image: item.image ?? '',
    price: item.price ?? '',
    price_amount: item.price_amount ?? null,
    currency: item.currency ?? '',
    original_price: item.original_price ?? null,
    discount: item.discount ?? null,
    rating: item.rating ?? '',
    reviews: item.reviews ?? '',
    delivery_text: item.delivery_text ?? null,
    availability: null,
    url: null,
    charger: null,
  }
}

export function snapshotFromProduct(response: ProductResponse): LibrarySnapshot {
  const product = response.product
  return {
    title: product.title,
    image: product.image,
    price: product.price,
    price_amount: response.price_amount ?? null,
    currency: response.currency || response.marketplace.currency,
    original_price: product.original_price,
    discount: product.discount,
    rating: product.rating,
    reviews: product.reviews,
    delivery_text: product.delivery?.time ?? null,
    availability: product.availability,
    url: product.url,
    charger: product.charger ?? null,
  }
}

export function createSavedProductFromSearch(
  item: SearchItem,
  domain: string,
  now = new Date().toISOString(),
): LibraryProduct {
  const productDomain = (item.marketplace_id ?? domain).toLowerCase()
  const asin = item.asin.toUpperCase()
  const snapshot = snapshotFromSearch(item)
  return {
    id: libraryProductId(productDomain, asin),
    asin,
    domain: productDomain,
    saved_at: now,
    updated_at: now,
    last_checked_at: null,
    price_at_save: snapshot.price_amount ?? null,
    price_last: snapshot.price_amount ?? null,
    currency: snapshot.currency,
    snapshot,
  }
}

export function createSavedProductFromDetail(
  response: ProductResponse,
  now = new Date().toISOString(),
): LibraryProduct {
  const domain = response.marketplace.id
  const asin = response.product.asin.toUpperCase()
  const snapshot = snapshotFromProduct(response)
  return {
    id: libraryProductId(domain, asin),
    asin,
    domain,
    saved_at: now,
    updated_at: now,
    last_checked_at: response.stale ? null : response.fetched_at,
    price_at_save: snapshot.price_amount ?? null,
    price_last: snapshot.price_amount ?? null,
    currency: snapshot.currency,
    snapshot,
  }
}

export function savedProductCheckedAt(product: LibraryProduct): string {
  return product.last_checked_at ?? product.saved_at
}

export function wasSavedProductCheckedWithin(
  product: LibraryProduct,
  minutes: number,
  now = Date.now(),
): boolean {
  if (!product.last_checked_at) {
    return false
  }
  const checked = Date.parse(product.last_checked_at)
  return Number.isFinite(checked) && now - checked < minutes * 60_000
}

export function isSavedProductStale(
  product: LibraryProduct,
  minutes: number,
  now = Date.now(),
): boolean {
  const checked = Date.parse(savedProductCheckedAt(product))
  return !Number.isFinite(checked) || now - checked >= minutes * 60_000
}

export function groupProducts(
  group: LibraryGroup,
  products: LibraryProduct[],
): LibraryProduct[] {
  const byId = new Map(products.map((product) => [product.id, product]))
  return group.items
    .map((item) => byId.get(item.product_id))
    .filter((product): product is LibraryProduct => Boolean(product))
}

export function productGroupIds(
  groups: LibraryGroup[],
  productId: string,
): string[] {
  return groups
    .filter((group) => group.items.some((item) => item.product_id === productId))
    .map((group) => group.id)
}

export type PriceDelta = {
  direction: 'down' | 'up'
  percent: number
}

export function priceDelta(product: LibraryProduct): PriceDelta | null {
  const before = product.price_at_save
  const after = product.price_last ?? product.snapshot.price_amount
  if (before == null || after == null || before <= 0 || after === before) {
    return null
  }
  return {
    direction: after < before ? 'down' : 'up',
    percent: (Math.abs(after - before) / before) * 100,
  }
}

export type GroupPriceMin = {
  amount: number
  currency: string
}

export function groupMinPrice(
  group: LibraryGroup,
  products: LibraryProduct[],
): GroupPriceMin | null {
  let min: GroupPriceMin | null = null
  for (const product of groupProducts(group, products)) {
    const price = product.price_last ?? product.snapshot.price_amount
    if (price == null || price < 0) {
      continue
    }
    const currency = (
      product.currency ||
      product.snapshot.currency ||
      'EUR'
    ).toUpperCase()
    if (min === null || price < min.amount) {
      min = { amount: price, currency }
    }
  }
  return min
}


export type GroupPriceTotal = {
  currency: string
  amount: number
  priced: number
}

export function groupPriceTotals(
  group: LibraryGroup,
  products: LibraryProduct[],
): GroupPriceTotal[] {
  const quantities = new Map(
    group.items.map((item) => [item.product_id, Math.max(1, item.quantity)]),
  )
  const totals = new Map<string, GroupPriceTotal>()
  for (const product of groupProducts(group, products)) {
    const price = product.price_last ?? product.snapshot.price_amount
    if (price == null || price < 0) {
      continue
    }
    const currency = (
      product.currency ||
      product.snapshot.currency ||
      'EUR'
    ).toUpperCase()
    const current = totals.get(currency) ?? {
      currency,
      amount: 0,
      priced: 0,
    }
    current.amount += price * (quantities.get(product.id) ?? 1)
    current.priced += 1
    totals.set(currency, current)
  }
  return [...totals.values()].sort((left, right) => right.amount - left.amount)
}


export const groupAccentPalette = [
  'accent',
  'blue',
  'gold',
  'rust',
  'green',
  'teal',
  'violet',
  'rose',
  'slate',
  'orange',
  'plum',
  'sky',
] as const

const groupAccentHex: Record<string, string> = {
  accent: '#ad4a14',
  blue: '#3d6583',
  gold: '#855a10',
  rust: '#a8442a',
  green: '#2f6b4f',
  teal: '#1f6f70',
  violet: '#6b4f9a',
  rose: '#a54865',
  slate: '#536575',
  orange: '#b45f1f',
  plum: '#7d4a6b',
  sky: '#3f6f95',
}

export function groupAccentClass(accent: string): string {
  return accent.startsWith('#')
    ? 'saved-accent--custom'
    : `saved-accent--${accent}`
}

export function groupAccentStyle(accent: string): CSSProperties {
  if (!accent.startsWith('#')) {
    return {}
  }
  return { '--custom-accent': accent } as CSSProperties
}

export function groupAccentHexValue(accent: string): string {
  return accent.startsWith('#')
    ? accent
    : groupAccentHex[accent] ?? groupAccentHex.accent
}
