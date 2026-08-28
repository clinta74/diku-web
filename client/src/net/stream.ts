import { EVENT_TYPES, type GameEvent } from './protocol'

export interface StreamHandlers {
  onEvent: (event: GameEvent) => void
  onOpen?: () => void
  onError?: () => void

  /**
   * This character is being played somewhere else and this screen is finished.
   *
   * Separate from `onError` because the two want opposite reactions: an error is retried, and
   * this is the one case where retrying is the bug — two devices that both keep reconnecting
   * take the stream in turns and each ends up with about half the game's output.
   */
  onDisplaced?: (message: string) => void
}

/**
 * A name for this screen's connection, minted once and kept for its lifetime.
 *
 * It is not a credential — the cookie does all the authorising and ownership is rechecked on
 * every request (PLAN.md §3.2). It exists because two devices playing one character send
 * byte-identical requests, so the server has no other way to tell the device that was replaced
 * from the device that replaced it.
 *
 * The whole trick is that `EventSource` retries the *same URL*: a dropped connection comes back
 * carrying the id it already had and is recognised as the same screen resuming, while a genuine
 * takeover is a new `EventSource` with a new id.
 */
function mintConnectionId(): string {
  return crypto.randomUUID?.() ?? `c${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/**
 * Opens the SSE stream.
 *
 * Native EventSource is used deliberately: it reconnects on its own and replays the
 * Last-Event-ID header, which the server answers from its ring buffer (PLAN.md §3.4).
 * It also cannot set request headers, which is exactly why auth is a cookie.
 */
/**
 * How often to tell the server this screen is still here.
 *
 * A third of the server's sixty-second deadline, so two beats can be lost to a slow network
 * without anybody being thrown out of the world. The traffic is nothing: at two hundred players
 * this is ten requests a second against the eighty-nine commands a second a busy world was
 * measured handling.
 */
export const HEARTBEAT_INTERVAL_MS = 20_000

/**
 * Tells the server the player is still there, because nothing else does.
 *
 * **The SSE stream cannot prove this and never could.** Everything the server writes goes into a
 * kernel send buffer and succeeds whether or not anyone is listening, so a phone that goes into a
 * tunnel leaves a character standing in the world — broadcast to, regenerated, and looking to
 * everyone else like somebody idle rather than somebody dropped. Measured at over sixteen minutes
 * before the server noticed (PLAN.md §11). A request travelling the other way is the one signal a
 * dead socket cannot produce.
 *
 * Failures are ignored on purpose. A missed beat is what a flaky network looks like, the next one
 * is twenty seconds away, and the server allows three before it does anything.
 */
function startHeartbeat(characterId: string): () => void {
  const beat = () => {
    void fetch(`/api/game/${characterId}/heartbeat`, {
      method: 'POST',
      credentials: 'same-origin',
      keepalive: true,
    }).catch(() => {})
  }

  // One immediately, so a reconnecting client is not counted as quiet for its first interval.
  beat()

  const timer = setInterval(beat, HEARTBEAT_INTERVAL_MS)
  return () => clearInterval(timer)
}

export function connectStream(characterId: string, handlers: StreamHandlers): () => void {
  const connectionId = mintConnectionId()
  const stopHeartbeat = startHeartbeat(characterId)

  // Set when the server says another device has taken over. Everything that would reopen the
  // stream checks it, because reconnecting is precisely what must not happen.
  let displaced = false
  let source = open()

  function open(): EventSource {
    const stream = new EventSource(
      `/api/game/${characterId}/stream?connection=${encodeURIComponent(connectionId)}`,
    )

    stream.onopen = () => handlers.onOpen?.()

    // Fired on a dropped connection too, where EventSource retries by itself. Reporting it is
    // useful for the status indicator; closing the source here would defeat the auto-retry.
    stream.onerror = () => {
      if (displaced) return
      handlers.onError?.()
    }

    for (const type of EVENT_TYPES) {
      stream.addEventListener(type, (message) => {
        try {
          const data = JSON.parse((message as MessageEvent).data)

          // Closed here rather than left to the caller: the server has already finished the
          // response, so anything short of closing lets EventSource retry in three seconds and
          // start the tug-of-war this frame exists to end.
          if (type === 'sys' && data?.kind === 'displaced') {
            displaced = true
            stopHeartbeat()
            stream.close()
            handlers.onDisplaced?.(String(data.message ?? ''))
            return
          }

          handlers.onEvent({ type, data } as GameEvent)
        } catch {
          // A malformed frame must not tear down the stream.
        }
      })
    }

    return stream
  }

  /**
   * Reconnect the moment the page is looked at again, rather than waiting for EventSource's own
   * retry timer (MOBILE.md §6).
   *
   * A phone suspends a backgrounded tab, and the socket usually dies with it. EventSource does
   * reconnect on its own, but only when it next notices — and its backoff is measured from a
   * failure it may not have registered while suspended, so a player returning to the app can sit
   * looking at a dead transcript for several seconds with nothing to click.
   *
   * Only when the connection is actually gone: `CLOSED` means EventSource has given up, and
   * reopening a live stream would drop frames for no reason. The new stream sends Last-Event-ID,
   * so the server replays what was missed from its ring buffer (PLAN.md §3.4) and the transcript
   * closes its own gap.
   */
  const onVisible = () => {
    if (document.visibilityState !== 'visible') return
    if (source.readyState !== EventSource.CLOSED) return

    // A displaced stream is CLOSED on purpose. Reopening it here would walk straight back into
    // the fight over the session every time the player glanced at the old device.
    if (displaced) return

    source.close()
    source = open()
  }

  document.addEventListener('visibilitychange', onVisible)

  return () => {
    document.removeEventListener('visibilitychange', onVisible)
    stopHeartbeat()
    source.close()
  }
}
