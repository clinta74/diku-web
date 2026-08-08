import { describe, expect, it } from 'vitest'
import {
  gameReducer,
  initialGameState,
  MAX_SCROLLBACK,
  type GameState,
} from './gameReducer'
import type { GameEvent } from '../net/protocol'

function apply(state: GameState, ...events: GameEvent[]): GameState {
  return events.reduce((acc, event) => gameReducer(acc, { kind: 'event', event }), state)
}

const roomEvent: GameEvent = {
  type: 'room',
  data: {
    key: 'aldenmoor.millbrook.north-gate',
    title: 'The North Gate',
    description: 'A weathered portcullis.',
    exits: ['north', 'east'],
  },
}

describe('gameReducer', () => {
  it('appends text events to the scrollback', () => {
    const state = apply(initialGameState, {
      type: 'text',
      data: { spans: [{ t: 'You walk north.' }] },
    })

    expect(state.scrollback).toHaveLength(1)
    expect(state.scrollback[0].spans[0].t).toBe('You walk north.')
  })

  it('gives every line a unique id', () => {
    // React keys off these; duplicates would make lines swap places on re-render.
    const state = apply(
      initialGameState,
      { type: 'text', data: { spans: [{ t: 'one' }] } },
      { type: 'text', data: { spans: [{ t: 'two' }] } },
      { type: 'text', data: { spans: [{ t: 'three' }] } },
    )

    const ids = state.scrollback.map((l) => l.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('replaces panel state rather than merging it', () => {
    // The server always sends a complete snapshot, so a merge could only preserve stale data.
    const first = apply(initialGameState, roomEvent)
    const second = apply(first, {
      type: 'room',
      data: { key: 'aldenmoor.millbrook.market-row', title: 'Market Row', description: '', exits: ['west'] },
    })

    expect(second.room?.title).toBe('Market Row')
    expect(second.room?.exits).toEqual(['west'])
  })

  it('keeps panels independent of one another', () => {
    const state = apply(
      initialGameState,
      roomEvent,
      {
        type: 'vitals',
        data: {
          health: 42, healthMax: 60, focus: 20, focusMax: 20,
          stamina: 88, staminaMax: 100, level: 7, xp: 12480, path: 'Warden', gold: 340,
        },
      },
    )

    // A vitals update must not clear the room panel.
    expect(state.room?.title).toBe('The North Gate')
    expect(state.vitals?.health).toBe(42)
  })

  it('renders sys events into the scrollback with a kind style', () => {
    const state = apply(initialGameState, {
      type: 'sys',
      data: { message: 'Reconnected.', kind: 'info' },
    })

    expect(state.scrollback[0].spans[0].t).toBe('Reconnected.')
    expect(state.scrollback[0].spans[0].s).toBe('sys-info')
  })

  it('caps the scrollback and keeps the newest lines', () => {
    let state = initialGameState
    for (let i = 0; i < MAX_SCROLLBACK + 50; i++) {
      state = gameReducer(state, {
        kind: 'event',
        event: { type: 'text', data: { spans: [{ t: `line ${i}` }] } },
      })
    }

    expect(state.scrollback).toHaveLength(MAX_SCROLLBACK)
    expect(state.scrollback.at(-1)?.spans[0].t).toBe(`line ${MAX_SCROLLBACK + 49}`)
  })

  it('tracks connection state separately from game state', () => {
    const withRoom = apply(initialGameState, roomEvent)
    const disconnected = gameReducer(withRoom, { kind: 'connection', connected: false })

    // A dropped stream must not blank the panels: the character is still in the world
    // during the link-dead grace window.
    expect(disconnected.connected).toBe(false)
    expect(disconnected.room?.title).toBe('The North Gate')
  })

  it('supports locally-generated lines for echoing input', () => {
    const state = gameReducer(initialGameState, {
      kind: 'local',
      spans: [{ t: '> look', s: 'echo' }],
    })

    expect(state.scrollback[0].spans[0].t).toBe('> look')
  })
})
