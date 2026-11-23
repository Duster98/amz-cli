import type { ProductDetail } from './api'
import type { Translate } from '../i18n/types'

export function fulfilmentLabel(
  detail: Pick<ProductDetail, 'seller' | 'shipped_by'>,
  displaySeller: string,
  t: Translate,
): string {
  const seller = displaySeller.trim() || detail.seller?.trim() || ''
  const shipper = detail.shipped_by?.trim() || ''
  if (seller && shipper) {
    const sameSeller =
      seller.localeCompare(shipper, undefined, { sensitivity: 'base' }) === 0
    return sameSeller
      ? t('product.soldAndShippedBy', { seller })
      : t('product.soldByShippedBy', { seller, shipper })
  }
  if (seller) {
    return t('product.soldBy', { seller })
  }
  if (shipper) {
    return t('product.shippedBy', { shipper })
  }
  return ''
}
