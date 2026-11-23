export const languageCodes = ['en', 'it', 'de', 'fr', 'es', 'ja'] as const

export type LanguageCode = (typeof languageCodes)[number]

export type TranslationKey = keyof typeof import('./en').en

export type TranslationVars = Record<string, string | number>

export type Translate = (
  key: TranslationKey,
  vars?: TranslationVars,
) => string

export const languageNames: Record<LanguageCode, string> = {
  en: 'English',
  it: 'Italiano',
  de: 'Deutsch',
  fr: 'Français',
  es: 'Español',
  ja: '日本語',
}

export const languageLocales: Record<LanguageCode, string> = {
  en: 'en-US',
  it: 'it-IT',
  de: 'de-DE',
  fr: 'fr-FR',
  es: 'es-ES',
  ja: 'ja-JP',
}

export function isLanguageCode(value: unknown): value is LanguageCode {
  return (
    typeof value === 'string' &&
    (languageCodes as readonly string[]).includes(value)
  )
}
