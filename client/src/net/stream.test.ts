// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { connectStream } from './stream'

/**
 * A stand-in for EventSource that records how many were opened and lets a test drive readyState.
 * jsdom has no EventSource at all, so there is nothing to spy on otherwise.
 */
class FakeEventSource {
  static opened: FakeEventSource[] = []
  static readonly CLOSED = 2

  readyState = 0
  onopen: (() => void) | null = null
  onerror: (() => void) | null = null
  closed = false

  constructor(public url: string) {
    FakeEventSource.opened.push(this)
  }

  addEventListener() {}

  close() {
    this.closed = true
  }
}

function visibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
  document.dispatchEvent(new Event('visibilitychange'))
}

/**
 * Every connection made by a test, closed when it ends.
 *
 * `document` is shared across the whole file, so a stream left open keeps its visibilitychange
 * listener registered and reacts to the *next* test's events — opening streams that get counted
 * against whichever test is running. Leaking a listener is exactly what the last case here is
 * about, so the harness cannot be the thing doing it.
 */
const connections: Array<() => void> = []

function connect(characterId = 'c1') {
  const close = connectStream(characterId, { onEvent: () => {} })
  connections.push(close)
  return close
}

beforeEach(() => {
  FakeEventSource.opened = []
  vi.stubGlobal('EventSource', FakeEventSource)
})

afterEach(() => {
  for (const close of connections.splice(0)) close()
  vi.unstubAllGlobals()
})

it('opens one stream for the character', () => {
  connect()

  expect(FakeEventSource.opened).toHaveLength(1)
  expect(FakeEventSource.opened[0].url).toContain('/api/game/c1/stream')
})

it('reopens a dead stream when the page is looked at again', () => {
  // A phone suspends a backgrounded tab and the socket usually dies with it. EventSource does
  // reconnect on its own, but its backoff is measured from a failure it may not have registered
  // while suspended — so a player coming back can sit looking at a dead transcript.
  connect()
  FakeEventSource.opened[0].readyState = FakeEventSource.CLOSED

  visibility('visible')

  expect(FakeEventSource.opened).toHaveLength(2)
})

it('leaves a live stream alone', () => {
  // Reopening a working stream would drop whatever was in flight for no reason, and would do it
  // every time the player switched tabs.
  connect()
  FakeEventSource.opened[0].readyState = 1

  visibility('visible')

  expect(FakeEventSource.opened).toHaveLength(1)
})

it('does nothing when the page is being hidden', () => {
  connect()
  FakeEventSource.opened[0].readyState = FakeEventSource.CLOSED

  visibility('hidden')

  expect(FakeEventSource.opened).toHaveLength(1)
})

it('stops listening once the caller disconnects', () => {
  // Otherwise leaving the world and coming back leaves a listener per visit, each of them able to
  // open a stream for a character the player is no longer playing.
  const close = connect()
  FakeEventSource.opened[0].readyState = FakeEventSource.CLOSED

  close()
  visibility('visible')

  expect(FakeEventSource.opened).toHaveLength(1)
  expect(FakeEventSource.opened[0].closed).toBe(true)
})
