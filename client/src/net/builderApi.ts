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

/**
 * The §4.4 difficulty dial. Composition is `round(base × world × zone)`, so 1.0 everywhere is
 * "no change" and the two levels multiply rather than override.
 */
export interface Multipliers {
  /** Master dial: scales health and damage together. */
  strength: number
  health: number
  damage: number
  xp: number
  gold: number
  itemValue: number
  itemPower: number
  /** Spawner target counts — makes a zone crowded, not just tougher. */
  spawnDensity: number
}

export const MULTIPLIER_KEYS: Array<keyof Multipliers> = [
  'strength',
  'health',
  'damage',
  'xp',
  'gold',
  'itemValue',
  'itemPower',
  'spawnDensity',
]

export const NEUTRAL_MULTIPLIERS: Multipliers = {
  strength: 1,
  health: 1,
  damage: 1,
  xp: 1,
  gold: 1,
  itemValue: 1,
  itemPower: 1,
  spawnDensity: 1,
}

export interface WorldSummary {
  key: string
  name: string
  description: string
  sortOrder: number
  flags: Record<string, boolean>
  multipliers: Multipliers
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
  multipliers: Multipliers
  roomCount: number
}

/** One template's stats before and after this zone's multipliers, for the §7.5 preview table. */
export interface MultiplierPreviewRow {
  templateKey: string
  templateName: string
  kind: string
  baseStats: Record<string, unknown>
  resolvedStats: Record<string, number>
}

export interface MultiplierPreview {
  zoneKey: string
  worldMultipliers: Record<string, number>
  zoneMultipliers: Record<string, number>
  templates: MultiplierPreviewRow[]
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

/** One of a mob's attacks. Each entry runs on its own timer. */
export interface MobAttack {
  /** Base-form verb: "bite" narrates "A wolf bites you". */
  verb: string
  /** Pulses between swings of this attack. Minimum 4 (1 second). */
  delayPulses: number
  /** Scales this attack against the mob's damage. Null means 1.0. */
  damageMultiplier: number | null
  /**
   * An effect applied on a landed hit, keyed as the engine's `EffectRegistry` knows it. Null for
   * a plain attack. This is how a mob stuns, snares, or bleeds — it has attacks rather than a
   * spellbook (PLAN.md §12).
   */
  effectKey: string | null
  /** Parameters for `effectKey`. Strings, because that is what the executors parse. */
  effectParams: Record<string, string> | null
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
  attacks: MobAttack[]
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
  /** Pulses between swings when wielded. Null means no declared speed. */
  attackDelayPulses: number | null
  /** Base-form verb describing how it strikes: "slash", "crush". */
  attackVerb: string | null
  /** Bound to a quest: cannot be sold or destroyed, but can still be dropped (PLAN.md §4.9). */
  isQuestItem: boolean
}

/**
 * Whether mobs from a spawner wander. `template` defers to the mob template, which carries the
 * default (PLAN.md §4.8); the other two override it for this placement.
 *
 * A word rather than a boolean because the server's value is three-valued and every field of a
 * spawner PATCH is optional — a nullable bool could not tell "leave this alone" from "follow the
 * template".
 */
export type WanderMode = 'template' | 'always' | 'never'

export type CharacterPath = 'Warden' | 'Adept' | 'Shade' | 'Hallow'
export type CostType = 'Focus' | 'Stamina' | 'Health'
export type TargetingType = 'SingleTarget' | 'Self' | 'Aoe'

/** What the validator says about a stored ability. Errors are refused on save. */
export interface AbilityProblem {
  severity: 'Error' | 'Warning'
  message: string
}

/** One effect an ability applies. PascalCase on the wire, matching the stored jsonb. */
export interface AbilityEffectSpec {
  key: string
  params: Record<string, string>
}

export interface Ability {
  key: string
  path: CharacterPath
  unlockLevel: number
  name: string
  description: string
  costType: CostType
  costValue: number
  cooldownPulses: number
  castTimePulses: number | null
  targetingType: TargetingType
  /**
   * What the ability does, in order. One entry is the ordinary case; several let one ability do
   * several things — the reason Last Stand can raise maximum health *and* harden defence rather
   * than being written as a heal.
   */
  effects: AbilityEffectSpec[]
  /**
   * Carried on every read, not just returned from a save. A row can arrive by import or by hand,
   * and then nobody ever saw a refusal — so this list is the only place a builder finds out.
   */
  problems: AbilityProblem[]
}

export interface Spawner {
  id: string
  zoneKey: string
  templateKey: string
  templateKind: 'Mob' | 'Item'
  roomKeys: string[]
  targetCount: number
  respawnSeconds: number
  wander: WanderMode
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
  autoStart: boolean
  dialogue: Record<string, string>
  sortOrder: number
}

/**
 * One reason a quest could not be finished. Advisory: an unfinishable quest still saves, it just
 * should not be a surprise (PLAN.md §7.4).
 */
export interface ReachabilityWarning {
  kind: string
  message: string
  itemKey: string | null
  mobKey: string | null
}

export interface QuestReachability {
  questKey: string
  warnings: ReachabilityWarning[]
}

/** A quest in the chain graph. `external` marks a prerequisite that lives in another zone. */
export interface StorylineNode {
  key: string
  name: string
  zoneKey: string
  external: boolean
}

/** `from` must be completed before `to` can be offered. */
export interface StorylineEdge {
  from: string
  to: string
}

export interface Storyline {
  zoneKey: string
  nodes: StorylineNode[]
  edges: StorylineEdge[]
  /** Quests sitting on a prerequisite cycle. Every one of them is unstartable. */
  cycles: string[]
  /** Quests whose prerequisites can never all be met. */
  unreachable: string[]
  /** Prerequisites naming a quest that does not exist. */
  missingPrerequisites: Array<{ quest: string; missing: string }>
}

/**
 * The four dialogue strings a quest can override (PLAN.md §4.9), keyed exactly as
 * `QuestCommands` reads them. Each falls back to generated prose when absent, so leaving one
 * blank is a real choice rather than a hole.
 */
export const DIALOGUE_KEYS = [
  'giverOffer',
  'giverInProgress',
  'giverComplete',
  'turninReady',
] as const

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

