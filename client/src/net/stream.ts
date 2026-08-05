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
  const source = new EventSource(`/api/game/${characterId}/stream`)

  source.onopen = () => handlers.onOpen?.()

  // Fired on a dropped connection too, where EventSource retries by itself. Reporting it is
  // useful for the status indicator; closing the source here would defeat the auto-retry.
  source.onerror = () => handlers.onError?.()

  for (const type of EVENT_TYPES) {
    source.addEventListener(type, (message) => {
      try {
        const data = JSON.parse((message as MessageEvent).data)
        handlers.onEvent({ type, data } as GameEvent)
      } catch {
        // A malformed frame must not tear down the stream.
      }
    })
  }

  return () => source.close()
}
