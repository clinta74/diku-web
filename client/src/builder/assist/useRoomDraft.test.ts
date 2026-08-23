// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook } from '@testing-library/react'
import { ApiError } from '../../net/api'
import type { AssistJob } from '../../net/builderApi'
import { builderApi } from '../../net/builderApi'
import { resetAssistAvailability, useRoomDraft } from './useRoomDraft'

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
    error: null,
    warnings: [],
    ...extra,
  }
}

beforeEach(() => {
  vi.useFakeTimers()
  resetAssistAvailability()
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  cleanup()
})

/** Runs the poll interval once and lets the promises inside it settle. */
async function poll() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(3000)
  })
}

it('is idle until asked', () => {
  const { result } = renderHook(() => useRoomDraft())

  expect(result.current.status).toBe('idle')
  expect(result.current.draft).toBeNull()
})

/**
 * The state the builder spends most of the wait in, so it is the state that has to be right.
 */
it('goes to working as soon as the job is accepted', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })

  expect(result.current.status).toBe('working')
})

it('keeps polling while the job is queued, then offers the draft', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })

  const assistJob = vi
    .spyOn(builderApi, 'assistJob')
    .mockResolvedValueOnce(job('Queued'))
    .mockResolvedValueOnce(job('Running'))
    .mockResolvedValueOnce(
      job('Succeeded', { draft: DRAFT, warnings: ['names north 2 times'] }),
    )

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })

  await poll()
  expect(result.current.status).toBe('working')

  await poll()
  expect(result.current.status).toBe('working')

  await poll()
  expect(result.current.status).toBe('ready')

  expect(result.current.draft).toEqual(DRAFT)
  expect(result.current.warnings).toEqual(['names north 2 times'])

  // And it stops: a finished job is not worth asking about again.
  const calls = assistJob.mock.calls.length
  await poll()
  expect(assistJob.mock.calls.length).toBe(calls)
})

it('reports a failed job with the reason the server gave', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })
  vi.spyOn(builderApi, 'assistJob').mockResolvedValue(
    job('Failed', { error: "There is no zone 'nowhere'." }),
  )

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })
  await poll()

  expect(result.current.status).toBe('failed')
  expect(result.current.error).toBe("There is no zone 'nowhere'.")
})

/**
 * A full queue is the expected refusal, not an error.
 *
 * One model, one worker, minutes a job - so 429 is what a busy assistant says, and it deserves a
 * sentence a builder can act on rather than the raw status.
 */
it('turns a full queue into something a person can act on', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockRejectedValue(new ApiError('Too Many Requests', 429))

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })

  expect(result.current.status).toBe('failed')
  expect(result.current.error).toContain('busy')
})

it('discards a draft back to idle', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })
  vi.spyOn(builderApi, 'assistJob').mockResolvedValue(job('Succeeded', { draft: DRAFT }))

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })
  await poll()
  expect(result.current.status).toBe('ready')

  act(() => result.current.discard())

  expect(result.current.status).toBe('idle')
  expect(result.current.draft).toBeNull()
})

/**
 * Navigating away has to stop the polling.
 *
 * Left running, every room a builder visits during one slow draft would leave its own interval
 * behind, all asking about jobs nothing will ever display.
 */
it('stops polling once nobody is watching', async () => {
  vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })
  const assistJob = vi.spyOn(builderApi, 'assistJob').mockResolvedValue(job('Running'))

  const { result, unmount } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request(REQUEST)
  })
  await poll()

  const before = assistJob.mock.calls.length
  unmount()
  await poll()

  expect(assistJob.mock.calls.length).toBe(before)
})

/**
 * The buffer is what gets sent, and that is the point of sending it at all.
 *
 * A builder asks for help with the half-paragraph in front of them, which may never have been
 * saved - so reading the row back on the server would seed the model with the very version they
 * are replacing.
 */
it('sends the editor text so the draft is seeded by it', async () => {
  const draftRoom = vi.spyOn(builderApi, 'draftRoom').mockResolvedValue({ id: 'job-1' })

  const { result } = renderHook(() => useRoomDraft())

  await act(async () => {
    await result.current.request({
      ...REQUEST,
      title: 'The Tollhouse Steps',
      description: 'Half a paragraph I am stuck on.',
    })
  })

  expect(draftRoom).toHaveBeenCalledWith({
    ...REQUEST,
    title: 'The Tollhouse Steps',
    description: 'Half a paragraph I am stuck on.',
  })
})
