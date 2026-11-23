import { useEffect, useState, type ReactNode } from 'react'

export type IconName =
  | 'search'
  | 'arrow-left'
  | 'arrow-right'
  | 'more'
  | 'plus'
  | 'expand'
  | 'external'
  | 'star'
  | 'sun'
  | 'moon'
  | 'globe'
  | 'map-pin'
  | 'close'
  | 'check'
  | 'info'
  | 'chevron-down'
  | 'package'
  | 'alert'
  | 'spark'
  | 'refresh'
  | 'bookmark'
  | 'bookmark-filled'
  | 'folder'
  | 'clock'
  | 'activity'
  | 'play'
  | 'stop'
  | 'compare'
  | 'settings'
  | 'sliders'
  | 'panel-left'
  | 'pin'
  | 'filter'
  | 'tag'
  | 'trash'
  | 'list'
  | 'grid'

export function Icon({
  name,
  className,
}: {
  name: IconName
  className?: string
}) {
  const common = {
    className,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.7,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
  }

  switch (name) {
    case 'search':
      return (
        <svg {...common}>
          <circle cx="11" cy="11" r="6.5" />
          <path d="m16 16 4.5 4.5" />
        </svg>
      )
    case 'arrow-left':
      return (
        <svg {...common}>
          <path d="M19 12H5" />
          <path d="m11 6-6 6 6 6" />
        </svg>
      )
    case 'arrow-right':
      return (
        <svg {...common}>
          <path d="M5 12h14" />
          <path d="m13 6 6 6-6 6" />
        </svg>
      )
    case 'more':
      return (
        <svg {...common} fill="currentColor" stroke="none">
          <circle cx="5.5" cy="12" r="1.4" />
          <circle cx="12" cy="12" r="1.4" />
          <circle cx="18.5" cy="12" r="1.4" />
        </svg>
      )
    case 'plus':
      return (
        <svg {...common}>
          <path d="M12 5v14M5 12h14" />
        </svg>
      )
    case 'expand':
      return (
        <svg {...common}>
          <path d="M14 4h6v6" />
          <path d="M20 4l-7 7" />
          <path d="M10 20H4v-6" />
          <path d="M4 20l7-7" />
        </svg>
      )
    case 'external':
      return (
        <svg {...common}>
          <path d="M14 4h6v6" />
          <path d="M20 4 11 13" />
          <path d="M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5" />
        </svg>
      )
    case 'star':
      return (
        <svg
          className={className}
          viewBox="0 0 24 24"
          fill="currentColor"
          aria-hidden="true"
        >
          <path d="m12 2.8 2.8 5.7 6.3.9-4.6 4.5 1.1 6.3-5.6-3-5.6 3 1.1-6.3L2.9 9.4l6.3-.9L12 2.8Z" />
        </svg>
      )
    case 'sun':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="3.7" />
          <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
        </svg>
      )
    case 'moon':
      return (
        <svg {...common}>
          <path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z" />
        </svg>
      )
    case 'globe':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="8.5" />
          <path d="M3.8 12h16.4M12 3.5c2.2 2.3 3.3 5.1 3.3 8.5s-1.1 6.2-3.3 8.5c-2.2-2.3-3.3-5.1-3.3-8.5S9.8 5.8 12 3.5Z" />
        </svg>
      )
    case 'map-pin':
      return (
        <svg {...common}>
          <path d="M20 10c0 5-8 11-8 11S4 15 4 10a8 8 0 1 1 16 0Z" />
          <circle cx="12" cy="10" r="2.5" />
        </svg>
      )
    case 'close':
      return (
        <svg {...common}>
          <path d="m6 6 12 12M18 6 6 18" />
        </svg>
      )
    case 'check':
      return (
        <svg {...common}>
          <path d="m5 12.5 4.5 4.5L19 7" />
        </svg>
      )
    case 'info':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="9" />
          <path d="M12 11v5" />
          <path d="M12 8h.01" />
        </svg>
      )
    case 'chevron-down':
      return (
        <svg {...common}>
          <path d="m6 9 6 6 6-6" />
        </svg>
      )
    case 'package':
      return (
        <svg {...common}>
          <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z" />
          <path d="m4.4 7.7 7.6 4.4 7.6-4.4M12 12.1V21" />
        </svg>
      )
    case 'alert':
      return (
        <svg {...common}>
          <path d="M12 3 2.8 20h18.4L12 3Z" />
          <path d="M12 9v4" />
          <path d="M12 16.5h.01" />
        </svg>
      )
    case 'spark':
      return (
        <svg {...common}>
          <path d="M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M18.4 5.6l-2.8 2.8M8.4 15.6l-2.8 2.8" />
        </svg>
      )
    case 'bookmark':
      return (
        <svg {...common}>
          <path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21l-6-3.5L6 21V4.5Z" />
        </svg>
      )
    case 'bookmark-filled':
      return (
        <svg {...common} fill="currentColor">
          <path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21l-6-3.5L6 21V4.5Z" />
        </svg>
      )
    case 'folder':
      return (
        <svg {...common}>
          <path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v7A2.5 2.5 0 0 1 18.5 19h-13A2.5 2.5 0 0 1 3 16.5v-9Z" />
        </svg>
      )
    case 'clock':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="8.5" />
          <path d="M12 7.5V12l3 2" />
        </svg>
      )
    case 'activity':
      return (
        <svg {...common}>
          <path d="M3 12h4l2.2-5 4 10 2.3-5H21" />
        </svg>
      )
    case 'play':
      return (
        <svg {...common} fill="currentColor" stroke="none">
          <path d="M8 5.4v13.2L19 12 8 5.4Z" />
        </svg>
      )
    case 'stop':
      return (
        <svg {...common} fill="currentColor" stroke="none">
          <rect x="6" y="6" width="12" height="12" rx="1.2" />
        </svg>
      )
    case 'compare':
      return (
        <svg {...common}>
          <rect x="3.5" y="4" width="7" height="16" />
          <rect x="13.5" y="4" width="7" height="16" />
          <path d="M6 8h2M16 8h2M6 12h2M16 12h2" />
        </svg>
      )
    case 'settings':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
        </svg>
      )
    case 'sliders':
      return (
        <svg {...common}>
          <path d="M4 7h7M15 7h5M4 12h12M20 12h0M4 17h3M11 17h9" />
          <circle cx="13" cy="7" r="2" />
          <circle cx="18" cy="12" r="2" />
          <circle cx="9" cy="17" r="2" />
        </svg>
      )
    case 'panel-left':
      return (
        <svg {...common}>
          <rect x="3.5" y="4.5" width="17" height="15" rx="1.5" />
          <path d="M9 4.5v15" />
        </svg>
      )
    case 'pin':
      return (
        <svg {...common}>
          <path d="M14 4.5 19.5 10l-2.2 2.2-1.1-.6-2.7 2.7.2 3.2-1.4 1.4-2.2-2.2-3.6 3.6-1.5-1.5 3.6-3.6-2.2-2.2 1.4-1.4 3.2.2 2.7-2.7-.6-1.1L14 4.5Z" />
        </svg>
      )
    case 'trash':
      return (
        <svg {...common}>
          <path d="M4 7h16" />
          <path d="M9 7V4.5h6V7" />
          <path d="m6.5 7 .8 12.5h9.4L17.5 7" />
          <path d="M10 11v5M14 11v5" />
        </svg>
      )
    case 'list':
      return (
        <svg {...common}>
          <path d="M8 6h12M8 12h12M8 18h12" />
          <path d="M4 6h.01M4 12h.01M4 18h.01" />
        </svg>
      )
    case 'grid':
      return (
        <svg {...common}>
          <rect x="3.5" y="3.5" width="7" height="7" />
          <rect x="13.5" y="3.5" width="7" height="7" />
          <rect x="3.5" y="13.5" width="7" height="7" />
          <rect x="13.5" y="13.5" width="7" height="7" />
        </svg>
      )
    case 'filter':
      return (
        <svg {...common}>
          <path d="M4 7h16M4 12h16M4 17h16" />
          <circle cx="9" cy="7" r="2" fill="currentColor" stroke="none" />
          <circle cx="15" cy="12" r="2" fill="currentColor" stroke="none" />
          <circle cx="11" cy="17" r="2" fill="currentColor" stroke="none" />
        </svg>
      )
    case 'tag':
      return (
        <svg {...common}>
          <path d="M3.5 12.2V5.5A2 2 0 0 1 5.5 3.5h6.7a2 2 0 0 1 1.4.6l6.3 6.3a2 2 0 0 1 0 2.8l-6.7 6.7a2 2 0 0 1-2.8 0l-6.3-6.3a2 2 0 0 1-.6-1.4Z" />
          <circle cx="8.3" cy="8.3" r="1.2" />
        </svg>
      )
    case 'refresh':
      return (
        <svg {...common}>
          <path d="M20 11a8 8 0 0 0-14.9-3M4 4v4h4" />
          <path d="M4 13a8 8 0 0 0 14.9 3M20 20v-4h-4" />
        </svg>
      )
  }
}

