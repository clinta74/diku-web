import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../net/api'
import {
  builderApi,
  type AssistJob,
  type ProseDraft,
  type ProseDraftRequest,
  type RoomDraft,
  type RoomDraftRequest,
} from '../../net/builderApi'

/**
 * How often the elapsed clock ticks. Local only — no request is made for this.
 */
const TICK_MS = 1000

/**
 * How long to give the stream before falling back to asking.
 *
 * The server writes the job's current state the instant a stream connects, so silence for this
 * long means the stream is not working rather than that the job is slow. Generous enough not to
 * trip on a slow first paint, short enough that a builder does not sit through a whole draft
 * before anything happens.
 */
const STREAM_GRACE_MS = 8000

/**
 * How often to ask, once asking is all that is left.
 *
 * Only reached when the stream could not be used at all. Three seconds against a job measured at
 * about three minutes.
 */
const POLL_MS = 3000

export type DraftStatus = 'idle' | 'working' | 'ready' | 'failed'

export interface RoomDraftState {
  status: DraftStatus
  /** Seconds since the request was sent. Shown, because a silent three minutes reads as broken. */
  elapsed: number
  draft: RoomDraft | null
  /** Set instead of `draft` when the job was for a mob, item, or quest. */
  prose: ProseDraft | null
  warnings: string[]
  error: string | null
}

const IDLE: RoomDraftState = {
  status: 'idle',
  elapsed: 0,
  draft: null,
  prose: null,
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

  /**
   * The job being watched, in state rather than in a ref.
   *
   * <b>This has to be state, and a ref here was a real bug.</b> `submit` sets the status to
   * working and then awaits the POST that returns the id, so React renders and runs the effect
   * below in between - with the ref still null. The effect returned, and nothing changed the
   * status again, so it never re-ran: the stream was never opened and a draft that completed
   * perfectly well on the server was never collected. The polling version it replaced survived
   * this by accident, because it read the ref lazily inside each tick three seconds later.
   *
   * As state, setting the id is itself what starts the watching.
   */
  const [jobId, setJobId] = useState<string | null>(null)
  const startedAt = useRef<number>(0)

  const discard = useCallback(() => {
    setJobId(null)
    setState(IDLE)
  }, [])

  // One submit path for both kinds, taking the call rather than the body: the polling, the
  // elapsed clock, the refusal handling and the cleanup are identical, and the only thing that
  // differs is which endpoint starts the job.
  const submit = useCallback(async (start: () => Promise<{ id: string }>) => {
    startedAt.current = Date.now()
    setState({ ...IDLE, status: 'working' })

    try {
      const { id } = await start()
      setJobId(id)
    } catch (e) {
      setJobId(null)
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

  const request = useCallback(
    (body: RoomDraftRequest) => submit(() => builderApi.draftRoom(body)),
    [submit],
  )

  const requestProse = useCallback(
    (body: ProseDraftRequest) => submit(() => builderApi.draftProse(body)),
    [submit],
  )

  // The elapsed clock, which costs nothing and is most of what tells a builder the thing is alive.
  useEffect(() => {
    if (state.status !== 'working') return

    const timer = setInterval(() => {
      setState((previous) =>
        previous.status === 'working'
          ? { ...previous, elapsed: Math.floor((Date.now() - startedAt.current) / 1000) }
          : previous,
      )
    }, TICK_MS)

    return () => clearInterval(timer)
  }, [state.status])

  /**
   * Listen for the answer rather than asking for it.
   *
   * A draft takes minutes. Polling it meant twenty-odd requests per draft, an answer up to three
   * seconds after it existed, and — found the hard way — a fight with the assist rate limiter,
   * which is sized for submissions and was being spent on reads.
   *
   * `EventSource` reconnects by itself and the server writes the current state on every connect,
   * so a dropped connection heals without anything here noticing. What it cannot do is tell us it
   * was never going to work at all — a proxy that eats event streams looks exactly like a slow
   * job — so if nothing arrives within the grace period, this gives up on the stream and asks
   * instead. A silent hang is the one outcome worth this much code to avoid.
   */
  useEffect(() => {
    // Keyed on the id, so the arrival of the id is what opens the stream. Keying on the status
    // instead is the bug described above: the status is already `working` before there is an id
    // to watch, and it never changes again to trigger a second look.
    if (!jobId) return

    const id = jobId

    let live = true
    let heard = false
    let poll: ReturnType<typeof setInterval> | undefined

    const settle = (job: AssistJob) => {
      const elapsed = Math.floor((Date.now() - startedAt.current) / 1000)

      if (job.state === 'Succeeded' && (job.draft || job.prose)) {
        setJobId(null)
        setState({
          status: 'ready',
          elapsed,
          draft: job.draft,
          prose: job.prose,
          warnings: job.warnings,
          error: null,
        })
        return true
      }

      if (job.state === 'Failed') {
        setJobId(null)
        setState({ ...IDLE, status: 'failed', error: job.error ?? 'The draft failed.' })
        return true
      }

      return false
    }

    const fail = (message: string) => {
      setJobId(null)
      setState({ ...IDLE, status: 'failed', error: message })
    }

    const startPolling = () => {
      if (poll || !live) return

      poll = setInterval(() => {
        void builderApi
          .assistJob(id)
          .then((job) => {
            if (live) settle(job)
          })
          .catch((e: unknown) => {
            if (live) fail(e instanceof Error ? e.message : 'Lost track of the draft.')
          })
      }, POLL_MS)
    }

    let source: EventSource | undefined

    try {
      source = new EventSource(`/api/builder/assist/jobs/${id}/stream`)

      source.onmessage = (event) => {
        if (!live) return
        heard = true

        try {
          settle(JSON.parse(event.data) as AssistJob)
        } catch {
          // A malformed frame is not worth losing the job over; the next one will be fine, and
          // the stream re-sends the current state whenever it reconnects.
        }
      }

      // Not treated as failure. EventSource raises this on every reconnect, and reconnecting is
      // the behaviour being relied on rather than a problem to report.
      source.onerror = () => undefined
    } catch {
      // No EventSource at all (an old browser, a test environment). Ask instead.
      startPolling()
    }

    const grace = setTimeout(() => {
      if (live && !heard) startPolling()
    }, STREAM_GRACE_MS)

    return () => {
      live = false
      clearTimeout(grace)
      if (poll) clearInterval(poll)
      source?.close()
    }
  }, [jobId])

  return { ...state, request, requestProse, discard }
}
