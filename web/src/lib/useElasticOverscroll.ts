import { useEffect, type RefObject } from 'react'

const MAX_OFFSET = 56
const DAMPING = 120
const RELEASE_MS = 120

export function useElasticOverscroll(
  targetRef: RefObject<HTMLElement | null>,
  enabled: boolean,
): void {
  useEffect(() => {
    const target = targetRef.current
    if (!enabled || !target) {
      return
    }

    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      return
    }

    let raw = 0
    let active = false
    let releaseTimer = 0
    let resetTimer = 0

    const reset = () => {
      if (!active && raw === 0) {
        return
      }
      raw = 0
      active = false
      target.classList.remove('is-overscrolling')
      target.style.transform = ''
      window.clearTimeout(resetTimer)
      resetTimer = window.setTimeout(() => {
        if (!active) {
          target.style.transform = ''
        }
      }, 460)
    }

    const onWheel = (event: WheelEvent) => {
      if (
        event.defaultPrevented ||
        event.ctrlKey ||
        event.metaKey ||
        document.body.style.overflow === 'hidden'
      ) {
        return
      }
      if (
        event.target instanceof Element &&
        event.target.closest(
          '.rail, .search-suggestions, .filter-panel, .modal-backdrop',
        )
      ) {
        return
      }

      const root = document.documentElement
      const viewport = window.innerHeight
      const maximum = root.scrollHeight - viewport
      if (maximum <= 0) {
        return
      }

      const atTop = window.scrollY <= 0.5
      const atBottom = window.scrollY >= maximum - 1
      const pullingPastTop = atTop && event.deltaY < 0
      const pullingPastBottom = atBottom && event.deltaY > 0
      if (!pullingPastTop && !pullingPastBottom) {
        reset()
        return
      }

      event.preventDefault()
      if (!active) {
        active = true
        target.classList.add('is-overscrolling')
      }

      raw += event.deltaY
      const offset =
        -Math.sign(raw) * MAX_OFFSET * (1 - Math.exp(-Math.abs(raw) / DAMPING))
      target.style.transform = `translate3d(0, ${offset.toFixed(2)}px, 0)`

      window.clearTimeout(releaseTimer)
      releaseTimer = window.setTimeout(reset, RELEASE_MS)
    }

    window.addEventListener('wheel', onWheel, { passive: false })
    return () => {
      window.removeEventListener('wheel', onWheel)
      window.clearTimeout(releaseTimer)
      window.clearTimeout(resetTimer)
      target.classList.remove('is-overscrolling')
      target.style.transform = ''
    }
  }, [enabled, targetRef])
}
