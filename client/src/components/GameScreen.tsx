import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { api } from '../net/api'
import { connectStream } from '../net/stream'
import { gameReducer, initialGameState } from '../state/gameReducer'
import type { ContentEntry, MapPayload, TextSpan, VitalsPayload } from '../net/protocol'
import { shouldRedirectToInput } from './typeAnywhere'
import { applyCompletion, completionsFor, type Completions } from './completion'
import { loadHistory, remember, saveHistory } from './commandHistory'
import { isAtBottom } from './scrollFollow'

interface Props {
  characterId: string
  characterName: string
  onLeave: () => void
  /** Drives the builder's follow mode (PLAN.md §7.6). Ignored for ordinary players. */
  onRoomChange?: (roomKey: string) => void
  /**
   * Shown only to builders; opens the builder alongside this session. An optional path opens
   * it on a specific entity, which is what the deep links in `examine` and `stats` use.
   */
  onOpenBuilder?: (path?: string) => void
  /** Ref to focus the command input from parent (used when closing builder). */
  focusInputRef?: React.RefObject<(() => void) | null>
  /**
   * False while something else owns the screen - today, the builder, which hides this component
   * without unmounting it. Only the keyboard cares: an invisible session must not be reading the
   * document's keystrokes.
   */
  active?: boolean
}

