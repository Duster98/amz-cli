import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { de } from './de'
import { en } from './en'
import { es } from './es'
import { fr } from './fr'
import { it } from './it'
import { ja } from './ja'
import {
  languageLocales,
  type LanguageCode,
  type Translate,
  type TranslationVars,
} from './types'

type I18nValue = {
  language: LanguageCode
  languageLocale: string
  setLanguage: (language: LanguageCode) => void
  t: Translate
  formatNumber: (value: number | undefined) => string
}

const dictionaries: Record<LanguageCode, Record<string, string>> = {
  en,
  it,
  de,
  fr,
  es,
  ja,
}

const I18nContext = createContext<I18nValue | null>(null)

function interpolate(template: string, vars?: TranslationVars): string {
  if (!vars) {
    return template
  }
  return template.replace(/\{(\w+)\}/g, (match, key: string) => {
    const value = vars[key]
    return value === undefined ? match : String(value)
  })
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguage] = useState<LanguageCode>('en')

  useEffect(() => {
    document.documentElement.lang = language
    document.documentElement.dir = 'ltr'
  }, [language])

  const t = useCallback<Translate>(
    (key, vars) => {
      const template =
        dictionaries[language][key] ?? dictionaries.en[key] ?? key
      return interpolate(template, vars)
    },
    [language],
  )

  const formatNumber = useCallback(
    (value: number | undefined) => {
      if (value === undefined || !Number.isFinite(value)) {
        return '—'
      }
      return new Intl.NumberFormat(languageLocales[language]).format(value)
    },
    [language],
  )

  const value = useMemo<I18nValue>(
    () => ({
      language,
      languageLocale: languageLocales[language],
      setLanguage,
      t,
      formatNumber,
    }),
    [formatNumber, language, t],
  )

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

export function useI18n(): I18nValue {
  const value = useContext(I18nContext)
  if (!value) {
    throw new Error('useI18n must be used inside I18nProvider.')
  }
  return value
}
