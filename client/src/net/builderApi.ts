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

/** What bundle format the running server reads. See `builderApi.bundleFormat`. */
export interface BundleFormatInfo {
  formatVersion: number
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
  /** Spawner target counts — makes a zone crowded, not just tougher. */
}

export const MULTIPLIER_KEYS: Array<keyof Multipliers> = [
  'strength',
  'health',
  'damage',
  'xp',
  'gold',
  'itemValue',
]

export const NEUTRAL_MULTIPLIERS: Multipliers = {
  strength: 1,
  health: 1,
  damage: 1,
  xp: 1,
  gold: 1,
  itemValue: 1,
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
  /** The same keys as `resolvedStats`, unscaled — `baseStats` cannot be joined against for a range. */
  baseValues: Record<string, number>
  /** Zero for an item, which has no level. */
  templateLevel: number
  /** What a mob spawned in this zone actually fights at (PLAN.md §4.7). */
  fightsAtLevel: number
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
  /** A character flag needed to pass, or null for a way anyone may take (PLAN.md §4.15). */
  requiredFlagKey: string | null
  /** An item template key the character must be carrying. Never consumed. */
  requiredItemKey: string | null
  /** What someone turned away is told. Null falls back to a generic line. */
  refusalMessage: string | null
}

/** What gates an exit. Every field null is an exit anyone may use (PLAN.md §4.15). */
export interface ExitConditions {
  requiredFlagKey: string | null
  requiredItemKey: string | null
  refusalMessage: string | null
  /**
   * Whether the conditions also apply to the reciprocal edge. Defaults false where `reciprocal`
   * defaults true, because you can always leave a vault.
   */
  reciprocalConditions?: boolean
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
  /** One only, counting what is worn. Checked on pick-up, purchase, gift, and quest reward. */
  isLore: boolean
  /** Cannot be dropped or given away. Destroying it is still allowed, deliberately. */
  isNoDrop: boolean
  /** Lights the room while worn or wielded. Any slot; carrying it in the pack is not enough. */
  isLightSource: boolean
  /**
   * The Paths that may wear or wield this. **Empty means anyone**, which is why it is a list of
   * what is allowed rather than of what is forbidden — an item is unrestricted until a builder
   * opts in.
   *
   * Strings rather than numbers: the server registers a `JsonStringEnumConverter` globally, so
   * `CharacterPath` crosses the wire as "Warden" rather than 0.
   */
  paths: CharacterPath[]
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

/**
 * The four Paths, in the server enum's own order.
 *
 * A value rather than only a type, because a form that offers all four has to iterate them — and
 * a hand-written list beside the union is the pair that drifts. `CharacterPath` is derived from
 * this so the two cannot disagree.
 */
export const CHARACTER_PATHS = ['Warden', 'Adept', 'Shade', 'Hallow'] as const

export type CharacterPath = (typeof CHARACTER_PATHS)[number]
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
  /** Seconds before one lost instance is replaced. How rare the thing is. */
  respawnSeconds: number
  wander: WanderMode
  /**
   * What mobs from this spawner fight at (PLAN.md §4.7). Server-computed and read-only; zero for
   * an item spawner. Sending it back is ignored — see `SpawnerSave`.
   *
   * It reports the outcome whether the level was pinned or derived from the zone; `level` says
   * which.
   */
  fightsAtLevel: number
  /**
   * Where `fightsAtLevel` came from: `'zone'`, or the pinned level as a decimal string.
   *
   * A word rather than a nullable number for the reason `wander` is one — on a PATCH, null already
   * means "leave this alone", so a nullable number could not also spell "clear the pin".
   */
  level: string
}

/**
 * The subset of a spawner a client may write.
 *
 * `createSpawner`/`updateSpawner` used to take `Partial<Spawner>`, which now advertises the
 * server-computed `fightsAtLevel` as though setting it did something.
 */
export type SpawnerSave = Partial<Omit<Spawner, 'id' | 'fightsAtLevel'>>

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
  /**
   * A character flag granted on completion, or null (PLAN.md §4.15). How attunement is earned,
   * and the only thing that opens a gate requiring that flag.
   */
  rewardFlagKey: string | null
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

/**
 * A named starter configuration (PLAN.md §4.16): where a new character wakes up and what the game
 * says to them. A server holds several and exactly one is live.
 */