export function GameScreen({
  characterId,
  characterName,
  onLeave,
  onRoomChange,
  onOpenBuilder,
  focusInputRef,
  active = true,
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

  // What Tab can complete to. The contents frame is already the room's own answer to "what is
  // here", so nothing new has to be sent for this.
  //
  // Carried items are the gap, and a real one - `drop`, `wear` and the item half of `give` all
  // name something in the pack. Inventory is not on the wire at all today, and piggy-backing it
  // on this frame would be wrong rather than merely incomplete: contents is sent when a room
  // changes, so a list that also claimed to describe the pack would be stale after every buy.
  const candidates = useMemo(() => {
    const entries = [...(state.contents?.occupants ?? []), ...(state.contents?.items ?? [])]

    return entries
      // The viewer is sent as "you", which is not a name anything answers to.
      .filter((entry) => entry.label !== 'you')
      // A parenthetical is presentation - "Mira (link-dead)" is targeted as "Mira".
      .map((entry) => entry.label.replace(/\s*\(.*\)\s*$/, '').trim())
      .filter(Boolean)
  }, [state.contents])

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
      <Scrollback lines={state.scrollback} onOpenBuilder={onOpenBuilder} />
      <InputBar
        onSend={send}
        insertRef={insertKeyword}
        focusRef={focusInputRef ?? focusInput}
        active={active}
        characterId={characterId}
        candidates={candidates}
      />
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

function Scrollback({
  lines,
  onOpenBuilder,
}: {
  lines: { id: number; spans: TextSpan[] }[]
  onOpenBuilder?: (path?: string) => void
}) {
  const boxRef = useRef<HTMLElement>(null)

  // Following the bottom is the normal state, and it is the state a player leaves by scrolling up
  // to read something. It used to scroll to the bottom on every line unconditionally, which meant
  // reading back through a fight that was still going yanked you away four times a second.
  const [following, setFollowing] = useState(true)

  useEffect(() => {
    const box = boxRef.current
    if (box && following) box.scrollTop = box.scrollHeight
  }, [lines, following])

  return (
    <section
      className="scrollback"
      aria-live="polite"
      ref={boxRef}
      onScroll={(e) => setFollowing(isAtBottom(e.currentTarget))}
    >
      {lines.map((line) => (
        <div key={line.id} className="line">
          {line.spans.map((span, i) =>
            // A span carrying a builder path renders as a button rather than text. Only
            // builders are ever sent one, so there is no permission check here — but the
            // handler is still optional, and without it the span stays plain text rather
            // than becoming a control that does nothing.
            span.b && onOpenBuilder ? (
              <button
                key={i}
                type="button"
                className="span-link"
                onClick={() => onOpenBuilder(span.b ?? undefined)}
              >
                {span.t}
              </button>
            ) : (
              <span key={i} className={span.s ?? undefined}>
                {span.t}
              </span>
            ),
          )}
        </div>
      ))}

      {/* Sticky rather than absolutely positioned, so it needs no wrapper around the scroll
          container: as the last child its resting place is below the fold, and sticking to the
          bottom edge is exactly where a jump-to-bottom control belongs. */}
      {!following && (
        <div className="jump-to-bottom">
          <button
            type="button"
            onClick={() => {
              const box = boxRef.current
              if (box) box.scrollTop = box.scrollHeight
              setFollowing(true)
            }}
          >
            ↓ Jump to newest
          </button>
        </div>
      )}
    </section>
  )
}

function InputBar({
  onSend,
  insertRef,
  focusRef,
  active,
  characterId,
  candidates,
}: {
  onSend: (input: string) => void
  insertRef: React.RefObject<((keyword: string) => void) | null>
  focusRef: React.RefObject<(() => void) | null>
  active: boolean
  characterId: string
  candidates: string[]
}) {
  const [value, setValue] = useState('')
  const [history, setHistory] = useState<string[]>(() => loadHistory(characterId))
  const [cursor, setCursor] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)

  // Which completion of the current fragment is showing, so a second Tab offers the next one
  // rather than recomputing against the text the first one just wrote.
  const cycle = useRef<{ typed: string; completions: Completions; index: number } | null>(null)

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

  // Typing anywhere on the page types here, and coming back to the tab puts the caret back.
  // Both are bound to the document rather than to the game panel because the whole point is to
  // catch keystrokes aimed at nothing in particular.
  //
  // `active` is what keeps this from being a document-wide keyboard hijack: the game is hidden
  // rather than unmounted while the builder is open (App.tsx), so without the guard this handler
  // would still be listening and would pull every keystroke out of the builder's forms.
  useEffect(() => {
    if (!active) return

    function focusInput() {
      const input = inputRef.current
      if (input && document.activeElement !== input) input.focus()
    }

    function onKeyDown(event: KeyboardEvent) {
      if (shouldRedirectToInput(event)) focusInput()
    }

    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('focus', focusInput)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('focus', focusInput)
    }
  }, [active])

  function submit() {
    const input = value.trim()
    if (!input) return

    onSend(input)

    // Written outside the updater rather than inside it: an updater must stay pure, and under
    // StrictMode React runs it twice.
    const next = remember(history, input)
    setHistory(next)
    saveHistory(characterId, next)

    setCursor(-1)
    setValue('')
  }

  /**
   * Grows the trailing name, cycling on repeated presses.
   *
   * Tab is reported rather than swallowed when nothing matches, so it still moves focus out of
   * the input. Taking it unconditionally would leave the keyboard with no way off this control,
   * which combined with the type-anywhere handler would make the page unnavigable without a mouse.
   */
  function complete(backwards: boolean): boolean {
    // A cycle is still live only while the box still holds exactly what the last press put there.
    // That one comparison is the whole staleness rule: anything the player does in between -
    // typing, deleting, arrowing through history - changes the value and starts the search again,
    // so no separate "the fragment moved on" reset is needed on the other keys.
    const previous = cycle.current
    const state =
      previous && applyCompletion(previous.typed, previous.completions, previous.index) === value
        ? { ...previous, index: previous.index + (backwards ? -1 : 1) }
        : { typed: value, completions: completionsFor(value, candidates), index: 0 }

    const count = state.completions.matches.length
    if (count === 0) {
      cycle.current = null
      return false
    }

    state.index = ((state.index % count) + count) % count
    cycle.current = state
    setValue(applyCompletion(state.typed, state.completions, state.index))
    return true
  }

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Tab') {
      if (complete(event.shiftKey)) event.preventDefault()
      return
    }

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
            {' · '}
            <span className="gold">{vitals.gold.toLocaleString()} gold</span>
          </span>
        </>
      ) : (
        <span className="dim">{characterName}</span>
      )}

      <span className={connected ? 'status good' : 'status bad'}>
        {connected ? 'connected' : 'reconnecting…'}
      </span>
      {onOpenBuilder && (
        // Called with no arguments on purpose. Passing the handler straight to onClick hands it
        // React's MouseEvent as its first argument, which this signature now reads as a builder
        // path — so the button navigated to an event object instead of /builder.
        <button type="button" className="leave" onClick={() => onOpenBuilder()}>
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
