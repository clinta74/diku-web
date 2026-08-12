import { EVENT_TYPES, type GameEvent } from './protocol'

export interface StreamHandlers {
  onEvent: (event: GameEvent) => void
  onOpen?: () => void
  onError?: () => void
}

/**
 * Opens the SSE stream.
 *
 * Native EventSource is used deliberately: it reconnects on its own and replays the
 * Last-Event-ID header, which the server answers from its ring buffer (PLAN.md §3.4).
 * It also cannot set request headers, which is exactly why auth is a cookie.
 */
export function connectStream(characterId: string, handlers: StreamHandlers): () => void {
  let source = open()

  function open(): EventSource {
    const stream = new EventSource(`/api/game/${characterId}/stream`)

    stream.onopen = () => handlers.onOpen?.()

    // Fired on a dropped connection too, where EventSource retries by itself. Reporting it is
    // useful for the status indicator; closing the source here would defeat the auto-retry.
    stream.onerror = () => handlers.onError?.()

    for (const type of EVENT_TYPES) {
      stream.addEventListener(type, (message) => {
        try {
          const data = JSON.parse((message as MessageEvent).data)
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

    source.close()
    source = open()
  }

  document.addEventListener('visibilitychange', onVisible)

  return () => {
    document.removeEventListener('visibilitychange', onVisible)
    source.close()
  }
}
