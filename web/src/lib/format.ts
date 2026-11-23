export function formatPrice(
  amount: number | null | undefined,
  currency: string | undefined,
  fallback: string,
  locale = 'it-IT',
): string {
  if (amount === null || amount === undefined || !Number.isFinite(amount)) {
    return fallback || '—'
  }

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currency || 'EUR',
    }).format(amount)
  } catch {
    return `${amount.toFixed(2)} ${currency ?? ''}`.trim()
  }
}

export function parseRating(value: string | undefined): number {
  if (!value) {
    return 0
  }

  const matches = [...value.matchAll(/\d+(?:[.,]\d+)?/g)]
  if (matches.length === 0) {
    return 0
  }
  const token = value.includes('つ星のうち')
    ? matches[matches.length - 1][0]
    : matches[0][0]
  const parsed = Number.parseFloat(token.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : 0
}

export function formatNumber(value: number | undefined): string {
  if (value === undefined || !Number.isFinite(value)) {
    return '—'
  }
  return new Intl.NumberFormat('it-IT').format(value)
}

export function formatDuration(milliseconds: number | undefined): string {
  if (!milliseconds || milliseconds < 0) {
    return '—'
  }
  if (milliseconds < 1000) {
    return `${milliseconds} ms`
  }
  return `${(milliseconds / 1000).toFixed(1)} s`
}

export function shortAsin(asin: string): string {
  return asin.length > 10 ? asin.slice(0, 10) : asin
}

export function conditionLabel(value: string | null | undefined): string | null {
  if (!value) {
    return null
  }

  const normalized = value.toLocaleLowerCase('it-IT')
  if (
    /(occasion|usat[oaie]|\bused\b|gebraucht|usado|segunda mano|segunda mão|second hand|中古)/.test(
      normalized,
    )
  ) {
    return 'Used'
  }
  if (/(neuf|nuov[oaie]|\bnew\b|\bneu\b|nuevo|新品)/.test(normalized)) {
    return 'New'
  }
  if (
    /(remis|renewed|rigenerat|refurb|ricondizionat|rinnovato|reconditionn|generalüberholt|generalueberholt|reacondicionad|整備済み)/.test(
      normalized,
    )
  ) {
    return 'Refurbished'
  }

  return value
}

export function conditionTranslationKey(
  value: string | null | undefined,
): 'condition.used' | 'condition.new' | 'condition.refurbished' | null {
  switch (conditionLabel(value)) {
    case 'Used':
      return 'condition.used'
    case 'New':
      return 'condition.new'
    case 'Refurbished':
      return 'condition.refurbished'
    default:
      return null
  }
}

export function conditionTypeTranslationKey(
  value: string | null | undefined,
): 'condition.used' | 'condition.new' | 'condition.refurbished' | null {
  switch (value) {
    case 'used':
      return 'condition.used'
    case 'new':
      return 'condition.new'
    case 'refurbished':
      return 'condition.refurbished'
    default:
      return conditionTranslationKey(value)
  }
}

export function isUsedCondition(value: string | null | undefined): boolean {
  return conditionLabel(value) === 'Used'
}

export type ConditionGrade =
  | 'premium'
  | 'excellent'
  | 'good'
  | 'acceptable'

export function conditionGrade(
  value: string | null | undefined,
): ConditionGrade | null {
  if (!value) {
    return null
  }
  const normalized = value.toLocaleLowerCase('it-IT')
  if (/\bpremium\b|プレミアム/.test(normalized)) {
    return 'premium'
  }
  if (
    /eccellent|excellent|sehr gut|sehr gute|tr[eè]s bon|muy bueno|very good|ottimo|come nuovo|like new|非常に良い|极佳|極上/.test(
      normalized,
    )
  ) {
    return 'excellent'
  }
  if (/\bbuono\b|\bgood\b|\bgut(?:e)?\b|\bbon\b|\bbueno\b|良い/.test(normalized)) {
    return 'good'
  }
  if (/accettabile|acceptable|akzeptabel|aceptable|許容|可/.test(normalized)) {
    return 'acceptable'
  }
  return null
}

export function conditionGradeTranslationKey(
  grade: ConditionGrade | null,
):
  | 'condition.grade.premium'
  | 'condition.grade.excellent'
  | 'condition.grade.good'
  | 'condition.grade.acceptable'
  | null {
  return grade ? `condition.grade.${grade}` : null
}

export function conditionGradeFromToken(
  value: string | null | undefined,
): ConditionGrade | null {
  return value === 'premium' ||
    value === 'excellent' ||
    value === 'good' ||
    value === 'acceptable'
    ? value
    : null
}
