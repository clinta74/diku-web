// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook } from '@testing-library/react'
import type { AssistJob } from '../../net/builderApi'
import { builderApi } from '../../net/builderApi'
import { resetAssistAvailability, useRoomDraft } from './useRoomDraft'

/**
 * A stand-in for EventSource, which jsdom does not have at all.
 *
 * Modelled on the one in `net/stream.test.ts` — the game stream had the same problem first.
 */
class FakeEventSource {
  static opened: FakeEventSource[] = []

  url: string
  onmessage: ((event: { data: string }) => void) | null = null
  onerror: (() => void) | null = null
  closed = false

  constructor(url: string) {
    this.url = url
    FakeEventSource.opened.push(this)
  }

  close() {
    this.closed = true
  }

  /** Delivers a frame the way the server would. */
  send(job: AssistJob) {
    this.onmessage?.({ data: JSON.stringify(job) })
  }
}

const REQUEST = { zoneKey: 'ossara.gatetown', roomKey: 'ossara.gatetown.a-room' }
const DRAFT = { title: 'A Room', description: 'Stone, and cold.', exits: [] }

function job(state: AssistJob['state'], extra: Partial<AssistJob> = {}): AssistJob {
  return {
    id: 'job-1',
    state,
    queuedAt: '2026-08-22T12:00:00Z',
    startedAt: null,
    finishedAt: null,
    draft: null,
    prose: null,
    error: null,
    warnings: [],
    ...extra,
  }
}

beforeEach(() => {
  vi.useFakeTimers()
  resetAssistAvailability()
  FakeEventSource.opened = []
  vi.stubGlobal('EventSource', FakeEventSource)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  cleanup()
})

async function start() {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })

  const rendered = renderHook(() => useRoomDraft())

  await act(async () => {
    await rendered.result.current.request(REQUEST)
  })

  return rendered
}

/**
 * The stream opens when the id arrives, not when the status changes.
 *
 * <b>A regression test for a draft that was generated and then thrown away.</b> `submit` sets the
 * status to working and then awaits the POST that returns the job id, so React renders and runs
 * the watching effect in between. With the id held in a ref, that effect saw null, returned, and
 * never ran again - the status was already `working` and never changed - so no stream was ever
 * opened. The server drafted the room, logged it, and nobody collected it.
 *
 * Every other test in this file hides that by awaiting the request to completion before React
 * flushes anything, which is precisely the ordering a browser does not give you. This one drives
 * the real order: render and flush with the request still in flight, then let it land.
 */
it('opens the stream when the job id arrives, not before', async () => {
  let land: (value: { id: string }) => void = () => undefined
  vi.spyOn(builderApi, 'draftRoom').mockReturnValue(
    new Promise((resolve) => {
      land = resolve
    }),
  )

  const { result } = renderHook(() => useRoomDraft())

  // Fire and deliberately do not await: this is the moment the effect runs with no id yet.
  act(() => {
    void result.current.request(REQUEST)
  })

  expect(result.current.status).toBe('working')
  expect(FakeEventSource.opened).toHaveLength(0)

  await act(async () => {
    land({ id: 'job-1' })
    await Promise.resolve()
  })

  expect(FakeEventSource.opened).toHaveLength(1)
  expect(FakeEventSource.opened[0].url).toBe('/api/builder/assist/jobs/job-1/stream')

  // And it is a working stream, not just an open socket.
  await act(async () => {
    FakeEventSource.opened[0].send(job('Succeeded', { draft: DRAFT }))
  })

  expect(result.current.status).toBe('ready')
  expect(result.current.draft).toEqual(DRAFT)
})

it('opens a stream for the job rather than polling it', async () => {
  const assistJob = vi.spyOn(builderApi, 'assistJob')

  const { result } = await start()

  expect(FakeEventSource.opened).toHaveLength(1)
  expect(FakeEventSource.opened[0].url).toBe('/api/builder/assist/jobs/job-1/stream')
  expect(result.current.status).toBe('working')

  // The whole point: no requests are being made while it waits.
  await act(async () => {
    await vi.advanceTimersByTimeAsync(6000)
  })

  expect(assistJob).not.toHaveBeenCalled()
})

