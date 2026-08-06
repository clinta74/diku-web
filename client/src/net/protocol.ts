/**
 * Mirrors DikuWeb.Engine.Protocol. Kept hand-written rather than generated: the surface is
 * small, and a mismatch shows up immediately in the reducer tests.
 */

export interface TextSpan {
  t: string
  s?: string | null
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

export const EVENT_TYPES = ['text', 'room', 'map', 'contents', 'vitals', 'sys'] as const
