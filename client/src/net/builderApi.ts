/**
 * The builder API (PLAN.md §7.3). Same-origin and cookie-authorised like everything else;
 * the server additionally requires the Builder role, so every call here can 403.
 */

import { request } from './api'

export interface RoomFlagDefinition {
  key: string
  default: boolean
  summary: string
  phase: string
}

export interface WorldSummary {
  key: string
  name: string
  description: string
  sortOrder: number
  flags: Record<string, boolean>
  zoneCount: number
}

export interface ZoneSummary {
  key: string
  worldKey: string
  name: string
  description: string
  minLevel: number
  maxLevel: number
  flags: Record<string, boolean>
  roomCount: number
}

export interface RoomExit {
  direction: string
  to: string
  /** False for a dangling link - allowed, and worth drawing differently (PLAN.md §7.4). */
  targetExists: boolean
}

/** Where a flag's effective value came from, so inherited values can be shown as inherited. */
export interface ResolvedFlag {
  key: string
  value: boolean
  source: 'room' | 'zone' | 'world' | 'default'
  summary: string
}

export interface RoomDetail {
  key: string
  zoneKey: string
  title: string
  description: string
  flags: Record<string, boolean>
  resolved: ResolvedFlag[]
  grid: string[]
  legend: Record<string, string>
  editorX: number | null
  editorY: number | null
  exits: RoomExit[]
}

export interface ValidationWarning {
  kind: string
  entityKey: string
  message: string
}

export interface ZoneValidation {
  zoneKey: string
  warnings: ValidationWarning[]
}

export interface UnfinishedRoom {
  key: string
  title: string
  editorX: number | null
  editorY: number | null
}

export interface AuditEntry {
  id: string
  accountId: string | null
  username: string | null
  entityKind: string
  entityKey: string
  action: string
  at: string
}

export interface MobTemplate {
  key: string
  name: string
  description: string
  icon: string
  level: number
  wanderIntervalPulses: number
  baseStats: Record<string, unknown>
  baseXp: number
  baseGold: number
  loot: Array<Record<string, unknown>>
  behavior: Record<string, unknown>
}

export interface ItemTemplate {
  key: string
  name: string
  description: string
  icon: string
  slot: string | null
  weight: number
  baseValue: number
  baseStats: Record<string, unknown>
}

export interface Spawner {
  id: string
  zoneKey: string
  templateKey: string
  templateKind: 'Mob' | 'Item'
  roomKeys: string[]
  targetCount: number
  respawnSeconds: number
  sentinel: boolean
}

export interface Quest {
  key: string
  zoneKey: string
  name: string
  summary: string
  description: string
  giverMobKey: string
  turninMobKey: string
  requiredItemKey: string | null
  requiredCount: number
  rewardXp: number
  rewardGold: number
  rewardItemKey: string | null
  rewardItemCount: number
  prerequisiteQuestKeys: string[]
  isRepeatable: boolean
  dialogue: Record<string, string>
  sortOrder: number
}

const base = '/api/builder'

