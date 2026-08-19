/**
 * Mirrors DikuWeb.Engine.Protocol. Kept hand-written rather than generated: the surface is
 * small, and a mismatch shows up immediately in the reducer tests.
 */

export interface TextSpan {
  t: string
  s?: string | null
  /**
   * A command this span runs when clicked, e.g. `talk vane cogs`.
   *
   * Always identical to `t`. The client echoes what it sends, so a span that displayed one thing
   * and ran another would put a string in the transcript the player never typed — and identical
   * text is also what stops a label and its action drifting apart.
   *
   * It runs rather than inserting, unlike a contents-panel keyword: that click is ambiguous
   * ("a rat" could mean look, attack or get) where this one is already a resolved verb and object.
   */
  c?: string | null
  /**
   * A builder path this span opens, e.g. `/builder/items/rusty-dagger`. Routed internally
   * rather than navigated, so following one keeps the session and the event stream alive.
   * Only ever sent to builders — the server decides, so its absence is not a permission check
   * the client has to make.
   */
  b?: string | null
}

export interface TextPayload {
  spans: TextSpan[]
}

export interface RoomPayload {
  key: string
  title: string
  description: string
  exits: string[]
}

export interface MapEntity {
  id: string
  icon: string
  x: number
  y: number
  label: string
  type?: 'mob' | 'player' | 'item'
}

export interface MapPayload {
  w: number
  h: number
  terrain: string[]
  entities: MapEntity[]
}

export interface ContentEntry {
  icon: string
  label: string
  keyword: string
}

export interface ContentsPayload {
  occupants: ContentEntry[]
  items: ContentEntry[]
  legend?: Record<string, string>
}

export interface VitalsPayload {
  health: number
  healthMax: number
  focus: number
  focusMax: number
  stamina: number
  staminaMax: number
  level: number
  xp: number
  path: string
  gold: number
}

/**
 * One pulse, in milliseconds (PLAN.md §2.3). The server speaks in pulses and the client counts
 * cooldowns down in wall-clock time, so this is the one place the two units meet.
 */
export const PULSE_MS = 250

/** One ability the character has, as the server describes it. */
export interface AbilityEntry {
  key: string
  name: string
  /** What to type. "cast bolt" for a spell, "kick" for a skill — `cast` refuses a skill. */
  verb: string
  costType: string
  costValue: number
  cooldownPulses: number
  /**
   * What was left of the cooldown when the server sent this. The client counts down from here
   * rather than being told again each pulse, so this is only ever a starting point — and the
   * reason the roster is resent on entry, which is how a reconnect resynchronises.
   */
  remainingPulses: number
  isSpell: boolean
}

export interface AbilitiesPayload {
  abilities: AbilityEntry[]
}

export interface CooldownPayload {
  key: string
  pulses: number
}

/** One member of the group, as the rest of the group sees them. */
export interface PartyMemberEntry {
  name: string
  level: number
  path: string
  health: number
  healthMax: number
  focus: number
  focusMax: number
  stamina: number
  staminaMax: number
  isLeader: boolean
  /** In the same room as the viewer. A split party is a normal state, not an error. */
  here: boolean
  linkDead: boolean
}

/**
 * The whole group, sent to each of its members. An empty list means "not in a group" — which is
 * what clears the panel when you leave one, rather than leaving the last roster on screen.
 */
export interface PartyPayload {
  members: PartyMemberEntry[]
}

export interface SysPayload {
  message: string
  kind: 'info' | 'warning' | 'disconnect'
}

export type GameEvent =
  | { type: 'text'; data: TextPayload }
  | { type: 'room'; data: RoomPayload }
  | { type: 'map'; data: MapPayload }
  | { type: 'contents'; data: ContentsPayload }
  | { type: 'vitals'; data: VitalsPayload }
  | { type: 'sys'; data: SysPayload }
  | { type: 'abilities'; data: AbilitiesPayload }
  | { type: 'cooldown'; data: CooldownPayload }
  | { type: 'party'; data: PartyPayload }

export const EVENT_TYPES = [
  'text',
  'room',
  'map',
  'contents',
  'vitals',
  'sys',
  'abilities',
  'cooldown',
  'party',
] as const