export type TagTone = 'neutral' | 'accent' | 'blue' | 'rust' | 'gold' | 'quiet'

export function Tag({
  tone = 'neutral',
  children,
}: {
  tone?: TagTone
  children: ReactNode
}) {
  return <span className={`tag tag-${tone}`}>{children}</span>
}

export function RatingStars({
  value,
  count,
}: {
  value: number
  count?: string
}) {
  if (!value) {
    return null
  }

  const rounded = Math.round(value)
  return (
    <span className="rating" aria-label={`${value} out of 5 stars`}>
      <span className="rating-stars" aria-hidden="true">
        {Array.from({ length: 5 }, (_, index) => (
          <Icon
            key={index}
            name="star"
            className={index < rounded ? 'star-on' : 'star-off'}
          />
        ))}
      </span>
      {count ? <span className="rating-count">{count}</span> : null}
    </span>
  )
}

export function ProductImage({
  src,
  alt,
  className,
  eager = false,
}: {
  src?: string | null
  alt: string
  className?: string
  eager?: boolean
}) {
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setFailed(false)
  }, [src])

  if (!src || failed) {
    const initials = alt
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0])
      .join('')
      .toUpperCase()
    return (
      <span className={`image-fallback ${className ?? ''}`} aria-hidden="true">
        {initials || <Icon name="package" />}
      </span>
    )
  }

  return (
    <img
      className={className}
      src={src}
      alt={alt}
      loading={eager ? 'eager' : 'lazy'}
      decoding="async"
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
    />
  )
}

export function SkeletonRows({ count = 5 }: { count?: number }) {
  return (
    <div className="skeleton-list" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="skeleton-row" key={index}>
          <span className="skeleton skeleton-index" />
          <span className="skeleton skeleton-image" />
          <span className="skeleton-copy">
            <span className="skeleton skeleton-title" />
            <span className="skeleton skeleton-line" />
            <span className="skeleton skeleton-line short" />
          </span>
          <span className="skeleton skeleton-price" />
        </div>
      ))}
    </div>
  )
}

export function SkeletonGrid({ count = 8 }: { count?: number }) {
  return (
    <div className="skeleton-grid" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="skeleton-card" key={index}>
          <span className="skeleton skeleton-card-image" />
          <span className="skeleton-card-body">
            <span className="skeleton skeleton-card-tags" />
            <span className="skeleton skeleton-card-title" />
            <span className="skeleton skeleton-card-line" />
            <span className="skeleton skeleton-card-price" />
          </span>
        </div>
      ))}
    </div>
  )
}
