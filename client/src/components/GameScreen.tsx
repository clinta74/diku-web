import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import * as DropdownMenu from '@radix-ui/react-dropdown-menu'
import { api } from '../net/api'
import { connectStream } from '../net/stream'
import { gameReducer, initialGameState } from '../state/gameReducer'
import type { ContentEntry, MapPayload, TextSpan, VitalsPayload } from '../net/protocol'
import { shouldRedirectToInput } from './typeAnywhere'
import { useCoarsePointer, usePhoneLayout } from './pointer'
import { exitPad, recentCommands, verbsFor } from './touchVerbs'
import { applyCompletion, completionsFor, type Completions } from './completion'
import { loadHistory, remember, saveHistory } from './commandHistory'
import { AbilityBar } from './AbilityBar'
import { followSlack, isAtBottom } from './scrollFollow'

interface Props {
  characterId: string
  characterName: string
  onLeave: () => void
  /**
   * This character has been taken over by another device, and this screen is finished.
   *
   * Deliberately not `onLeave`: leaving removes the character from the world, and doing that here
   * would evict the character the *other* device is now playing. Nothing is asked of the server —
   * it has already handed the character over — so this only has to put the player somewhere that
   * is not a dead game screen.
   */
  onDisplaced?: (message: string) => void
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

/**
 * Publishes how much of the viewport the on-screen keyboard is covering, as `--keyboard-inset` on
 * the document element. Zero when there is no keyboard, which is every desktop and every phone
 * that is not currently typing.
 *
 * `interactive-widget=resizes-content` in the viewport meta already does this on Chrome and
 * Android by shrinking the viewport itself, and there the inset stays at zero. Safari ignores the
 * hint and overlays the keyboard instead, leaving the layout convinced it still has the full
 * height — so the command input, the last row of the grid, ends up underneath the keyboard the
 * moment it is tapped. VisualViewport is the only thing that reports that overlap.
 */
function useKeyboardInset() {
  useEffect(() => {
    const viewport = window.visualViewport
    if (!viewport) return

    const sync = () => {
      // How much of the layout viewport the visual one no longer covers. `offsetTop` matters on
      // Safari, which scrolls the page up behind the keyboard rather than resizing it.
      const covered = window.innerHeight - viewport.height - viewport.offsetTop

      // A pixel or two of disagreement is normal and constant; only a real keyboard is worth
      // reflowing the grid for.
      document.documentElement.style.setProperty(
        '--keyboard-inset',
        covered > 40 ? `${Math.round(covered)}px` : '0px',
      )
    }

    sync()
    viewport.addEventListener('resize', sync)
    viewport.addEventListener('scroll', sync)

    return () => {
      viewport.removeEventListener('resize', sync)
      viewport.removeEventListener('scroll', sync)
      document.documentElement.style.removeProperty('--keyboard-inset')
    }
  }, [])
}

export function GameScreen({
  characterId,
  characterName,
  onLeave,
  onDisplaced,
  onRoomChange,
  onOpenBuilder,
  focusInputRef,
  active = true,
}: Props) {
  const [state, dispatch] = useReducer(gameReducer, initialGameState)
  const roomKey = state.room?.key ?? null
  const phone = usePhoneLayout()

  // Layout is a width question; whether a tap should offer verbs is a pointer question. They are
  // asked separately because they disagree on a tablet and on a narrow desktop window.
  const coarse = useCoarsePointer()

  // The room panel and map, on a phone. Closed by default: the transcript is the game, and this
  // is the reference material you consult rather than the thing you watch.
  const [sheetOpen, setSheetOpen] = useState(false)

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


  /**
   * The takeover handler, held in a ref so the stream effect never depends on its identity.
   *
   * <b>This is load-bearing, not tidiness.</b> A callback passed inline by the parent is a new
   * function on every one of the parent's renders, and App re-renders on every room change. With
   * it in the dependency array below, the effect tore the stream down and reopened it constantly —
   * and each teardown tells the server the connection dropped, which marks the character link-dead
   * and completes its event channel, so the stream that replaced it read a closed one. The screen
   * ended up permanently "Disconnected. Trying to reconnect…", which is precisely the state a
   * player reported on the device that had just *won* the character.
   *
   * The same trap is why `onRoomChange` is wrapped in `useCallback` by the caller. A ref is the
   * stronger fix: it holds for any caller, rather than for callers who remember.
   */
  const displacedHandler = useRef(onDisplaced)
  displacedHandler.current = onDisplaced

  // Keyed by character, so a second tab on a different character opens its own stream
  // rather than evicting this one.
  //
  // A takeover ends this screen rather than offering to fight back for the character. Two screens
  // that can each reclaim it is the tug-of-war in a politer costume: the loser reconnects, wins,
  // and the other device becomes the loser. One of them has to be finished, and it is the older
  // one — the player is at the device they just picked up.
  useEffect(() => {
    const close = connectStream(characterId, {
      onEvent: (event) => dispatch({ kind: 'event', event }),
      onOpen: () => dispatch({ kind: 'connection', connected: true }),
      onError: () => dispatch({ kind: 'connection', connected: false }),
      onDisplaced: (message) => {
        dispatch({ kind: 'connection', connected: false })
        displacedHandler.current?.(message)
      },
    })
    return close
  }, [characterId])

  /**
   * Whether to admit to being disconnected.
   *
   * Not simply `!connected`: the stream has not opened yet on the first render, and every ordinary
   * page load would flash "Disconnected" before the connection it is complaining about had been
   * given a chance to happen. A dropped stream also usually comes back within a second or two on
   * its own, and a bar that appears for that long teaches players to ignore it — which is a
   * problem the one time it stays.
   */
  const [admitDisconnected, setAdmitDisconnected] = useState(false)

  useEffect(() => {
    if (state.connected) {
      setAdmitDisconnected(false)
      return
    }

    const timer = setTimeout(() => setAdmitDisconnected(true), 2000)
    return () => clearTimeout(timer)
  }, [state.connected])

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

  useKeyboardInset()

  // Escape closes the sheet, as it would any overlay. Bound while it is open rather than always,
  // so Escape means whatever it usually means the rest of the time.
  useEffect(() => {
    if (!sheetOpen) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setSheetOpen(false)
    }

    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [sheetOpen])

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
    <div className="game" data-layout={phone ? 'phone' : 'desktop'}>
      {/*
        The phone header. Hidden on desktop, where the room panel is on screen and says all of
        this already — this is that panel collapsed to one line, plus the way back to it.
      */}
      <RoomHeader
        title={state.room?.title ?? '...'}
        exits={state.room?.exits ?? []}
        connected={state.connected}
        open={sheetOpen}
        onToggle={() => setSheetOpen((open) => !open)}
      />

      {/*
        On desktop this wrapper is `display: contents`, so the map and room panels are grid items
        exactly as they were. On a phone it becomes the sheet that slides over the transcript.
        One tree, two shapes — rendering different children per layout would mean the map unmounts
        and remounts every time the window crosses 600px.
      */}
      <div
        className="room-sheet"
        data-open={sheetOpen}
        // Only meaningful on a phone, where the sheet is genuinely hidden. On desktop the panels
        // are on screen and hiding them from assistive tech would be a lie.
        aria-hidden={phone && !sheetOpen}
        // Keeps the closed sheet out of the tab order. Without it the map and the contents list
        // are still focusable behind the transcript, so tabbing wanders into an invisible panel.
        inert={phone && !sheetOpen}
      >
        <div className="sheet-head">
          <button type="button" className="sheet-close" onClick={() => setSheetOpen(false)}>
            ✕ Close
          </button>
        </div>

        <MapPanel map={state.map} />
        <RoomPanel
          title={state.room?.title ?? '...'}
          description={state.room?.description ?? ''}
          exits={state.room?.exits ?? []}
          contents={state.contents?.occupants ?? []}
          onKeyword={(keyword) => insertKeyword.current?.(keyword)}
          onCommand={(command) => {
            send(command)

            // The sheet has done its job the moment a verb is chosen, and the answer arrives in
            // the transcript behind it. Leaving it open would hide the result of the tap.
            setSheetOpen(false)
          }}
          touch={coarse}
        />
      </div>
      <Scrollback lines={state.scrollback} onOpenBuilder={onOpenBuilder} />

      {/*
        Shown whenever the stream is down. It says what is happening and nothing more.

        There was a "Rejoin the world" button here, for the case the retry cannot fix: once the
        link-dead window has passed the character has been removed, and reconnecting a stream
        attaches to nothing — only entering again puts them back. It is gone because the character
        list already does exactly that, in one click, and it is the way back from being displaced
        by another device too. Two routes to one place is two things to keep working, and this was
        the one that had quietly stopped: it called a setter that no longer existed, so pressing it
        threw rather than rejoining.
      */}
      {admitDisconnected && (
        <div className="reconnect-bar" role="status">
          <span className="dim">Disconnected. Trying to reconnect…</span>
        </div>
      )}

      {/*
        Touch verbs (MOBILE.md M2). Part of the phone layout rather than gated on the pointer:
        they occupy a row of the phone grid, and a narrow desktop window that gets the layout
        should get the row that goes with it.
      */}
      {phone && (
        <ExitPad exits={state.room?.exits ?? []} onGo={send} />
      )}

      {/*
        Above the input, because it is a thing you read and then type - putting it under the input
        would mean looking past what you are writing to see what you can write.
      */}
      <AbilityBar
        abilities={state.abilities}
        cooldownUntil={state.cooldownUntil}
        onPick={(verb) => insertKeyword.current?.(verb)}
      />

      <InputBar
        onSend={send}
        insertRef={insertKeyword}
        focusRef={focusInputRef ?? focusInput}
        active={active}
        characterId={characterId}
        candidates={candidates}
        showChips={phone}
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

/**
 * The phone header: where you are, whether the stream is up, and the way into the room sheet.
 *
 * Rendered on every layout and hidden by the stylesheet on desktop, where the room panel is
 * already on screen saying all of it. The exit count rather than the exits themselves — the names
 * do not fit on one line, and M2's exit pad is where they become useful anyway.
 */
function RoomHeader({
  title,
  exits,
  connected,
  open,
  onToggle,
}: {
  title: string
  exits: string[]
  connected: boolean
  open: boolean
  onToggle: () => void
}) {
  return (
    <header className="room-header">
      <span className="room-header-title">{title}</span>

      {/*
        The connection dot lives here as well as in the vitals row, because the vitals row wraps on
        a narrow screen and the status can end up on a second line below the fold. Losing sight of
        whether the game is connected is the one thing that must not happen quietly.
      */}
      <span
        className={connected ? 'room-header-dot good' : 'room-header-dot bad'}
        title={connected ? 'Connected' : 'Reconnecting…'}
        aria-label={connected ? 'Connected' : 'Reconnecting'}
      >
        ●
      </span>

      <button
        type="button"
        className="room-header-toggle"
        onClick={onToggle}
        aria-expanded={open}
      >
        ▤ room
        {exits.length > 0 && <span className="dim"> · {exits.length}</span>}
      </button>
    </header>
  )
}

function RoomPanel({
  title,
  description,
  exits,
  contents,
  onKeyword,
  onCommand,
  touch,
}: {
  title: string
  description: string
  exits: string[]
  contents: ContentEntry[]
  onKeyword: (keyword: string) => void
  onCommand: (command: string) => void
  /** Offers verbs on a tap instead of typing the keyword. See `verbsFor`. */
  touch: boolean
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
        {displayItems.map(({ entry, count }) => {
          const name = (
            <>
              <span className="glyph">{entry.icon}</span> {entry.label}
              {count > 1 && <span className="dim"> ×{count}</span>}
            </>
          )

          // On a desktop the click types the keyword and the player finishes the sentence, which
          // is a good trade when a keyboard is one key away. On touch it costs a keyboard over the
          // game, so the same tap offers the verbs instead — with "Type its name" kept as the last
          // item, so nothing that was possible before has become unreachable.
          return (
            <li key={entry.keyword}>
              {touch ? (
                <DropdownMenu.Root>
                  <DropdownMenu.Trigger asChild>
                    <button type="button">{name}</button>
                  </DropdownMenu.Trigger>

                  <DropdownMenu.Portal>
                    <DropdownMenu.Content className="menu" align="start" sideOffset={4}>
                      {verbsFor(entry.keyword).map((verb) => (
                        <DropdownMenu.Item
                          key={verb.label}
                          className="menu-item"
                          onSelect={() => onCommand(verb.command)}
                        >
                          {verb.label}
                        </DropdownMenu.Item>
                      ))}
                      <DropdownMenu.Item
                        className="menu-item"
                        onSelect={() => onKeyword(entry.keyword)}
                      >
                        Type its name
                      </DropdownMenu.Item>
                    </DropdownMenu.Content>
                  </DropdownMenu.Portal>
                </DropdownMenu.Root>
              ) : (
                <button type="button" onClick={() => onKeyword(entry.keyword)}>
                  {name}
                </button>
              )}
            </li>
          )
        })}
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
      onScroll={(e) => setFollowing(isAtBottom(e.currentTarget, followSlack(e.currentTarget)))}
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

/**
 * Six direction keys, under the thumb (MOBILE.md M2).
 *
 * The reason this exists: the main verb of a MUD is walking, and walking meant typing `north` on
 * a phone keyboard that covers half the screen. Every direction is drawn whether or not the room
 * has it — see `exitPad` for why the row must not reflow.
 */
function ExitPad({ exits, onGo }: { exits: string[]; onGo: (command: string) => void }) {
  return (
    <div className="exit-pad" role="group" aria-label="Exits">
      {exitPad(exits).map((key) => (
        <button
          key={key.direction}
          type="button"
          className={key.available ? 'exit-key' : 'exit-key unavailable'}
          disabled={!key.available}
          aria-label={key.direction}
          onClick={() => onGo(key.direction)}
        >
          {key.label}
        </button>
      ))}
    </div>
  )
}

function InputBar({
  onSend,
  insertRef,
  focusRef,
  active,
  characterId,
  candidates,
  showChips,
}: {
  onSend: (input: string) => void
  insertRef: React.RefObject<((keyword: string) => void) | null>
  focusRef: React.RefObject<(() => void) | null>
  active: boolean
  characterId: string
  candidates: string[]
  /** Draws the recent-command row above the input. The phone stand-in for the up arrow. */
  showChips?: boolean
}) {
  const [value, setValue] = useState('')
  const [history, setHistory] = useState<string[]>(() => loadHistory(characterId))
  const [cursor, setCursor] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)
  const coarse = useCoarsePointer()
  const chips = useMemo(() => (showChips ? recentCommands(history) : []), [showChips, history])

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
  //
  // Off entirely on touch. There are no stray keystrokes to catch when the keyboard only exists
  // while a field is focused, and stealing focus would summon it over the game unasked.
  useEffect(() => {
    if (!active || coarse) return

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
  }, [active, coarse])

  /**
   * Sends a command and files it in history, whatever typed it — the input box, a chip, or the
   * exit pad by way of `onSend`.
   */
  function run(input: string) {
    onSend(input)

    // Written outside the updater rather than inside it: an updater must stay pure, and under
    // StrictMode React runs it twice.
    const next = remember(history, input)
    setHistory(next)
    saveHistory(characterId, next)

    setCursor(-1)
  }

  function submit() {
    const input = value.trim()
    if (!input) return

    run(input)
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
      {/*
        The phone's up arrow. Tapping runs the command rather than loading it into the box: on a
        desktop, loading it is right because Enter is one key away, but here sending it would
        otherwise mean summoning the keyboard to press a return key you did not need.
      */}
      {showChips && chips.length > 0 && (
        <div className="command-chips" role="group" aria-label="Recent commands">
          {chips.map((command) => (
            <button
              key={command}
              type="button"
              className="command-chip"
              onClick={() => run(command)}
            >
              {command}
            </button>
          ))}
        </div>
      )}

      <span className="prompt">&gt;</span>
      <input
        ref={inputRef}
        value={value}
        // Focusing on arrival is right on a desktop and wrong on a phone, where it throws up the
        // keyboard over half the screen before the player has read the room they are standing in.
        autoFocus={!coarse}
        spellCheck={false}
        autoComplete="off"
        // A phone otherwise sends `North` and helpfully corrects `n` to `no`. The parser is
        // case-insensitive, but autocorrect rewriting whole words is not something it can survive.
        autoCapitalize="none"
        autoCorrect="off"
        // "Send" on the return key rather than "Go". The form is not going anywhere.
        enterKeyHint="send"
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
