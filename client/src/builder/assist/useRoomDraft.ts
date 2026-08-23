import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../net/api'
import { builderApi, type RoomDraft, type RoomDraftRequest } from '../../net/builderApi'

/**
 * How often to ask whether a draft is done.
 *
 * Three seconds against a job measured at about three minutes. Tighter would be a hundred
 * requests to learn nothing; looser would make the elapsed counter visibly lurch, and the counter
 * is most of what tells a builder the thing is alive.
 */
const POLL_MS = 3000

export type DraftStatus = 'idle' | 'working' | 'ready' | 'failed'

export interface RoomDraftState {
  status: DraftStatus
  /** Seconds since the request was sent. Shown, because a silent three minutes reads as broken. */
  elapsed: number
  draft: RoomDraft | null
  warnings: string[]
  error: string | null
}

const IDLE: RoomDraftState = {
  status: 'idle',
  elapsed: 0,
  draft: null,
  warnings: [],
  error: null,
}

/**
 * Whether this server has an assistant at all, asked once per page load.
 *
 * Module scope rather than state: the answer is a property of the deployment and cannot change
 * while the page is open, and every room editor asking again would be a request per navigation to
 * learn something already known.
 */
let available: Promise<boolean> | null = null

export function assistAvailable(): Promise<boolean> {
  // Started inside a promise rather than called directly, so a synchronous throw lands in the
  // catch with everything else. This is not hypothetical: a test double that omits the function
  // makes this a TypeError before any promise exists, which escaped the effect and took the whole
  // room editor down with it - the exact failure PLAN.md §13 rules out, since the builder is
  // supposed to work unchanged when there is no assistant. Nothing about "is a model configured"
  // is worth a blank page.
  available ??= Promise.resolve()
    .then(() => builderApi.assistAvailable())
    .then(() => true)
    .catch(() => false)

  return available
}

/** Test seam: forgets the cached answer so each test starts from nothing. */
export function resetAssistAvailability() {
  available = null
}

/**
 * Requests a room draft and follows it to completion.
 *
 * The draft is never applied here. It comes back as something to look at, and the caller decides
 * whether to take it - which is the whole safety argument for the feature (PLAN.md §13): the
 * assistant proposes, a person commits, and the save goes through the same PATCH as any other
 * edit so `content_audit` still records a human.
 */
export function useRoomDraft() {
  const [state, setState] = useState<RoomDraftState>(IDLE)

  // Held in a ref rather than state so the polling effect does not restart every tick, and so the
  // cleanup below can stop a poll for a job the component no longer cares about.
  const jobId = useRef<string | null>(null)
  const startedAt = useRef<number>(0)

  const discard = useCallback(() => {
    jobId.current = null
    setState(IDLE)
  }, [])

  const request = useCallback(async (body: RoomDraftRequest) => {
    startedAt.current = Date.now()
    setState({ ...IDLE, status: 'working' })

    try {
      const { id } = await builderApi.draftRoom(body)
      jobId.current = id
    } catch (e) {
      jobId.current = null
      setState({
        ...IDLE,
        status: 'failed',
        error:
          e instanceof ApiError && e.status === 429
            ? 'The assistant is busy with other drafts. Try again in a minute.'
            : e instanceof Error
              ? e.message
              : 'The request failed.',
      })
    }
  }, [])

  useEffect(() => {
    if (state.status !== 'working') return

    let live = true

    const tick = async () => {
      const id = jobId.current
      if (!id) return

      const elapsed = Math.floor((Date.now() - startedAt.current) / 1000)

      try {
        const job = await builderApi.assistJob(id)
        if (!live) return

        if (job.state === 'Succeeded' && job.draft) {
          jobId.current = null
          setState({
            status: 'ready',
            elapsed,
            draft: job.draft,
            warnings: job.warnings,
            error: null,
          })
          return
        }

        if (job.state === 'Failed') {
          jobId.current = null
          setState({ ...IDLE, status: 'failed', error: job.error ?? 'The draft failed.' })
          return
        }

        // Still queued or running. Only the clock has moved.
        setState((previous) => ({ ...previous, elapsed }))
      } catch (e) {
        if (!live) return
        jobId.current = null
        setState({
          ...IDLE,
          status: 'failed',
          error: e instanceof Error ? e.message : 'Lost track of the draft.',
        })
      }
    }

    const timer = setInterval(tick, POLL_MS)

    return () => {
      live = false
      clearInterval(timer)
    }
  }, [state.status])

  return { ...state, request, discard }
}