/**
 * A pushed answer arrives when it exists, rather than up to a poll interval later.
 */
it('takes the draft from the stream', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Succeeded', { draft: DRAFT, warnings: ['worth a look'] }))
  })

  expect(result.current.status).toBe('ready')
  expect(result.current.draft).toEqual(DRAFT)
  expect(result.current.warnings).toEqual(['worth a look'])
})

it('takes a failure from the stream too', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Failed', { error: "There is no zone 'nowhere'." }))
  })

  expect(result.current.status).toBe('failed')
  expect(result.current.error).toBe("There is no zone 'nowhere'.")
})

/**
 * Intermediate states move the job along without ending it.
 */
it('stays working while the job is only running', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Running'))
  })

  expect(result.current.status).toBe('working')
})

/**
 * An error frame is not a failure.
 *
 * EventSource raises `onerror` on every reconnect, and reconnecting is the behaviour being relied
 * on — the server re-sends the job's current state whenever a stream connects. Treating it as a
 * failure would abandon a perfectly healthy draft the first time a connection blipped.
 */
it('does not give up when the stream reconnects', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].onerror?.()
  })

  expect(result.current.status).toBe('working')
})

/**
 * The one thing a stream cannot report is never having worked.
 *
 * A proxy that eats event streams looks exactly like a slow job, and a slow job here is three
 * minutes — so silence past the grace period is taken as "this is not going to work" and the
 * client asks instead. A silent hang is the outcome worth this much code to avoid.
 */
it('falls back to asking when the stream says nothing at all', async () => {
  const assistJob = vi.spyOn(builderApi, 'assistJob').mockResolvedValue(job('Running'))

  const { result } = await start()

  expect(assistJob).not.toHaveBeenCalled()

  await act(async () => {
    await vi.advanceTimersByTimeAsync(8000 + 3000)
  })

  expect(assistJob).toHaveBeenCalled()
  expect(result.current.status).toBe('working')
})

/** And having heard from the stream, it never starts asking. */
it('does not fall back once the stream has spoken', async () => {
  const assistJob = vi.spyOn(builderApi, 'assistJob').mockResolvedValue(job('Running'))

  await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Running'))
  })

  await act(async () => {
    await vi.advanceTimersByTimeAsync(30_000)
  })

  expect(assistJob).not.toHaveBeenCalled()
})

/**
 * Warming is a different wait, and is said to be one.
 *
 * Measured on the deployment: a cold canon is about half an hour of prefill against three minutes
 * for a draft. A counter climbing past everything a builder expected, with the same word beside it
 * as an ordinary draft, is how a working system gets reported as broken.
 */
it('says when it is waiting for the model rather than drafting', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Warming'))
  })

  expect(result.current.status).toBe('working')
  expect(result.current.warming).toBe(true)
})

/** And stops saying it once the draft actually starts. */
it('stops saying it is warming once the job runs', async () => {
  const { result } = await start()

  await act(async () => {
    FakeEventSource.opened[0].send(job('Warming'))
  })
  expect(result.current.warming).toBe(true)

  await act(async () => {
    FakeEventSource.opened[0].send(job('Running'))
  })

  expect(result.current.warming).toBe(false)
  expect(result.current.status).toBe('working')
})

it('closes the stream when nobody is watching any more', async () => {
  const { unmount } = await start()

  unmount()

  expect(FakeEventSource.opened[0].closed).toBe(true)
})

/**
 * The elapsed clock is local, so the wait can be shown without asking anything.
 */
it('counts the wait without making a request', async () => {
  const assistJob = vi.spyOn(builderApi, 'assistJob')

  const { result } = await start()

  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000)
  })

  expect(result.current.elapsed).toBeGreaterThanOrEqual(4)
  expect(assistJob).not.toHaveBeenCalled()
})
