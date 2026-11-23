import { createRoot } from 'react-dom/client'
import App from './App'
import { I18nProvider } from './i18n/I18nProvider'
import './styles.css'

const initialTheme = window.matchMedia?.('(prefers-color-scheme: dark)').matches
  ? 'dark'
  : 'light'
document.documentElement.dataset.theme = initialTheme

const root = document.getElementById('root')
if (!root) {
  throw new Error('Element #root not found.')
}

createRoot(root).render(
  <I18nProvider>
    <App />
  </I18nProvider>,
)
