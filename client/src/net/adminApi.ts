/**
 * The account administration API (PLAN.md §7.7, §8). Same-origin and cookie-authorised like
 * everything else; the server additionally requires the Admin role, so every call here can 403.
 *
 * The in-game verbs (`promote`, `ban`, `mute`, `delete`) drive the same service, so anything done
 * here is visible there and the other way round.
 */

import { request } from './api'

export interface AdminAccount {
  id: string
  username: string
  email: string
  role: string
  isBanned: boolean
  banReason: string | null
  /** ISO instant, possibly in the past - an expired mute is not cleaned up. */
  mutedUntil: string | null
  createdAt: string
  lastLoginAt: string | null
  characters: string[]
  /**
   * When the sign-in backoff against this account ends, or null when there is none. Only ever a
   * future instant: the server forgets an expired pause, so there is no "used to be paused".
   */
  loginLockedUntil: string | null
  /** As the server saw the caller, after forwarded headers. Null for accounts older than the column. */
  registeredFromAddress: string | null
  /** The most recent sign-in, overwritten each time. Null until the first sign-in on a build that records it. */
  lastLoginAddress: string | null
}

export const ROLES = ['Player', 'Builder', 'Moderator', 'Admin'] as const
export type Role = (typeof ROLES)[number]

/** What each role unlocks, for the panel's role picker. */
export const ROLE_BLURBS: Record<Role, string> = {
  Player: 'Plays. No access to the builder or to moderation.',
  Builder: 'Edits the world. Cannot hand out access or moderate.',
  Moderator: 'Bans, mutes, and retires characters. Not a builder.',
  Admin: 'Everything, including this panel.',
}

/** A mute is set as a duration; the server turns it into an instant against its own clock. */
export const MUTE_DURATIONS = [
  { label: '15 minutes', minutes: 15 },
  { label: '1 hour', minutes: 60 },
  { label: '8 hours', minutes: 480 },
  { label: '24 hours', minutes: 1440 },
  { label: '7 days', minutes: 10080 },
] as const

export function isMuted(account: AdminAccount, now: number = Date.now()): boolean {
  return account.mutedUntil !== null && Date.parse(account.mutedUntil) > now
}

/** Too many wrong passwords: the next sign-in attempt has to wait. */
export function isLoginPaused(account: AdminAccount, now: number = Date.now()): boolean {
  return account.loginLockedUntil !== null && Date.parse(account.loginLockedUntil) > now
}

/**
 * Whether a search term is an address rather than a name. Names are letters, digits and
 * underscores; anything with a dot or a colon in it can only be an address.
 */
export function looksLikeAddress(query: string): boolean {
  return /[.:]/.test(query)
}

export const adminApi = {
  /**
   * By name fragment, or - when the term reads as an address - by exact address, which is the
   * question a ban raises next: who else came from there.
   */
  accounts: (query?: string) =>
    request<AdminAccount[]>(
      query
        ? `/api/admin/accounts?${looksLikeAddress(query) ? 'address' : 'q'}=${encodeURIComponent(query)}`
        : '/api/admin/accounts',
    ),

  account: (username: string) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}`),

  setRole: (username: string, role: Role) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}/role`, {
      method: 'PATCH',
      body: JSON.stringify({ role }),
    }),

  setBan: (username: string, banned: boolean, reason?: string) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}/ban`, {
      method: 'PATCH',
      body: JSON.stringify({ banned, reason: reason || null }),
    }),

  /** Omit `minutes` (or pass 0) to lift a mute. */
  setMute: (username: string, minutes: number | null, reason?: string) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}/mute`, {
      method: 'PATCH',
      body: JSON.stringify({ minutes, reason: reason || null }),
    }),

  /** Clears the sign-in backoff, so somebody whose account was being hammered can get in now. */
  unlock: (username: string) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}/unlock`, {
      method: 'POST',
    }),

  /**
   * Sets someone else's password outright - the only route back in for a locked-out player,
   * since this deployment sends no email. It signs them out everywhere and evicts their
   * characters from the world.
   */
  setPassword: (username: string, password: string) =>
    request<AdminAccount>(`/api/admin/accounts/${encodeURIComponent(username)}/password`, {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),

  /** A soft delete: the row and everything hanging off it survive, the name is freed. */
  deleteCharacter: (name: string) =>
    request<{ message: string }>(`/api/admin/characters/${encodeURIComponent(name)}`, {
      method: 'DELETE',
    }),
}
