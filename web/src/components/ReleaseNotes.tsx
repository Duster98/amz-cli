import type { ReactNode } from 'react'

type Block =
  | { type: 'heading'; level: number; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'list'; items: string[] }

function inline(text: string, keyPrefix: string): ReactNode[] {
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g)
  return parts
    .filter((part) => part.length > 0)
    .map((part, index) => {
      const key = `${keyPrefix}-${index}`
      if (part.startsWith('**') && part.endsWith('**')) {
        return <strong key={key}>{part.slice(2, -2)}</strong>
      }
      if (part.startsWith('`') && part.endsWith('`')) {
        return <code key={key}>{part.slice(1, -1)}</code>
      }
      return part
    })
}

function parse(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  const blocks: Block[] = []
  let paragraph: string[] = []
  let list: string[] = []

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ type: 'paragraph', text: paragraph.join(' ') })
      paragraph = []
    }
  }
  const flushList = () => {
    if (list.length > 0) {
      blocks.push({ type: 'list', items: list })
      list = []
    }
  }

  for (const raw of lines) {
    const line = raw.trim()
    if (line.length === 0) {
      flushParagraph()
      flushList()
      continue
    }
    const heading = /^(#{1,4})\s+(.*)$/.exec(line)
    if (heading) {
      flushParagraph()
      flushList()
      blocks.push({
        type: 'heading',
        level: heading[1].length,
        text: heading[2].trim(),
      })
      continue
    }
    const bullet = /^[-*]\s+(.*)$/.exec(line)
    if (bullet) {
      flushParagraph()
      list.push(bullet[1].trim())
      continue
    }
    flushList()
    paragraph.push(line)
  }
  flushParagraph()
  flushList()
  return blocks
}

export function ReleaseNotes({
  markdown,
  plain,
}: {
  markdown?: string | null
  plain?: string | null
}) {
  const source = markdown?.trim() || plain?.trim()
  if (!source) {
    return null
  }
  if (!markdown?.trim()) {
    return <div className="release-notes release-notes--plain">{source}</div>
  }

  return (
    <div className="release-notes">
      {parse(source).map((block, index) => {
        if (block.type === 'heading') {
          const Tag = block.level >= 3 ? 'h4' : 'h3'
          return <Tag key={`heading-${index}`}>{block.text}</Tag>
        }
        if (block.type === 'list') {
          return (
            <ul key={`list-${index}`}>
              {block.items.map((item, itemIndex) => (
                <li key={`item-${index}-${itemIndex}`}>
                  {inline(item, `item-${index}-${itemIndex}`)}
                </li>
              ))}
            </ul>
          )
        }
        return (
          <p key={`paragraph-${index}`}>
            {inline(block.text, `paragraph-${index}`)}
          </p>
        )
      })}
    </div>
  )
}
