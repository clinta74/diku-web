/**
 * All requests go to same-origin paths, proxied to Kestrel by the Vite dev server. That is
 * what lets the HttpOnly session cookie behave in development exactly as in production
 * (PLAN.md §3.2) - the browser never sees a cross-origin request.
 */

export interface Account {
  id: string
  username: string
  email: string
  role: string
}

export interface Character {
  id: string
  name: string
  path: string
  level: number
  xp: number
  roomKey: string
  lastPlayedAt: string | null
}

export class ApiError extends Error {
  // A plain field rather than a constructor parameter property: the tsconfig sets
  // erasableSyntaxOnly, so only syntax that strips cleanly to JavaScript is allowed.
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    // Endpoints report failures as { error }, so surface that rather than a bare status.
    let message = `Request failed (${response.status})`
    try {
      const body = await response.json()
      if (body && typeof body.error === 'string') message = body.error
    } catch {
      // Some failures have no body at all; the status message stands.
    }
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  me: () => request<Account>('/api/auth/me'),

  register: (email: string, username: string, password: string) =>
    request<Account>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, username, password }),
    }),

  login: (username: string, password: string) =>
    request<Account>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

  characters: () => request<Character[]>('/api/characters'),

  createCharacter: (name: string, path: string) =>
    request<Character>('/api/characters', {
      method: 'POST',
      body: JSON.stringify({ name, path }),
    }),

  /**
   * Game routes are scoped by character so one login can drive several at once. The cookie
   * still authorises; the id only selects which of your own characters is meant.
   */
  enter: (characterId: string) =>
    request<{
      sessionId: string
      characterId: string
      character: string
      reconnected: boolean
    }>(`/api/game/${characterId}/enter`, { method: 'POST' }),

  /**
   * Returns 202 with no body on purpose. Every result, including this command's own output,
   * arrives over that character's SSE stream so the scrollback is always in true order
   * (PLAN.md §3.3).
   */
  command: (characterId: string, input: string) =>
    request<void>(`/api/game/${characterId}/command`, {
      method: 'POST',
      body: JSON.stringify({ input }),
    }),

  leave: (characterId: string) =>
    request<void>(`/api/game/${characterId}/leave`, { method: 'POST' }),

  /** Which of this account's characters are currently in the world. */
  sessions: () =>
    request<{ characterId: string; character: string; streaming: boolean }[]>(
      '/api/game/sessions',
    ),
}