  /** @see setRoomFlag — same three states, one scope up. */
  setWorldFlag: (key: string, flag: string, value: boolean | null) =>
    request<WorldSummary>(`${base}/worlds/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
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

  /** @see setRoomFlag — same three states, one scope up. */
  setZoneFlag: (key: string, flag: string, value: boolean | null) =>
    request<ZoneSummary>(`${base}/zones/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  deleteZone: (key: string) => request<void>(`${base}/zones/${key}`, { method: 'DELETE' }),

  /**
   * How every template in a zone resolves under the current multipliers (PLAN.md §7.5).
   * The endpoint has existed since Phase 3; nothing called it until the multipliers became
   * editable, so the panel it was written for never got built.
   */
  zonePreview: (zoneKey: string) =>
    request<MultiplierPreview>(`${base}/zones/${zoneKey}/preview`),

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

  /**
   * Sets one flag without sending the whole map. `null` clears the key so the zone or world
   * decides again. Narrow by design: a full-object room PATCH replaces every flag, which
   * quietly discards whatever another builder changed in the meantime (PLAN §1).
   */
  setRoomFlag: (key: string, flag: string, value: boolean | null) =>
    request<RoomDetail>(`${base}/rooms/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  validate: (zoneKey: string) => request<ZoneValidation>(`${base}/zones/${zoneKey}/validate`),

  unfinished: (zoneKey: string) =>
    request<UnfinishedRoom[]>(`${base}/zones/${zoneKey}/unfinished`),

  audit: (kind: string, key: string) =>
    request<AuditEntry[]>(`${base}/audit?kind=${kind}&key=${encodeURIComponent(key)}`),

  abilities: () => request<Ability[]>(`${base}/abilities`),

  ability: (key: string) => request<Ability>(`${base}/abilities/${key}`),

  createAbility: (key: string, body: Partial<Ability>) =>
    request<Ability>(`${base}/abilities/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateAbility: (key: string, body: Partial<Ability>) =>
    request<Ability>(`${base}/abilities/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteAbility: (key: string) =>
    request<void>(`${base}/abilities/${key}`, { method: 'DELETE' }),

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
    request<QuestReachability>(`${base}/quests/${key}/reachability`),

  storyline: (zoneKey: string) => request<Storyline>(`${base}/zones/${zoneKey}/storyline`),
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
