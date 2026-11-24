import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type MouseEvent,
  type PointerEvent,
  type RefObject,
} from 'react'
import { useI18n } from '../i18n/I18nProvider'
import { clampNumber, QUICK_IMAGE_ZOOM } from '../lib/imageZoom'
import { Icon, ProductImage } from './ui'
import {
  ProductHoverZoom,
  type HoverZoomBounds,
} from './ProductHoverZoom'

export function ProductGallery({
  images,
  title,
  activeIndex,
  onSelect,
  onOpenImage,
  zoomTargetRef,
}: {
  images: string[]
  title: string
  activeIndex: number
  onSelect: (index: number) => void
  onOpenImage: (index: number, trigger: HTMLElement) => void
  zoomTargetRef: RefObject<HTMLDivElement | null>
}) {
  const { t } = useI18n()
  const [previewIndex, setPreviewIndex] = useState<number | null>(null)
  const [edges, setEdges] = useState({
    top: false,
    bottom: false,
    left: false,
    right: false,
  })
  const [slideFrom, setSlideFrom] = useState<'left' | 'right' | null>(null)
  const [hoverBounds, setHoverBounds] = useState<HoverZoomBounds | null>(null)
  const shellRef = useRef<HTMLDivElement>(null)
  const thumbsRef = useRef<HTMLDivElement>(null)
  const mainImageRef = useRef<HTMLButtonElement>(null)
  const lensRef = useRef<HTMLSpanElement>(null)
  const zoomPanelRef = useRef<HTMLDivElement>(null)
  const suppressPreviewRef = useRef(0)
  const suppressClickRef = useRef(0)
  const hoverPointerRef = useRef<{ x: number; y: number } | null>(null)
  const swipeRef = useRef<{
    id: number
    x: number
    y: number
  } | null>(null)
  const safeActive = Math.min(Math.max(activeIndex, 0), Math.max(images.length - 1, 0))
  const current =
    previewIndex === null
      ? safeActive
      : Math.min(Math.max(previewIndex, 0), Math.max(images.length - 1, 0))

  const suppressPreview = useCallback(() => {
    suppressPreviewRef.current = Date.now() + 240
    setPreviewIndex(null)
  }, [])

  const goTo = useCallback(
    (index: number, from: 'left' | 'right' | null) => {
      setPreviewIndex(null)
      setSlideFrom(from)
      onSelect(index)
    },
    [onSelect],
  )

  const selectImage = (index: number) => {
    const from =
      index > safeActive ? 'right' : index < safeActive ? 'left' : null
    goTo(index, from)
  }

  const canHoverZoom = useCallback(() => {
    if (images.length === 0) {
      return false
    }
    return (
      window.matchMedia('(min-width: 1200px)').matches
      && window.matchMedia('(hover: hover) and (pointer: fine)').matches
    )
  }, [images.length])

  const updateHoverPaint = useCallback((clientX: number, clientY: number) => {
    const image = mainImageRef.current
    if (!image) {
      return
    }
    const rect = image.getBoundingClientRect()
    if (rect.width <= 0 || rect.height <= 0) {
      return
    }
    const x = clampNumber((clientX - rect.left) / rect.width, 0, 1)
    const y = clampNumber((clientY - rect.top) / rect.height, 0, 1)
    const lensX = clampNumber(x, 0.17, 0.83) * 100
    const lensY = clampNumber(y, 0.17, 0.83) * 100
    if (lensRef.current) {
      lensRef.current.style.setProperty('--lens-x', `${lensX}%`)
      lensRef.current.style.setProperty('--lens-y', `${lensY}%`)
    }
    if (zoomPanelRef.current) {
      zoomPanelRef.current.style.setProperty(
        '--zoom-scale',
        String(QUICK_IMAGE_ZOOM),
      )
      zoomPanelRef.current.style.setProperty(
        '--zoom-shift-x',
        `${(0.5 - x) * 100}%`,
      )
      zoomPanelRef.current.style.setProperty(
        '--zoom-shift-y',
        `${(0.5 - y) * 100}%`,
      )
    }
  }, [])

  const openHoverZoom = (clientX: number, clientY: number) => {
    const target = zoomTargetRef.current
    if (!target || !canHoverZoom()) {
      return
    }
    hoverPointerRef.current = { x: clientX, y: clientY }
    const rect = target.getBoundingClientRect()
    setHoverBounds({
      left: rect.left,
      top: rect.top,
      width: rect.width,
      height: rect.height,
    })
  }

  const closeHoverZoom = () => {
    hoverPointerRef.current = null
    setHoverBounds(null)
  }

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (images.length <= 1) {
      return
    }
    if (event.pointerType === 'mouse' && event.button !== 0) {
      return
    }
    if ((event.target as HTMLElement).closest('.gallery-dot')) {
      return
    }
    swipeRef.current = {
      id: event.pointerId,
      x: event.clientX,
      y: event.clientY,
    }
    try {
      event.currentTarget.setPointerCapture(event.pointerId)
    } catch {
      // Pointer capture is best effort; swipe still works without it.
    }
  }

  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    const start = swipeRef.current
    if (!start || start.id !== event.pointerId) {
      return
    }
    swipeRef.current = null
    const dx = event.clientX - start.x
    const dy = event.clientY - start.y
    if (Math.abs(dx) > 8 || Math.abs(dy) > 8) {
      suppressClickRef.current = Date.now() + 260
    }
    if (Math.abs(dx) < 44 || Math.abs(dx) < Math.abs(dy) * 1.2) {
      return
    }
    const step = dx < 0 ? 1 : -1
    const next = (safeActive + step + images.length) % images.length
    closeHoverZoom()
    goTo(next, dx < 0 ? 'right' : 'left')
  }

  const onPointerCancel = (event: PointerEvent<HTMLDivElement>) => {
    if (swipeRef.current?.id === event.pointerId) {
      swipeRef.current = null
    }
  }

  const updateEdges = useCallback(() => {
    const element = thumbsRef.current
    if (!element) {
      return
    }
    setEdges({
      top: element.scrollTop > 1,
      bottom:
        element.scrollTop + element.clientHeight < element.scrollHeight - 1,
      left: element.scrollLeft > 1,
      right:
        element.scrollLeft + element.clientWidth < element.scrollWidth - 1,
    })
  }, [])

  useEffect(() => {
    setPreviewIndex(null)
  }, [activeIndex, images.length])

  useEffect(() => {
    if (images.length <= 1) {
      return
    }
    const neighbors = [
      images[(safeActive + 1) % images.length],
      images[(safeActive - 1 + images.length) % images.length],
    ]
    const preloads = neighbors
      .filter((src): src is string => Boolean(src))
      .map((src) => {
        const image = new Image()
        image.decoding = 'async'
        image.src = src
        return image
      })
    return () => {
      preloads.forEach((image) => {
        image.src = ''
      })
    }
  }, [images, safeActive])

  useEffect(() => {
    const element = thumbsRef.current
    if (!element) {
      return
    }
    updateEdges()
    const onScroll = () => {
      updateEdges()
      suppressPreview()
    }
    element.addEventListener('scroll', onScroll, { passive: true })
    const observer = new ResizeObserver(updateEdges)
    observer.observe(element)
    return () => {
      element.removeEventListener('scroll', onScroll)
      observer.disconnect()
    }
  }, [images.length, suppressPreview, updateEdges])

  useEffect(() => {
    const shell = shellRef.current
    if (!shell) {
      return
    }

    const onWheel = (event: WheelEvent) => {
      const element = thumbsRef.current
      if (!element) {
        return
      }
      const vertical = element.scrollHeight > element.clientHeight + 1
      const horizontal = element.scrollWidth > element.clientWidth + 1
      if (!vertical && !horizontal) {
        return
      }

      event.preventDefault()
      suppressPreview()
      if (vertical) {
        element.scrollTop += event.deltaY
      } else {
        element.scrollLeft += event.deltaY || event.deltaX
      }
    }

    shell.addEventListener('wheel', onWheel, { passive: false })
    return () => shell.removeEventListener('wheel', onWheel)
  }, [images.length, suppressPreview])

  useLayoutEffect(() => {
    if (!hoverBounds) {
      return
    }
    const pointer = hoverPointerRef.current
    if (pointer) {
      updateHoverPaint(pointer.x, pointer.y)
    }
  }, [hoverBounds, updateHoverPaint])

  useEffect(() => {
    if (!hoverBounds) {
      return
    }
    const close = () => setHoverBounds(null)
    window.addEventListener('scroll', close, { passive: true })
    window.addEventListener('resize', close)
    return () => {
      window.removeEventListener('scroll', close)
      window.removeEventListener('resize', close)
    }
  }, [hoverBounds])

  useEffect(() => {
    if (images.length === 0) {
      setHoverBounds(null)
    }
  }, [images.length])

  const openViewer = (event: MouseEvent<HTMLDivElement>) => {
    if (Date.now() < suppressClickRef.current) {
      event.preventDefault()
      return
    }
    if ((event.target as HTMLElement).closest('.gallery-dot')) {
      return
    }
    closeHoverZoom()
    onOpenImage(current, mainImageRef.current ?? event.currentTarget)
  }

  return (
    <div className={`product-gallery${images.length <= 1 ? ' is-single' : ''}`}>
      {images.length > 1 ? (
        <div className="product-thumbs-shell" ref={shellRef}>
          {edges.top ? (
            <span
              className="thumbs-shadow thumbs-shadow-top"
              aria-hidden="true"
            />
          ) : null}
          {edges.bottom ? (
            <span
              className="thumbs-shadow thumbs-shadow-bottom"
              aria-hidden="true"
            />
          ) : null}
          {edges.left ? (
            <span
              className="thumbs-shadow thumbs-shadow-left"
              aria-hidden="true"
            />
          ) : null}
          {edges.right ? (
            <span
              className="thumbs-shadow thumbs-shadow-right"
              aria-hidden="true"
            />
          ) : null}
          <div
            className="product-thumbs"
            ref={thumbsRef}
            role="group"
            aria-label={t('product.images')}
            onMouseLeave={() => setPreviewIndex(null)}
            onBlur={(event) => {
              if (
                !event.currentTarget.contains(
                  event.relatedTarget as Node | null,
                )
              ) {
                setPreviewIndex(null)
              }
            }}
          >
            {images.map((image, index) => (
              <button
                type="button"
                className={`product-thumb${index === safeActive ? ' is-active' : ''}${
                  index === current && index !== safeActive ? ' is-preview' : ''
                }`}
                key={`${image}-${index}`}
                aria-pressed={index === safeActive}
                aria-label={t('product.imageN', { index: index + 1 })}
                onMouseEnter={() => {
                  if (Date.now() >= suppressPreviewRef.current) {
                    setPreviewIndex(index)
                  }
                }}
                onFocus={() => setPreviewIndex(index)}
                onClick={() => selectImage(index)}
              >
                <ProductImage src={image} alt="" />
              </button>
            ))}
          </div>
        </div>
      ) : null}

      <div
        className="product-main-image"
        onPointerDown={onPointerDown}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerCancel}
        onClick={openViewer}
        onDragStart={(event) => event.preventDefault()}
      >
        <button
          type="button"
          className="product-main-image-frame"
          ref={mainImageRef}
          aria-label={t('product.openImageViewer')}
          aria-haspopup="dialog"
          onPointerEnter={(event) => {
            if (event.pointerType !== 'mouse') {
              return
            }
            openHoverZoom(event.clientX, event.clientY)
          }}
          onPointerMove={(event) => {
            if (hoverBounds) {
              hoverPointerRef.current = {
                x: event.clientX,
                y: event.clientY,
              }
              updateHoverPaint(event.clientX, event.clientY)
            }
          }}
          onPointerLeave={closeHoverZoom}
        >
          <span
            className={`product-main-image-media${
              slideFrom ? ` is-from-${slideFrom}` : ''
            }`}
            key={`${current}-${images[current] ?? ''}`}
            onAnimationEnd={() => setSlideFrom(null)}
          >
            <ProductImage src={images[current]} alt={title} eager />
          </span>
          <span className="product-main-image-expand" aria-hidden="true">
            <Icon name="expand" />
          </span>
          {hoverBounds ? (
            <span
              className="product-zoom-lens"
              ref={lensRef}
              aria-hidden="true"
            />
          ) : null}
        </button>
        {images.length > 1 ? (
          <div
            className="gallery-dots"
            role="group"
            aria-label={t('product.images')}
          >
            {images.map((_, index) => (
              <button
                key={index}
                type="button"
                className={`gallery-dot${
                  index === current ? ' is-active' : ''
                }`}
                aria-label={t('product.imageN', { index: index + 1 })}
                aria-pressed={index === current}
                onClick={() => selectImage(index)}
              />
            ))}
          </div>
        ) : null}
      </div>

      {hoverBounds && images[current] ? (
        <ProductHoverZoom
          image={images[current]}
          index={current}
          count={images.length}
          bounds={hoverBounds}
          panelRef={zoomPanelRef}
        />
      ) : null}
    </div>
  )
}
