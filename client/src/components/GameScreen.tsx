import { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { api } from '../net/api'
import { connectStream } from '../net/stream'
import { gameReducer, initialGameState } from '../state/gameReducer'
import type { ContentEntry, MapPayload, TextSpan, VitalsPayload } from '../net/protocol'

interface Props {
  characterId: string
  characterName: string
  onLeave: () => void
  /** Drives the builder's follow mode (PLAN.md §7.6). Ignored for ordinary players. */
  onRoomChange?: (roomKey: string) => void
  /** Shown only to builders; opens the builder alongside this session. */
  onOpenBuilder?: () => void
  /** Ref to focus the command input from parent (used when closing builder). */
  focusInputRef?: React.RefObject<(() => void) | null>
}

export function GameScreen({
  characterId,
  characterName,
  onLeave,
  onRoomChange,
  onOpenBuilder,
  focusInputRef,
}: Props) {
  const [state, dispatch] = useReducer(gameReducer, initialGameState)
  const roomKey = state.room?.key ?? null

  // Reported upward rather than read from the builder, because the stream is the only thing
  // that knows where the character actually is - including after a goto or a rename.
  useEffect(() => {
    if (roomKey) onRoomChange?.(roomKey)
  }, [roomKey, onRoomChange])

  // Lets the contents list type a keyword into the input box without lifting the input's
  // value up here, which would re-render all five panels on every keystroke.
  const insertKeyword = useRef<((keyword: string) => void) | null>(null)

  // Exposed so parent can focus input when returning from builder
  const focusInput = useRef<(() => void) | null>(null)

  // Keyed by character, so a second tab on a different character opens its own stream
  // rather than evicting this one.
  useEffect(() => {
    const close = connectStream(characterId, {
      onEvent: (event) => dispatch({ kind: 'event', event }),
      onOpen: () => dispatch({ kind: 'connection', connected: true }),
      onError: () => dispatch({ kind: 'connection', connected: false }),
    })
    return close
  }, [characterId])

  const send = useCallback(
    (input: string) => {
      // Echo locally so the player sees what they typed immediately. The result itself still
      // arrives over SSE, keeping one ordered output channel (PLAN.md §3.3).
      dispatch({ kind: 'local', spans: [{ t: `> ${input}`, s: 'echo' }] })
      void api.command(characterId, input).catch(() => {
        dispatch({ kind: 'local', spans: [{ t: 'Command not delivered.', s: 'bad' }] })
      })
    },
    [characterId],
  )

  return (
    <div className="game">
      <MapPanel map={state.map} />
      <RoomPanel
        title={state.room?.title ?? '...'}
        description={state.room?.description ?? ''}
        exits={state.room?.exits ?? []}
        contents={state.contents?.occupants ?? []}
        onKeyword={(keyword) => insertKeyword.current?.(keyword)}
      />
      <Scrollback lines={state.scrollback} />
      <InputBar onSend={send} insertRef={insertKeyword} focusRef={focusInputRef ?? focusInput} />
      <VitalsBar
        vitals={state.vitals}
        characterName={characterName}
        connected={state.connected}
        onLeave={onLeave}
        onOpenBuilder={onOpenBuilder}
      />
    </div>
  )
}

function MapPanel({ map }: { map: MapPayload | null }) {
  if (!map) {
    return (
      <section className="panel map-panel">
        <h2>Room map</h2>
        <p className="dim">Waiting for the world…</p>
      </section>
    )
  }

  // Overlay entities onto a mutable copy of the terrain rows.
  const rows = map.terrain.map((row) => row.split(''))
  for (const entity of map.entities) {
    if (rows[entity.y] && entity.x < rows[entity.y].length) {
      rows[entity.y][entity.x] = entity.icon
    }
  }

  const items = map.entities.filter((entity) => entity.type === 'item')

  return (
    <section className="panel map-panel">
      <h2>Room map</h2>
      <pre className="map">{rows.map((row) => row.join('')).join('\n')}</pre>
      {items.length > 0 && (
        <ul className="legend">
          {items.map((entity) => (
            <li key={entity.id}>
              <span className="glyph">{entity.icon}</span> {entity.label}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function RoomPanel({
  title,
  description,
  exits,
  contents,
  onKeyword,
}: {
  title: string
  description: string
  exits: string[]
  contents: ContentEntry[]
  onKeyword: (keyword: string) => void
}) {
  // Group items by keyword and count duplicates
  const grouped = new Map<string, { entry: ContentEntry; count: number }>()
  for (const entry of contents) {
    const existing = grouped.get(entry.keyword)
    if (existing) {
      existing.count += 1
    } else {
      grouped.set(entry.keyword, { entry, count: 1 })
    }
  }

  const displayItems = Array.from(grouped.values())

  return (
    <section className="panel room-panel">
      <h1 className="room-title">{title}</h1>
      {description && <p className="room-description">{description}</p>}
      <p className="exits">
        {exits.length ? `Exits: ${exits.join(', ')}` : 'There are no obvious exits.'}
      </p>

      <h2>Here</h2>
      <ul className="contents">
        {displayItems.length === 0 && <li className="dim">Nobody else.</li>}
        {displayItems.map(({ entry, count }) => (
          <li key={entry.keyword}>
            <button type="button" onClick={() => onKeyword(entry.keyword)}>
              <span className="glyph">{entry.icon}</span> {entry.label}
              {count > 1 && <span className="dim"> ×{count}</span>}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function Scrollback({ lines }: { lines: { id: number; spans: TextSpan[] }[] }) {
  const endRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: 'end' })
  }, [lines])

  return (
    <section className="scrollback" aria-live="polite">
      {lines.map((line) => (
        <div key={line.id} className="line">
          {line.spans.map((span, i) => (
            <span key={i} className={span.s ?? undefined}>
              {span.t}
            </span>
          ))}
        </div>
      ))}
      <div ref={endRef} />
    </section>
  )
}

function InputBar({
  onSend,
  insertRef,
  focusRef,
}: {
  onSend: (input: string) => void
  insertRef: React.RefObject<((keyword: string) => void) | null>
  focusRef: React.RefObject<(() => void) | null>
}) {
  const [value, setValue] = useState('')
  const [history, setHistory] = useState<string[]>([])
  const [cursor, setCursor] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const ref = insertRef
    ref.current = (keyword: string) => {
      setValue((current) => (current ? `${current} ${keyword}` : keyword))
      inputRef.current?.focus()
    }
    return () => {
      ref.current = null
    }
  }, [insertRef])

  useEffect(() => {
    const ref = focusRef
    ref.current = () => {
      inputRef.current?.focus()
    }
    return () => {
      ref.current = null
    }
  }, [focusRef])

  function submit() {
    const input = value.trim()
    if (!input) return

    onSend(input)
    setHistory((h) => [...h, input])
    setCursor(-1)
    setValue('')
  }

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      submit()
      return
    }

    // Up/down walk the history, the way every MUD client has since 1990.
    if (event.key === 'ArrowUp') {
      event.preventDefault()
      if (history.length === 0) return
      const next = cursor === -1 ? history.length - 1 : Math.max(0, cursor - 1)
      setCursor(next)
      setValue(history[next])
      return
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      if (cursor === -1) return
      const next = cursor + 1
      if (next >= history.length) {
        setCursor(-1)
        setValue('')
      } else {
        setCursor(next)
        setValue(history[next])
      }
    }
  }

  return (
    <div className="input-bar">
      <span className="prompt">&gt;</span>
      <input
        ref={inputRef}
        value={value}
        autoFocus
        spellCheck={false}
        autoComplete="off"
        placeholder="look, north, say hello, help"
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={onKeyDown}
        aria-label="Command input"
      />
    </div>
  )
}

function VitalsBar({
  vitals,
  characterName,
  connected,
  onLeave,
  onOpenBuilder,
}: {
  vitals: VitalsPayload | null
  characterName: string
  connected: boolean
  onLeave: () => void
  onOpenBuilder?: () => void
}) {
  return (
    <div className="vitals-bar">
      {vitals ? (
        <>
          <Meter label="HP" value={vitals.health} max={vitals.healthMax} tone="health" />
          <Meter label="FO" value={vitals.focus} max={vitals.focusMax} tone="focus" />
          <Meter label="ST" value={vitals.stamina} max={vitals.staminaMax} tone="stamina" />
          <span className="identity">
            {characterName} · {vitals.path} · level {vitals.level} · {vitals.xp.toLocaleString()} xp
          </span>
        </>
      ) : (
        <span className="dim">{characterName}</span>
      )}

      <span className={connected ? 'status good' : 'status bad'}>
        {connected ? 'connected' : 'reconnecting…'}
      </span>
      {onOpenBuilder && (
        <button type="button" className="leave" onClick={onOpenBuilder}>
          builder
        </button>
      )}
      <button type="button" className="leave" onClick={onLeave}>
        leave
      </button>
    </div>
  )
}

function Meter({
  label,
  value,
  max,
  tone,
}: {
  label: string
  value: number
  max: number
  tone: string
}) {
  const filled = max > 0 ? Math.round((value / max) * 10) : 0

  return (
    <span className={`meter ${tone}`} title={`${label} ${value}/${max}`}>
      <span className="meter-label">{label}</span>
      <span className="meter-bar">
        {'█'.repeat(filled)}
        <span className="dim">{'░'.repeat(Math.max(0, 10 - filled))}</span>
      </span>
      <span className="meter-value">
        {value}/{max}
      </span>
    </span>
  )
}
