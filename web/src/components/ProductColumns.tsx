import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { useI18n } from '../i18n/I18nProvider'

export function ProductColumns({
  description,
  specs,
}: {
  description: string[]
  specs: Record<string, string>
}) {
  const { t } = useI18n()
  const descriptionRef = useRef<HTMLDivElement>(null)
  const specsRef = useRef<HTMLDListElement>(null)
  const [collapsedHeight, setCollapsedHeight] = useState<number | null>(null)
  const [descriptionExpanded, setDescriptionExpanded] = useState(false)
  const [specsExpanded, setSpecsExpanded] = useState(false)
  const [descriptionOverflow, setDescriptionOverflow] = useState(false)
  const [specsOverflow, setSpecsOverflow] = useState(false)

  const hasDescription = description.length > 0
  const hasSpecs = Object.keys(specs).length > 0
  const hasBoth = hasDescription && hasSpecs

  useLayoutEffect(() => {
    const descriptionElement = descriptionRef.current
    const specsElement = specsRef.current
    if (!descriptionElement || !specsElement) {
      return
    }

    const measure = () => {
      const descriptionHeight = descriptionElement.scrollHeight
      const specsHeight = specsElement.scrollHeight
      const target = Math.min(descriptionHeight, specsHeight)
      setCollapsedHeight(target)
      if (!descriptionExpanded) {
        setDescriptionOverflow(descriptionHeight > target + 1)
      }
      if (!specsExpanded) {
        setSpecsOverflow(specsHeight > target + 1)
      }
    }

    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(descriptionElement)
    observer.observe(specsElement)
    return () => observer.disconnect()
  }, [descriptionExpanded, specsExpanded])

  if (!hasDescription && !hasSpecs) {
    return null
  }

  const descriptionClamped =
    hasBoth &&
    !descriptionExpanded &&
    descriptionOverflow &&
    collapsedHeight !== null
  const specsClamped =
    hasBoth && !specsExpanded && specsOverflow && collapsedHeight !== null

  return (
    <div className={`product-columns${hasBoth ? '' : ' is-single'}`}>
      {hasDescription ? (
        <section className="detail-section product-column">
          <div className="section-heading">
            <h2>{t('product.description')}</h2>
            {hasBoth && (descriptionOverflow || descriptionExpanded) ? (
              <button
                type="button"
                className="text-button column-toggle"
                aria-expanded={descriptionExpanded}
                onClick={() => setDescriptionExpanded((value) => !value)}
              >
                {descriptionExpanded
                  ? t('product.showLess')
                  : t('product.showMore')}
              </button>
            ) : null}
          </div>
          <div
            ref={descriptionRef}
            className={`description${descriptionClamped ? ' is-clamped' : ''}`}
            style={
              descriptionClamped
                ? ({
                    '--column-clamp': `${collapsedHeight}px`,
                  } as CSSProperties)
                : undefined
            }
          >
            {description.map((paragraph, index) => (
              <p key={`${paragraph.slice(0, 24)}-${index}`}>{paragraph}</p>
            ))}
          </div>
        </section>
      ) : null}

      {hasSpecs ? (
        <section className="detail-section product-column">
          <div className="section-heading">
            <h2>{t('product.specifications')}</h2>
            {hasBoth && (specsOverflow || specsExpanded) ? (
              <button
                type="button"
                className="text-button column-toggle"
                aria-expanded={specsExpanded}
                onClick={() => setSpecsExpanded((value) => !value)}
              >
                {specsExpanded
                  ? t('product.showLess')
                  : t('product.showMore')}
              </button>
            ) : null}
          </div>
          <dl
            ref={specsRef}
            className={`spec-list${specsClamped ? ' is-clamped' : ''}`}
            style={
              specsClamped
                ? ({
                    '--column-clamp': `${collapsedHeight}px`,
                  } as CSSProperties)
                : undefined
            }
          >
            {Object.entries(specs).map(([key, value]) => (
              <div key={key}>
                <dt>{key}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : null}
    </div>
  )
}
