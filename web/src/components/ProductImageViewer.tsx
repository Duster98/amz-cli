import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
} from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import {
  clampImageOffset,
  clampImageZoom,
  MAX_IMAGE_ZOOM,
  MIN_IMAGE_ZOOM,
  QUICK_IMAGE_ZOOM,
  zoomImageAtPoint,
  zoomPercent,
  type ImageZoomState,
} from '../lib/imageZoom'
import { Icon, ProductImage } from './ui'

const EXIT_MS = 170

type Point = {
  x: number
  y: number
}

type Geometry = {
  width: number
  height: number
  imageWidth: number
  imageHeight: number
}

type PanState = {
  id: number
  startX: number
  startY: number
  offsetX: number
  offsetY: number
}

type PinchState = {
  distance: number
  scale: number
  midX: number
  midY: number
  offsetX: number
  offsetY: number
}

type TapState = {
  id: number
  x: number
  y: number
  time: number
  moved: boolean
}

function pointDistance(first: Point, second: Point): number {
  return Math.hypot(second.x - first.x, second.y - first.y)
}

function pointMidpoint(first: Point, second: Point): Point {
  return {
    x: (first.x + second.x) / 2,
    y: (first.y + second.y) / 2,
  }
}

export function ProductImageViewer({
  images,
  index: initialIndex,
  title,
  returnFocus,
  onIndexChange,
  onClose,
}: {
  images: string[]
  index: number
  title: string
  returnFocus?: HTMLElement | null
  onIndexChange?: (index: number) => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const count = images.length
  const [current, setCurrent] = useState(() =>
    Math.min(Math.max(initialIndex, 0), Math.max(count - 1, 0)),
  )
  const [zoom, setZoomState] = useState<ImageZoomState>({
    scale: MIN_IMAGE_ZOOM,
    x: 0,
    y: 0,
  })
  const [loaded, setLoaded] = useState(false)
  const [closing, setClosing] = useState(false)
  const [grabbing, setGrabbing] = useState(false)
  const panelRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLDivElement>(null)
  const stripRef = useRef<HTMLDivElement>(null)
  const restoreRef = useRef<Element | null>(null)
  const closeTimer = useRef<number | null>(null)
  const zoomRef = useRef(zoom)
  const pointersRef = useRef(new Map<number, Point>())
  const panRef = useRef<PanState | null>(null)
  const pinchRef = useRef<PinchState | null>(null)
  const tapRef = useRef<TapState | null>(null)

  const setZoom = useCallback((next: ImageZoomState) => {
    zoomRef.current = next
    setZoomState(next)
  }, [])

  const resetZoom = useCallback(() => {
    setZoom({ scale: MIN_IMAGE_ZOOM, x: 0, y: 0 })
  }, [setZoom])

  const measure = useCallback((): Geometry | null => {
    const canvas = canvasRef.current
    if (!canvas) {
      return null
    }
    const image = canvas.querySelector<HTMLImageElement>('.image-viewer-image')
    if (
      !image
      || image.naturalWidth <= 0
      || image.naturalHeight <= 0
      || canvas.clientWidth <= 0
      || canvas.clientHeight <= 0
    ) {
      return null
    }
    const fit = Math.min(
      canvas.clientWidth / image.naturalWidth,
      canvas.clientHeight / image.naturalHeight,
    )
    return {
      width: canvas.clientWidth,
      height: canvas.clientHeight,
      imageWidth: image.naturalWidth * fit,
      imageHeight: image.naturalHeight * fit,
    }
  }, [])

  const applyZoom = useCallback(
    (nextScale: number, clientX: number, clientY: number) => {
      const canvas = canvasRef.current
      const geometry = measure()
      if (!canvas || !geometry) {
        return
      }
      const rect = canvas.getBoundingClientRect()
      setZoom(
        zoomImageAtPoint(
          zoomRef.current,
          nextScale,
          clientX - rect.left,
          clientY - rect.top,
          geometry.width,
          geometry.height,
          geometry.imageWidth,
          geometry.imageHeight,
        ),
      )
    },
    [measure, setZoom],
  )

  const zoomToCenter = useCallback(
    (nextScale: number) => {
      const canvas = canvasRef.current
      if (!canvas) {
        return
      }
      const rect = canvas.getBoundingClientRect()
      applyZoom(
        nextScale,
        rect.left + rect.width / 2,
        rect.top + rect.height / 2,
      )
    },
    [applyZoom],
  )

  const toggleZoom = useCallback(
    (clientX: number, clientY: number) => {
      if (zoomRef.current.scale > MIN_IMAGE_ZOOM + 0.01) {
        resetZoom()
        return
      }
      applyZoom(QUICK_IMAGE_ZOOM, clientX, clientY)
    },
    [applyZoom, resetZoom],
  )

  const requestClose = useCallback(() => {
    if (closing) {
      return
    }
    setClosing(true)
    closeTimer.current = window.setTimeout(() => {
      closeTimer.current = null
      onClose()
    }, EXIT_MS)
  }, [closing, onClose])

  const goTo = useCallback(
    (delta: number) => {
      if (count < 2) {
        return
      }
      setCurrent((value) => (value + delta + count) % count)
    },
    [count],
  )

  useLayoutEffect(() => {
    resetZoom()
    setLoaded(false)
    onIndexChange?.(current)
  }, [current, onIndexChange, resetZoom])

  useEffect(() => {
    const image = canvasRef.current?.querySelector<HTMLImageElement>(
      '.image-viewer-image',
    )
    if (image?.complete && image.naturalWidth > 0) {
      setLoaded(true)
    }
  }, [current])

  useEffect(() => {
    restoreRef.current = returnFocus ?? document.activeElement
    panelRef.current?.focus()

    const previousOverflow = document.body.style.overflow
    const previousPadding = document.body.style.paddingRight
    const scrollbar = window.innerWidth - document.documentElement.clientWidth
    const gutterStable =
      typeof CSS !== 'undefined'
      && typeof CSS.supports === 'function'
      && CSS.supports('scrollbar-gutter', 'stable')
    document.body.style.overflow = 'hidden'
    if (!gutterStable && scrollbar > 0) {
      document.body.style.paddingRight = `${scrollbar}px`
    }

    return () => {
      document.body.style.overflow = previousOverflow
      document.body.style.paddingRight = previousPadding
      if (closeTimer.current !== null) {
        window.clearTimeout(closeTimer.current)
      }
      if (restoreRef.current instanceof HTMLElement) {
        restoreRef.current.focus()
      }
    }
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      event.stopPropagation()
      if (event.key === 'Escape') {
        event.preventDefault()
        requestClose()
        return
      }
      if (event.key === 'ArrowLeft') {
        event.preventDefault()
        goTo(-1)
        return
      }
      if (event.key === 'ArrowRight') {
        event.preventDefault()
        goTo(1)
        return
      }
      if (event.key === '+' || event.key === '=') {
        event.preventDefault()
        zoomToCenter(zoomRef.current.scale + 0.5)
        return
      }
      if (event.key === '-' || event.key === '_') {
        event.preventDefault()
        zoomToCenter(zoomRef.current.scale - 0.5)
        return
      }
      if (event.key === '0') {
        event.preventDefault()
        resetZoom()
        return
      }
      if (event.key !== 'Tab' || !panelRef.current) {
        return
      }

      const focusable = Array.from(
        panelRef.current.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
        ),
      )
      if (focusable.length === 0) {
        event.preventDefault()
        panelRef.current.focus()
        return
      }
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      const onPanel = active === panelRef.current
      if (
        event.shiftKey
        && (active === first || onPanel || !panelRef.current.contains(active))
      ) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (active === last || onPanel)) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [goTo, requestClose, resetZoom, zoomToCenter])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) {
      return
    }
    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      const factor = Math.exp(-event.deltaY * 0.0015)
      applyZoom(
        clampImageZoom(zoomRef.current.scale * factor),
        event.clientX,
        event.clientY,
      )
    }
    canvas.addEventListener('wheel', onWheel, { passive: false })
    return () => canvas.removeEventListener('wheel', onWheel)
  }, [applyZoom])

  useLayoutEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) {
      return
    }
    const clamp = () => {
      const geometry = measure()
      if (!geometry) {
        return
      }
      const next = clampImageOffset(
        zoomRef.current,
        geometry.width,
        geometry.height,
        geometry.imageWidth,
        geometry.imageHeight,
      )
      if (
        next.scale !== zoomRef.current.scale
        || next.x !== zoomRef.current.x
        || next.y !== zoomRef.current.y
      ) {
        setZoom(next)
      }
    }
    clamp()
    const observer = new ResizeObserver(clamp)
    observer.observe(canvas)
    return () => observer.disconnect()
  }, [current, measure, setZoom])

  useEffect(() => {
    if (count < 2) {
      return
    }
    const sources = [
      images[(current + 1) % count],
      images[(current - 1 + count) % count],
    ].filter((source): source is string => Boolean(source))
    const preloads = sources.map((source) => {
      const image = new Image()
      image.decoding = 'async'
      image.src = source
      return image
    })
    return () => {
      preloads.forEach((image) => {
        image.src = ''
      })
    }
  }, [count, current, images])

  useEffect(() => {
    const strip = stripRef.current
    if (!strip) {
      return
    }
    const thumb = strip.querySelectorAll<HTMLButtonElement>(
      '.image-viewer-thumb',
    )[current]
    if (!thumb) {
      return
    }
    const reduceMotion =
      typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches
    strip.scrollTo({
      left:
        thumb.offsetLeft
        - strip.clientWidth / 2
        + thumb.clientWidth / 2,
      behavior: reduceMotion ? 'auto' : 'smooth',
    })
  }, [current])

  const onPointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.pointerType === 'mouse' && event.button !== 0) {
      return
    }
    const point = { x: event.clientX, y: event.clientY }
    pointersRef.current.set(event.pointerId, point)
    try {
      event.currentTarget.setPointerCapture(event.pointerId)
    } catch {
      // Pointer capture is best effort; gestures still work without it.
    }
    setGrabbing(true)

    if (pointersRef.current.size === 1) {
      tapRef.current = {
        id: event.pointerId,
        x: point.x,
        y: point.y,
        time: performance.now(),
        moved: false,
      }
      panRef.current = {
        id: event.pointerId,
        startX: point.x,
        startY: point.y,
        offsetX: zoomRef.current.x,
        offsetY: zoomRef.current.y,
      }
      pinchRef.current = null
      return
    }

    const points = Array.from(pointersRef.current.values()).slice(0, 2)
    if (points.length < 2) {
      return
    }
    const midpoint = pointMidpoint(points[0], points[1])
    tapRef.current = null
    panRef.current = null
    pinchRef.current = {
      distance: Math.max(1, pointDistance(points[0], points[1])),
      scale: zoomRef.current.scale,
      midX: midpoint.x,
      midY: midpoint.y,
      offsetX: zoomRef.current.x,
      offsetY: zoomRef.current.y,
    }
  }

  const onPointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!pointersRef.current.has(event.pointerId)) {
      return
    }
    pointersRef.current.set(event.pointerId, {
      x: event.clientX,
      y: event.clientY,
    })
    const points = Array.from(pointersRef.current.values())
    const pinch = pinchRef.current

    if (points.length >= 2 && pinch) {
      const canvas = canvasRef.current
      const geometry = measure()
      if (!canvas || !geometry) {
        return
      }
      const rect = canvas.getBoundingClientRect()
      const [first, second] = points
      const nextScale = clampImageZoom(
        pinch.scale * (pointDistance(first, second) / pinch.distance),
      )
      const midpoint = pointMidpoint(first, second)
      const centerX = rect.width / 2
      const centerY = rect.height / 2
      const startMidX = pinch.midX - rect.left
      const startMidY = pinch.midY - rect.top
      const imagePointX =
        (startMidX - centerX - pinch.offsetX) / pinch.scale
      const imagePointY =
        (startMidY - centerY - pinch.offsetY) / pinch.scale
      const next = clampImageOffset(
        {
          scale: nextScale,
          x:
            midpoint.x
            - rect.left
            - centerX
            - imagePointX * nextScale,
          y:
            midpoint.y
            - rect.top
            - centerY
            - imagePointY * nextScale,
        },
        geometry.width,
        geometry.height,
        geometry.imageWidth,
        geometry.imageHeight,
      )
      setZoom(next)
      if (tapRef.current) {
        tapRef.current.moved = true
      }
      return
    }

    const pan = panRef.current
    if (!pan || pan.id !== event.pointerId) {
      return
    }
    const dx = event.clientX - pan.startX
    const dy = event.clientY - pan.startY
    if (tapRef.current && (Math.abs(dx) > 4 || Math.abs(dy) > 4)) {
      tapRef.current.moved = true
    }
    if (zoomRef.current.scale <= MIN_IMAGE_ZOOM + 0.01) {
      return
    }
    const geometry = measure()
    if (!geometry) {
      return
    }
    setZoom(
      clampImageOffset(
        {
          scale: zoomRef.current.scale,
          x: pan.offsetX + dx,
          y: pan.offsetY + dy,
        },
        geometry.width,
        geometry.height,
        geometry.imageWidth,
        geometry.imageHeight,
      ),
    )
  }

  const onPointerUp = (event: ReactPointerEvent<HTMLDivElement>) => {
    pointersRef.current.delete(event.pointerId)
    try {
      event.currentTarget.releasePointerCapture(event.pointerId)
    } catch {
      // The pointer may already have been released by the browser.
    }

    if (pointersRef.current.size > 0) {
      const [remainingId, remaining] = Array.from(
        pointersRef.current.entries(),
      )[0]
      panRef.current = {
        id: remainingId,
        startX: remaining.x,
        startY: remaining.y,
        offsetX: zoomRef.current.x,
        offsetY: zoomRef.current.y,
      }
      pinchRef.current = null
      tapRef.current = null
      setGrabbing(false)
      return
    }

    const tap = tapRef.current
    if (tap && tap.id === event.pointerId) {
      const dx = event.clientX - tap.x
      const dy = event.clientY - tap.y
      const moved = tap.moved || Math.abs(dx) > 4 || Math.abs(dy) > 4
      if (!moved && performance.now() - tap.time < 450) {
        toggleZoom(event.clientX, event.clientY)
      } else if (
        zoomRef.current.scale <= MIN_IMAGE_ZOOM + 0.01
        && Math.abs(dx) >= 50
        && Math.abs(dx) > Math.abs(dy) * 1.2
      ) {
        goTo(dx < 0 ? 1 : -1)
      }
    }

    tapRef.current = null
    panRef.current = null
    pinchRef.current = null
    setGrabbing(false)
  }

  const onPointerCancel = (event: ReactPointerEvent<HTMLDivElement>) => {
    pointersRef.current.delete(event.pointerId)
    if (pointersRef.current.size === 0) {
      tapRef.current = null
      panRef.current = null
      pinchRef.current = null
      setGrabbing(false)
      return
    }
    const [remainingId, remaining] = Array.from(
      pointersRef.current.entries(),
    )[0]
    panRef.current = {
      id: remainingId,
      startX: remaining.x,
      startY: remaining.y,
      offsetX: zoomRef.current.x,
      offsetY: zoomRef.current.y,
    }
    pinchRef.current = null
    tapRef.current = null
    setGrabbing(false)
  }

  const image = images[current]
  const isZoomed = zoom.scale > MIN_IMAGE_ZOOM + 0.01

  return createPortal(
    <div
      className={`image-viewer${closing ? ' is-closing' : ''}`}
      role="dialog"
      aria-modal="true"
      aria-label={t('product.images')}
      ref={panelRef}
      tabIndex={-1}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          requestClose()
        }
      }}
    >
      <header className="image-viewer-head">
        <div className="image-viewer-title">
          <p className="image-viewer-kicker">{t('product.images')}</p>
          <h2 title={title}>{title}</h2>
        </div>
        <div className="image-viewer-tools">
          <span className="image-viewer-counter" aria-live="polite">
            {t('product.imageCounter', {
              current: current + 1,
              count,
            })}
          </span>
          <div
            className="image-viewer-zoom-controls"
            role="group"
            aria-label={t('product.zoom')}
          >
            <button
              type="button"
              className="image-viewer-icon-button"
              aria-label={t('product.zoomOut')}
              disabled={zoom.scale <= MIN_IMAGE_ZOOM + 0.01}
              onClick={() => zoomToCenter(zoomRef.current.scale - 0.5)}
            >
              <Icon name="zoom-out" />
            </button>
            <button
              type="button"
              className="image-viewer-zoom-value"
              aria-label={t('product.resetZoom')}
              title={t('product.resetZoom')}
              aria-live="polite"
              onClick={resetZoom}
            >
              {zoomPercent(zoom.scale)}%
            </button>
            <button
              type="button"
              className="image-viewer-icon-button"
              aria-label={t('product.zoomIn')}
              disabled={zoom.scale >= MAX_IMAGE_ZOOM - 0.01}
              onClick={() => zoomToCenter(zoomRef.current.scale + 0.5)}
            >
              <Icon name="zoom-in" />
            </button>
          </div>
          <button
            type="button"
            className="image-viewer-close"
            aria-label={t('nav.close')}
            onClick={requestClose}
          >
            <Icon name="close" />
          </button>
        </div>
      </header>

      <div className="image-viewer-stage">
        {count > 1 ? (
          <button
            type="button"
            className="image-viewer-nav is-previous"
            aria-label={t('product.previousImage')}
            onClick={() => goTo(-1)}
          >
            <Icon name="arrow-left" />
          </button>
        ) : null}

        <div className="image-viewer-frame">
          <div
            className={`image-viewer-canvas${isZoomed ? ' is-zoomed' : ''}${
              grabbing ? ' is-grabbing' : ''
            }`}
            ref={canvasRef}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerCancel}
            onDragStart={(event) => event.preventDefault()}
          >
            {!loaded && image ? (
              <span
                className="skeleton image-viewer-skeleton"
                aria-hidden="true"
              />
            ) : null}
            <ProductImage
              key={current}
              className="image-viewer-image"
              style={{
                transform: `translate3d(${zoom.x}px, ${zoom.y}px, 0) scale(${zoom.scale})`,
              }}
              src={image}
              alt={t('product.imageAlt', {
                title,
                index: current + 1,
                count,
              })}
              eager
              onLoad={() => setLoaded(true)}
              onError={() => setLoaded(true)}
            />
          </div>
        </div>

        {count > 1 ? (
          <button
            type="button"
            className="image-viewer-nav is-next"
            aria-label={t('product.nextImage')}
            onClick={() => goTo(1)}
          >
            <Icon name="arrow-right" />
          </button>
        ) : null}
      </div>

      <footer className="image-viewer-foot">
        {count > 1 ? (
          <div
            className="image-viewer-strip"
            ref={stripRef}
            role="group"
            aria-label={t('product.imageStrip')}
          >
            {images.map((source, index) => (
              <button
                key={`${source}-${index}`}
                type="button"
                className={`image-viewer-thumb${
                  index === current ? ' is-active' : ''
                }`}
                aria-pressed={index === current}
                aria-label={t('product.imageN', { index: index + 1 })}
                onClick={() => setCurrent(index)}
              >
                <ProductImage src={source} alt="" />
              </button>
            ))}
          </div>
        ) : null}
        <p className="image-viewer-hint">
          <span className="image-viewer-hint-pointer">
            {t('product.zoomHint')}
          </span>
          <span className="image-viewer-hint-touch">
            {t('product.zoomHintTouch')}
          </span>
        </p>
      </footer>
    </div>,
    document.body,
  )
}