export interface GameConfiguration {
  key: string
  name: string
  description: string
  startingRoomKey: string
  welcomeMessage: string
  isActive: boolean
  /**
   * False when the starting room names a room this server does not have. Advisory: writing a
   * configuration before importing the world it points into is the normal order of operations.
   */
  startingRoomExists: boolean
  updatedAt: string
}

export interface GameConfigurationList {
  configurations: GameConfiguration[]
  /** What the running loop is obeying, which is not always what a row says. */
  activeStartingRoomKey: string
  activeWelcomeMessage: string
}

export interface ImportCount {
  kind: string
  created: number
  updated: number
}

export interface ImportFailure {
  kind: string
  key: string
  message: string
}

/** What an import did, or - under dryRun - what it would have done. */
export interface ImportReport {
  formatVersion: number
  dryRun: boolean
  counts: ImportCount[]
  warnings: ValidationWarning[]
  failures: ImportFailure[]
  ok: boolean
}

export const builderApi = {
  roomFlags: () => request<RoomFlagDefinition[]>(`${base}/room-flags`),

  /**
   * The bundle format version this server accepts.
   *
   * Asked so the Transfer panel can compare a file against the server *before* uploading it. The
   * format version is the import path's only hard refusal, and without this the one way to find out
   * a server had not been updated yet was to send it a bundle and read the 400.
   */
  bundleFormat: () => request<BundleFormatInfo>(`${base}/bundle-format`),

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

  /**
   * A PUT states the whole exit, so conditions left out are conditions it does not have - which
   * is what lets a lock be taken off again (PLAN.md §4.15).
   */
  setExit: (
    key: string,
    direction: string,
    to: string,
    reciprocal = true,
    conditions?: ExitConditions,
  ) =>
    request<RoomDetail>(`${base}/rooms/${key}/exits/${direction}`, {
      method: 'PUT',
      body: JSON.stringify({
        to,
        reciprocal,
        requiredFlagKey: conditions?.requiredFlagKey ?? null,
        requiredItemKey: conditions?.requiredItemKey ?? null,
        refusalMessage: conditions?.refusalMessage ?? null,
        reciprocalConditions: conditions?.reciprocalConditions ?? false,
      }),
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

  createSpawner: (body: SpawnerSave) =>
    request<Spawner>(`${base}/spawners`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateSpawner: (id: string, body: SpawnerSave) =>
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

  configurations: () => request<GameConfigurationList>(`${base}/configurations`),

  saveConfiguration: (
    key: string,
    body: Pick<GameConfiguration, 'name' | 'description' | 'startingRoomKey' | 'welcomeMessage'>,
  ) =>
    request<GameConfiguration>(`${base}/configurations/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  deleteConfiguration: (key: string) =>
    request<void>(`${base}/configurations/${key}`, { method: 'DELETE' }),

  /** Makes one live. Takes effect for the next character to enter the game, with no restart. */
  activateConfiguration: (key: string) =>
    request<GameConfiguration>(`${base}/configurations/${key}/activate`, { method: 'POST' }),

  /**
   * The whole authored world as one JSON document, or one world, or one zone.
   *
   * A plain URL rather than a fetch: the response carries a Content-Disposition attachment with a
   * dated filename, so letting the browser follow it saves a file somebody keeps. Reading it into
   * a blob here would throw that name away and hand back an untitled download.
   */
  exportUrl: (scope?: { world?: string; zone?: string }) => {
    const query = new URLSearchParams()
    if (scope?.zone) query.set('zone', scope.zone)
    else if (scope?.world) query.set('world', scope.world)
    const suffix = query.toString()
    return suffix ? `${base}/export?${suffix}` : `${base}/export`
  },

  /**
   * Applies a bundle. `dryRun` reports what would happen and changes nothing.
   *
   * The bundle is sent as already-parsed JSON rather than as text, so a malformed file fails in
   * the browser with a parse error naming the position instead of arriving at the server as a
   * 400 that says only that the body was unreadable.
   */
  importBundle: (bundle: unknown, dryRun: boolean) =>
    request<ImportReport>(`${base}/import${dryRun ? '?dryRun=true' : ''}`, {
      method: 'POST',
      body: JSON.stringify(bundle),
    }),

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
