import { PULSE_MS } from '../net/protocol'
import type {
  AbilityEntry,
  ContentsPayload,
  GameEvent,
  MapPayload,
  RoomPayload,
  TextSpan,
  VitalsPayload,
} from '../net/protocol'

/** Capped so a long session cannot grow the DOM without bound. */
export const MAX_SCROLLBACK = 500

export interface ScrollbackLine {
  id: number
  spans: TextSpan[]
}

export interface GameState {
  room: RoomPayload | null
  map: MapPayload | null
  contents: ContentsPayload | null
  vitals: VitalsPayload | null
  abilities: AbilityEntry[]
  /**
   * When each cooling ability becomes usable again, as a wall-clock timestamp. Absent means ready.
   *
   * An absolute instant rather than a remaining count, so nothing has to tick this state forward:
   * the server says "24 pulses" once, this records the moment that lands on, and the panel derives
   * the number on screen from the clock. A stored countdown would need a timer inside the reducer
   * and would drift whenever the tab was backgrounded and the timer throttled.
   */
  cooldownUntil: Record<string, number>
  scrollback: ScrollbackLine[]
  nextLineId: number
  connected: boolean
}

export const initialGameState: GameState = {
  room: null,
  map: null,
  contents: null,
  vitals: null,
  abilities: [],
  cooldownUntil: {},
  scrollback: [],
  nextLineId: 1,
  connected: false,
}

export type GameAction =
  | { kind: 'event'; event: GameEvent }
  | { kind: 'connection'; connected: boolean }
  | { kind: 'local'; spans: TextSpan[] }

export function gameReducer(state: GameState, action: GameAction): GameState {
  switch (action.kind) {
    case 'connection':
      return { ...state, connected: action.connected }

    case 'local':
      return appendLine(state, action.spans)

    case 'event':
      return applyEvent(state, action.event)
  }
}

function applyEvent(state: GameState, event: GameEvent): GameState {
  switch (event.type) {
    case 'text':
      return appendLine(state, event.data.spans)

    // Panels are replaced wholesale rather than merged: the server always sends a complete
    // snapshot, so merging could only ever preserve stale data.
    case 'room':
      return { ...state, room: event.data }

    case 'map':
      return { ...state, map: event.data }

    case 'contents':
      return { ...state, contents: event.data }

    case 'vitals':
      return { ...state, vitals: event.data }

    case 'abilities': {
      // Replaces both the list and the cooldowns. The roster is the authoritative picture - it
      // arrives on entry precisely because the client has been away and its own countdowns are
      // whatever they were when it left.
      const now = Date.now()
      const cooldownUntil: Record<string, number> = {}

      for (const ability of event.data.abilities) {
        if (ability.remainingPulses > 0) {
          cooldownUntil[ability.key] = now + ability.remainingPulses * PULSE_MS
        }
      }

      return { ...state, abilities: event.data.abilities, cooldownUntil }
    }

    case 'cooldown':
      return {
        ...state,
        cooldownUntil: {
          ...state.cooldownUntil,
          [event.data.key]: Date.now() + event.data.pulses * PULSE_MS,
        },
      }

    case 'sys':
      return appendLine(state, [{ t: event.data.message, s: `sys-${event.data.kind}` }])
  }
}

function appendLine(state: GameState, spans: TextSpan[]): GameState {
  const line: ScrollbackLine = { id: state.nextLineId, spans }
  const scrollback = [...state.scrollback, line]

  return {
    ...state,
    scrollback:
      scrollback.length > MAX_SCROLLBACK
        ? scrollback.slice(scrollback.length - MAX_SCROLLBACK)
        : scrollback,
    nextLineId: state.nextLineId + 1,
  }
}