export const builderApi = {
  roomFlags: () => request<RoomFlagDefinition[]>(`${base}/room-flags`),

  worlds: () => request<WorldSummary[]>(`${base}/worlds`),

  createWorld: (key: string, body: Partial<WorldSummary>) =>
    request<WorldSummary>(`${base}/worlds/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateWorld: (key: string, body: Partial<WorldSummary>) =>
    request<WorldSummary>(`${base}/worlds/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteWorld: (key: string) =>
    request<void>(`${base}/worlds/${key}`, { method: 'DELETE' }),

  zones: (worldKey?: string) =>
    request<ZoneSummary[]>(worldKey ? `${base}/zones?world=${worldKey}` : `${base}/zones`),

  createZone: (key: string, body: Partial<ZoneSummary>) =>
    request<ZoneSummary>(`${base}/zones/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateZone: (key: string, body: Partial<ZoneSummary>) =>
    request<ZoneSummary>(`${base}/zones/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteZone: (key: string) => request<void>(`${base}/zones/${key}`, { method: 'DELETE' }),

  rooms: (zoneKey: string) => request<RoomDetail[]>(`${base}/zones/${zoneKey}/rooms`),

  room: (key: string) => request<RoomDetail>(`${base}/rooms/${key}`),

  createRoom: (key: string, body: Record<string, unknown>) =>
    request<RoomDetail>(`${base}/rooms/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateRoom: (key: string, body: Record<string, unknown>) =>
    request<RoomDetail>(`${base}/rooms/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteRoom: (key: string) => request<void>(`${base}/rooms/${key}`, { method: 'DELETE' }),

  renameRoom: (key: string, newKey: string) =>
    request<RoomDetail>(`${base}/rooms/${key}/rename`, {
      method: 'POST',
      body: JSON.stringify({ newKey }),
    }),

  /**
   * Creates and links a room, or materializes one a dangling exit already names - the server
   * picks from current state, so the caller never has to know which case it is (PLAN.md §7.6).
   */
  dig: (key: string, direction: string, options?: { reciprocal?: boolean; zoneKey?: string }) =>
    request<RoomDetail>(`${base}/rooms/${key}/dig`, {
      method: 'POST',
      body: JSON.stringify({ direction, reciprocal: options?.reciprocal ?? true, zoneKey: options?.zoneKey ?? null }),
    }),

  setExit: (key: string, direction: string, to: string, reciprocal = true) =>
    request<RoomDetail>(`${base}/rooms/${key}/exits/${direction}`, {
      method: 'PUT',
      body: JSON.stringify({ to, reciprocal }),
    }),

  removeExit: (key: string, direction: string) =>
    request<RoomDetail>(`${base}/rooms/${key}/exits/${direction}`, { method: 'DELETE' }),

  validate: (zoneKey: string) => request<ZoneValidation>(`${base}/zones/${zoneKey}/validate`),

  unfinished: (zoneKey: string) =>
    request<UnfinishedRoom[]>(`${base}/zones/${zoneKey}/unfinished`),

  audit: (kind: string, key: string) =>
    request<AuditEntry[]>(`${base}/audit?kind=${kind}&key=${encodeURIComponent(key)}`),

  mobTemplates: () => request<MobTemplate[]>(`${base}/mob-templates`),

  mobTemplate: (key: string) => request<MobTemplate>(`${base}/mob-templates/${key}`),

  createMobTemplate: (key: string, body: Partial<MobTemplate>) =>
    request<MobTemplate>(`${base}/mob-templates/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateMobTemplate: (key: string, body: Partial<MobTemplate>) =>
    request<MobTemplate>(`${base}/mob-templates/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteMobTemplate: (key: string) =>
    request<void>(`${base}/mob-templates/${key}`, { method: 'DELETE' }),

  itemTemplates: () => request<ItemTemplate[]>(`${base}/item-templates`),

  itemTemplate: (key: string) => request<ItemTemplate>(`${base}/item-templates/${key}`),

  createItemTemplate: (key: string, body: Partial<ItemTemplate>) =>
    request<ItemTemplate>(`${base}/item-templates/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateItemTemplate: (key: string, body: Partial<ItemTemplate>) =>
    request<ItemTemplate>(`${base}/item-templates/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteItemTemplate: (key: string) =>
    request<void>(`${base}/item-templates/${key}`, { method: 'DELETE' }),

  spawners: (zoneKey?: string) =>
    request<Spawner[]>(zoneKey ? `${base}/spawners?zone=${zoneKey}` : `${base}/spawners`),

  spawner: (id: string) => request<Spawner>(`${base}/spawners/${id}`),

  createSpawner: (body: Partial<Spawner>) =>
    request<Spawner>(`${base}/spawners`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateSpawner: (id: string, body: Partial<Spawner>) =>
    request<Spawner>(`${base}/spawners/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteSpawner: (id: string) =>
    request<void>(`${base}/spawners/${id}`, { method: 'DELETE' }),

  quests: () => request<Quest[]>(`${base}/quests`),

  quest: (key: string) => request<Quest>(`${base}/quests/${key}`),

  createQuest: (key: string, body: Partial<Quest>) =>
    request<Quest>(`${base}/quests/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateQuest: (key: string, body: Partial<Quest>) =>
    request<Quest>(`${base}/quests/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteQuest: (key: string) =>
    request<void>(`${base}/quests/${key}`, { method: 'DELETE' }),

  questReachability: (key: string) =>
    request<{ questKey: string; warnings: Array<{ type: string; item?: string }> }>(
      `${base}/quests/${key}/reachability`,
    ),

  storyline: (zoneKey: string) =>
    request<{
      zoneKey: string
      nodes: Array<{ key: string; name: string }>
      edges: Array<{ from: string; to: string }>
      cycles: string[]
      deadEnds: string[]
    }>(`${base}/zones/${zoneKey}/storyline`),
}

export const DIRECTIONS = ['north', 'east', 'south', 'west', 'up', 'down'] as const

export const OPPOSITE: Record<string, string> = {
  north: 'south',
  south: 'north',
  east: 'west',
  west: 'east',
  up: 'down',
  down: 'up',
}
