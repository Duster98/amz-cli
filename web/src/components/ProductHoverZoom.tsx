import {
  useLayoutEffect,
  useRef,
  useState,
  type RefObject,
} from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/I18nProvider'
import { ProductImage } from './ui'

export type HoverZoomBounds = {
  left: number
  top: number
  width: number
  height: number
}

export function ProductHoverZoom({
  image,
  index,
  count,
  bounds,
  panelRef,
}: {
  image: string
  index: number
  count: number
  bounds: HoverZoomBounds
  panelRef: RefObject<HTMLDivElement | null>
}) {
  const { t } = useI18n()
  const stageRef = useRef<HTMLDivElement>(null)
  const [canvasSize, setCanvasSize] = useState(0)

  useLayoutEffect(() => {
    const stage = stageRef.current
    if (!stage) {
      return
    }
    const measure = () => {
      const style = window.getComputedStyle(stage)
      const availableWidth =
        stage.clientWidth
        - parseFloat(style.paddingLeft || '0')
        - parseFloat(style.paddingRight || '0')
      const availableHeight =
        stage.clientHeight
        - parseFloat(style.paddingTop || '0')
        - parseFloat(style.paddingBottom || '0')
      setCanvasSize(
        Math.max(
          1,
          Math.floor(Math.min(availableWidth, availableHeight)),
        ),
      )
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    return () => observer.disconnect()
  }, [bounds.height, bounds.width])

  return createPortal(
    <div
      ref={panelRef}
      className="product-hover-zoom"
      style={{
        left: bounds.left,
        top: bounds.top,
        width: bounds.width,
        height: bounds.height,
      }}
      aria-hidden="true"
    >
      <div className="product-hover-zoom-head">
        <span className="product-hover-zoom-kicker">{t('product.zoom')}</span>
        <span className="mono">
          {String(index + 1).padStart(2, '0')} /{' '}
          {String(count).padStart(2, '0')}
        </span>
      </div>
      <div className="product-hover-zoom-stage" ref={stageRef}>
        <div
          className="product-hover-zoom-canvas"
          style={
            canvasSize > 0
              ? { width: canvasSize, height: canvasSize }
              : undefined
          }
        >
          <ProductImage
            className="product-hover-zoom-image"
            src={image}
            alt=""
            eager
          />
        </div>
      </div>
      <div className="product-hover-zoom-foot">
        <span>{t('product.hoverZoomHint')}</span>
        <span>{t('product.openImageHint')}</span>
      </div>
    </div>,
    document.body,
  )
}
