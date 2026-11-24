export const MIN_IMAGE_ZOOM = 1
export const MAX_IMAGE_ZOOM = 4
export const QUICK_IMAGE_ZOOM = 2.5

export type ImageZoomState = {
  scale: number
  x: number
  y: number
}

export function clampNumber(
  value: number,
  minimum: number,
  maximum: number,
): number {
  return Math.min(Math.max(value, minimum), maximum)
}

export function clampImageZoom(scale: number): number {
  return clampNumber(scale, MIN_IMAGE_ZOOM, MAX_IMAGE_ZOOM)
}

export function clampImageOffset(
  value: ImageZoomState,
  viewportWidth: number,
  viewportHeight: number,
  imageWidth: number,
  imageHeight: number,
): ImageZoomState {
  const maxX = Math.max(0, (imageWidth * value.scale - viewportWidth) / 2)
  const maxY = Math.max(0, (imageHeight * value.scale - viewportHeight) / 2)
  return {
    scale: value.scale,
    x: clampNumber(value.x, -maxX, maxX),
    y: clampNumber(value.y, -maxY, maxY),
  }
}

export function zoomImageAtPoint(
  current: ImageZoomState,
  nextScale: number,
  pointX: number,
  pointY: number,
  viewportWidth: number,
  viewportHeight: number,
  imageWidth: number,
  imageHeight: number,
): ImageZoomState {
  const scale = clampImageZoom(nextScale)
  if (scale <= MIN_IMAGE_ZOOM + 0.001) {
    return { scale: MIN_IMAGE_ZOOM, x: 0, y: 0 }
  }

  const centerX = viewportWidth / 2
  const centerY = viewportHeight / 2
  const ratio = scale / current.scale
  const x = pointX - centerX - (pointX - centerX - current.x) * ratio
  const y = pointY - centerY - (pointY - centerY - current.y) * ratio

  return clampImageOffset(
    { scale, x, y },
    viewportWidth,
    viewportHeight,
    imageWidth,
    imageHeight,
  )
}

export function zoomPercent(scale: number): number {
  return Math.round(scale * 100)
}
