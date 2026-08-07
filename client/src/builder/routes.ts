/**
 * Pure mapping between the builder's URL and its selection state (PLAN §4).
 *
 * The URL carries slug *segments* - `/builder/world/aldenmoor/millbrook/north-gate/flags` -
 * while the API speaks dotted composite *keys* - `aldenmoor`, `aldenmoor.millbrook`,
 * `aldenmoor.millbrook.north-gate`. Everything here is a pure function so it can be unit
 * tested without a router (the app is `environment: 'node'` under Vitest).
 */

export const SECTIONS = ['details', 'flags', 'terrain', 'exits', 'spawners'] as const
export type Section = (typeof SECTIONS)[number]

/** Rooms open on their prose first; a bare room URL redirects here. */
export const DEFAULT_SECTION: Section = 'details'

export type BuilderTab = 'world' | 'mobs' | 'items'

/** The route params react-router extracts from the `world` branch, each a single slug. */
export interface WorldRouteParams {
  world?: string
  zone?: string
  room?: string
  section?: string
}

/** The composite keys the selection resolves to, recomposed from the slug segments. */
export interface WorldSelection {
  worldKey: string | null
  zoneKey: string | null
  roomKey: string | null
  section: Section
}

function isSection(value: string | undefined): value is Section {
  return value !== undefined && (SECTIONS as readonly string[]).includes(value)
}

/** The last dotted segment of a composite key - its own slug. */
function slug(key: string): string {
  return key.slice(key.lastIndexOf('.') + 1)
}

/**
 * Recomposes composite keys from slug segments. A zone is only meaningful with its world,
 * and a room only with both, so a missing parent collapses everything below it to null -
 * which is exactly the "nothing selected yet" state the tree starts in.
 */
export function keysFromParams(params: WorldRouteParams): WorldSelection {
  const worldKey = params.world ?? null
  const zoneKey = worldKey && params.zone ? `${worldKey}.${params.zone}` : null
  const roomKey = zoneKey && params.room ? `${zoneKey}.${params.room}` : null

  return {
    worldKey,
    zoneKey,
    roomKey,
    // An unknown or absent section is not an error - it just means "the default one".
    section: isSection(params.section) ? params.section : DEFAULT_SECTION,
  }
}

/**
 * Builds the path for a world-tab selection. Deeper arguments are ignored once a shallower
 * one is null, so `toWorldPath('w', null, 'w.z.r')` is just `/builder/world/w` - you cannot
 * address a room without its zone.
 */
export function toWorldPath(
  worldKey: string | null,
  zoneKey?: string | null,
  roomKey?: string | null,
  section: Section = DEFAULT_SECTION,
): string {
  if (!worldKey) {
    return '/builder/world'
  }

  const parts = ['/builder/world', worldKey]

  if (zoneKey) {
    parts.push(slug(zoneKey))

    if (roomKey) {
      parts.push(slug(roomKey), section)
    }
  }

  return parts.join('/')
}

export function toMobsPath(templateKey?: string | null): string {
  return templateKey ? `/builder/mobs/${templateKey}` : '/builder/mobs'
}

export function toItemsPath(templateKey?: string | null): string {
  return templateKey ? `/builder/items/${templateKey}` : '/builder/items'
}