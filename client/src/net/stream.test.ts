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

  // A plain field rather than a constructor parameter property: the tsconfig sets
  // erasableSyntaxOnly, so only syntax that strips cleanly to JavaScript is allowed.
  readonly url: string

  constructor(url: string) {
    this.url = url
    FakeEventSource.opened.push(this)
  }

  // Recorded rather than ignored, so a test can push a frame in. The displaced notice arrives
  // as an ordinary `sys` event, which is the only way to exercise what the client does with it.
  readonly listeners = new Map<string, (message: { data: string }) => void>()

  addEventListener(type: string, handler: (message: { data: string }) => void) {
    this.listeners.set(type, handler)
  }

  emit(type: string, data: unknown) {
    this.listeners.get(type)?.({ data: JSON.stringify(data) })
  }

  close() {
    this.closed = true
    this.readyState = FakeEventSource.CLOSED
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

it('names its connection, and keeps the same name when it reopens', () => {
  // The whole mechanism rests on this. Two devices on one character send identical requests, so
  // the id is the only thing telling the replaced device from the one that replaced it — and it
  // has to survive a reconnect, or every dropped packet would look like a takeover.
  connect()
  const first = new URL(FakeEventSource.opened[0].url, 'http://x').searchParams.get('connection')

  expect(first).toBeTruthy()

  FakeEventSource.opened[0].readyState = FakeEventSource.CLOSED
  visibility('visible')

  const second = new URL(FakeEventSource.opened[1].url, 'http://x').searchParams.get('connection')
  expect(second).toBe(first)
})

it('gives a fresh name to a stream opened separately', () => {
  // Taking a character back onto this screen is a new connectStream, and it must read as a new
  // device rather than as the old one resuming - otherwise the takeover would be refused.
  connect()
  connect()

  const ids = FakeEventSource.opened.map(
    (source) => new URL(source.url, 'http://x').searchParams.get('connection'),
  )

  expect(ids[0]).not.toBe(ids[1])
})

it('stops for good when the server says another device took over', () => {
  // Retrying is the bug: two devices that both keep reconnecting take the stream in turns and
  // each ends up with about half the game's output.
  const displaced: string[] = []
  const events: unknown[] = []

  connections.push(
    connectStream('c1', {
      onEvent: (event) => events.push(event),
      onDisplaced: (message) => displaced.push(message),
    }),
  )

  FakeEventSource.opened[0].emit('sys', {
    message: 'This character was opened somewhere else.',
    kind: 'displaced',
  })

  expect(displaced).toEqual(['This character was opened somewhere else.'])
  expect(FakeEventSource.opened[0].closed).toBe(true)

  // Not delivered as an ordinary sys line as well: it is about this connection rather than about
  // the character, and the screen says it in its own words.
  expect(events).toHaveLength(0)

  // And looking at the page again must not walk straight back into the fight over the session.
  visibility('visible')
  expect(FakeEventSource.opened).toHaveLength(1)
})

it('does not report a displaced stream as a connection error', () => {
  // EventSource fires onerror as it closes. Reporting that would put "Trying to reconnect…" on a
  // screen that has deliberately stopped trying.
  const errors: number[] = []

  connections.push(
    connectStream('c1', { onEvent: () => {}, onError: () => errors.push(1) }),
  )

  const source = FakeEventSource.opened[0]
  source.emit('sys', { message: 'Opened elsewhere.', kind: 'displaced' })
  source.onerror?.()

  expect(errors).toHaveLength(0)
})

it('still reports an ordinary sys event to the caller', () => {
  const events: Array<{ type: string }> = []

  connections.push(connectStream('c1', { onEvent: (event) => events.push(event) }))

  FakeEventSource.opened[0].emit('sys', { message: 'Welcome.', kind: 'info' })

  expect(events).toHaveLength(1)
  expect(FakeEventSource.opened[0].closed).toBe(false)
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
