import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type PointerEvent,
} from 'react'
import { useI18n } from '../i18n/I18nProvider'
import { ProductImage } from './ui'

export function ProductGallery({
  images,
  title,
  activeIndex,
  onSelect,
}: {
  images: string[]
  title: string
  activeIndex: number
  onSelect: (index: number) => void
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
  const shellRef = useRef<HTMLDivElement>(null)
  const thumbsRef = useRef<HTMLDivElement>(null)
  const suppressPreviewRef = useRef(0)
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
    if (Math.abs(dx) < 44 || Math.abs(dx) < Math.abs(dy) * 1.2) {
      return
    }
    const step = dx < 0 ? 1 : -1
    const next = (safeActive + step + images.length) % images.length
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
        onDragStart={(event) => event.preventDefault()}
      >
        <div
          className={`product-main-image-frame${
            slideFrom ? ` is-from-${slideFrom}` : ''
          }`}
          key={`${current}-${images[current] ?? ''}`}
          onAnimationEnd={() => setSlideFrom(null)}
        >
          <ProductImage src={images[current]} alt={title} eager />
        </div>
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
    </div>
  )
}
