const FIVE_DIGITS = new Set(['IT', 'FR', 'ES', 'DE'])

export function normalizePostalCode(country: string, postalCode: string): string {
  const countryCode = country.trim().toUpperCase()
  const value = postalCode.trim()
  return countryCode === 'GB' ? value.toUpperCase() : value
}

export function postalCodePattern(country: string): RegExp | null {
  switch (country.trim().toUpperCase()) {
    case 'IT':
    case 'FR':
    case 'ES':
    case 'DE':
      return /^\d{5}$/
    case 'US':
      return /^\d{5}(?:-\d{4})?$/
    case 'GB':
      return /^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$/i
    case 'JP':
      return /^\d{3}-?\d{4}$/
    default:
      return /^[A-Za-z0-9][A-Za-z0-9\s-]{1,15}$/
  }
}

export function isValidPostalCode(
  country: string,
  postalCode: string,
): boolean {
  const countryCode = country.trim().toUpperCase()
  const value = postalCode.trim()
  if (countryCode.length === 0) {
    return value.length === 0
  }
  if (value.length === 0) {
    return false
  }
  const pattern = postalCodePattern(countryCode)
  return pattern ? pattern.test(value) : true
}

export function postalCodePlaceholder(country: string): string {
  switch (country.trim().toUpperCase()) {
    case 'IT':
    case 'FR':
    case 'ES':
    case 'DE':
      return '20121'
    case 'US':
      return '10019'
    case 'GB':
      return 'E1 6AN'
    case 'JP':
      return '100-0001'
    default:
      return FIVE_DIGITS.has(country.trim().toUpperCase()) ? '20121' : '—'
  }
}
