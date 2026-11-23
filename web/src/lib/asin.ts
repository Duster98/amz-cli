/**
 * Amazon ASINs are ten alphanumeric characters, but ordinary words such as
 * "smartwatch" or "blackwatch" have the same length. Only auto-open the
 * product page for shapes that are unambiguous enough to be ASINs: B-prefixed
 * product codes containing a digit, and ten-digit ISBNs.
 */
export function looksLikeAsin(value: string): boolean {
  const candidate = value.trim()
  if (!/^[a-z0-9]{10}$/i.test(candidate)) {
    return false
  }

  if (/^\d{9}[\dX]$/i.test(candidate)) {
    return true
  }

  return /^b/i.test(candidate) && /\d/.test(candidate)
}
