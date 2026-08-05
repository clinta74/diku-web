import type {
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
  scrollback: ScrollbackLine[]
  nextLineId: number
  connected: boolean
}

export const initialGameState: GameState = {
  room: null,
  map: null,
  contents: null,
  vitals: null,
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
