// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HEARTBEAT_INTERVAL_MS, connectStream } from './stream'

/**
 * The client saying it is still there.
 *
 * The SSE stream cannot prove this and never could: everything the server writes goes into a
 * kernel send buffer and succeeds whether or not anybody is listening. Measured, a client whose
 * network vanished silently held a live session for over sixteen minutes, with or without nginx
 * in front (PLAN.md §11). These assert the half of the fix that lives here.
 */
class FakeEventSource {
  static last: FakeEventSource | null = null
  static readonly CLOSED = 2

  readyState = 1
  closed = false
  url: string

  constructor(url: string) {
    this.url = url
    FakeEventSource.last = this
  }

  addEventListener() {}

  close() {
    this.closed = true
    this.readyState = 2
  }
}

describe('the client heartbeat', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.useFakeTimers()
    FakeEventSource.last = null

    fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('EventSource', FakeEventSource)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  const beats = () =>
    fetchMock.mock.calls.filter(([url]) => String(url).includes('/heartbeat'))

  it('beats once immediately, so a reconnecting client is not counted as quiet', () => {
    // Waiting a full interval before the first beat would hand a reconnecting client twenty
    // seconds of its sixty-second budget before it had said anything at all.
    const close = connectStream('abc', { onEvent: () => {} })

    expect(beats()).toHaveLength(1)
    expect(String(beats()[0][0])).toBe('/api/game/abc/heartbeat')

    close()
  })

  it('keeps beating on its interval', () => {
    const close = connectStream('abc', { onEvent: () => {} })

    vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS * 3)

    expect(beats()).toHaveLength(4)
    close()
  })

  it('stops when the screen is torn down', () => {
    // A beat after teardown claims a session that is gone, which is the exact lie this whole
    // mechanism exists to prevent.
    const close = connectStream('abc', { onEvent: () => {} })
    close()

    vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS * 5)

    expect(beats()).toHaveLength(1)
  })

  it('is comfortably inside the server deadline', () => {
    // The server allows sixty seconds. Three beats fit inside that, so two can be lost to a bad
    // network without anybody being taken out of the world.
    expect(HEARTBEAT_INTERVAL_MS * 3).toBeLessThanOrEqual(60_000)
  })

  it('survives a failed beat', () => {
    // A missed beat is what a flaky network looks like. The next one is twenty seconds away and
    // the server allows three, so a rejected request must not stop the timer or raise.
    fetchMock.mockImplementation(() => Promise.reject(new Error('offline')))

    const close = connectStream('abc', { onEvent: () => {} })
    vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS * 2)

    expect(beats()).toHaveLength(3)
    close()
  })
})
