import { describe, expect, it } from 'vitest'
import {
  DEFAULT_SECTION,
  keysFromParams,
  toItemsPath,
  toMobsPath,
  toQuestsPath,
  toRoomPath,
  toWorldPath,
  type Section,
  type WorldRouteParams,
} from './routes'

/**
 * Mimics how react-router would split a `/builder/world/...` path into params, so the two
 * pure functions can be exercised as inverses without pulling the router into a node test.
 */
function paramsFromWorldPath(path: string): WorldRouteParams {
  const rest = path.replace(/^\/builder\/world\/?/, '')
  const [world, zone, room, section] = rest.split('/').filter(Boolean)
  return { world, zone, room, section }
}

describe('toWorldPath', () => {
  it('stops at the shallowest null selection', () => {
    expect(toWorldPath(null)).toBe('/builder/world')
    expect(toWorldPath('aldenmoor')).toBe('/builder/world/aldenmoor')
    expect(toWorldPath('aldenmoor', 'aldenmoor.millbrook')).toBe(
      '/builder/world/aldenmoor/millbrook',
    )
  })

  it('cannot address a room without its zone', () => {
    // A room key is passed but the zone is null, so everything below the world is dropped.
    expect(toWorldPath('aldenmoor', null, 'aldenmoor.millbrook.north-gate')).toBe(
      '/builder/world/aldenmoor',
    )
  })

  it('emits the section only for a fully-qualified room, defaulting when omitted', () => {
    expect(
      toWorldPath('aldenmoor', 'aldenmoor.millbrook', 'aldenmoor.millbrook.north-gate'),
    ).toBe('/builder/world/aldenmoor/millbrook/north-gate/details')

    expect(
      toWorldPath('aldenmoor', 'aldenmoor.millbrook', 'aldenmoor.millbrook.north-gate', 'flags'),
    ).toBe('/builder/world/aldenmoor/millbrook/north-gate/flags')
  })
})

describe('keysFromParams', () => {
  it('recomposes composite keys from slug segments', () => {
    expect(
      keysFromParams({ world: 'aldenmoor', zone: 'millbrook', room: 'north-gate', section: 'exits' }),
    ).toEqual({
      worldKey: 'aldenmoor',
      zoneKey: 'aldenmoor.millbrook',
      roomKey: 'aldenmoor.millbrook.north-gate',
      section: 'exits',
    })
  })

  it('collapses a child whose parent is missing to null', () => {
    // zone slug present but no world - the whole chain is unselected.
    expect(keysFromParams({ zone: 'millbrook' })).toEqual({
      worldKey: null,
      zoneKey: null,
      roomKey: null,
      section: DEFAULT_SECTION,
    })
  })

  it('falls back to the default section for an absent or unknown value', () => {
    expect(keysFromParams({ world: 'aldenmoor' }).section).toBe(DEFAULT_SECTION)
    expect(
      keysFromParams({ world: 'a', zone: 'z', room: 'r', section: 'not-a-section' }).section,
    ).toBe(DEFAULT_SECTION)
  })
})

describe('round trip', () => {
  const cases: {
    worldKey: string | null
    zoneKey: string | null
    roomKey: string | null
    section?: Section
  }[] = [
    { worldKey: null, zoneKey: null, roomKey: null },
    { worldKey: 'aldenmoor', zoneKey: null, roomKey: null },
    { worldKey: 'aldenmoor', zoneKey: 'aldenmoor.millbrook', roomKey: null },
    {
      worldKey: 'aldenmoor',
      zoneKey: 'aldenmoor.millbrook',
      roomKey: 'aldenmoor.millbrook.north-gate',
      section: 'terrain',
    },
  ]

  it.each(cases)('path → params → keys recovers %o', (selection) => {
    const path = toWorldPath(selection.worldKey, selection.zoneKey, selection.roomKey, selection.section)
    const recovered = keysFromParams(paramsFromWorldPath(path))

    expect(recovered.worldKey).toBe(selection.worldKey)
    expect(recovered.zoneKey).toBe(selection.zoneKey)
    expect(recovered.roomKey).toBe(selection.roomKey)
    if (selection.roomKey) {
      expect(recovered.section).toBe(selection.section ?? DEFAULT_SECTION)
    }
  })
})

describe('template tab paths', () => {
  it('maps template keys straight through as single segments', () => {
    expect(toMobsPath()).toBe('/builder/mobs')
    expect(toMobsPath('warden-mentor')).toBe('/builder/mobs/warden-mentor')
    expect(toItemsPath()).toBe('/builder/items')
    expect(toItemsPath('rusted-blade')).toBe('/builder/items/rusted-blade')
  })

  it('maps quest keys the same way', () => {
    // A quest key is one segment, not the dotted composite a room uses - the server validates
    // it with IsKeySegment, so `zone.quest` would be refused on create.
    expect(toQuestsPath()).toBe('/builder/quests')
    expect(toQuestsPath('errand-for-mira')).toBe('/builder/quests/errand-for-mira')
  })
})

/**
 * The one-argument form, for callers that hold a room key and nothing above it — a spawner, a
 * placement row (§7.9). The split is the part worth pinning: a room key is `world.zone.room`, so
 * the zone is the first *two* segments and a caller doing this by hand tends to take the second.
 */
describe('toRoomPath', () => {
  it('recomposes the world and zone from the room key', () => {
    expect(toRoomPath('aldenmoor.millbrook.north-gate')).toBe(
      '/builder/world/aldenmoor/millbrook/north-gate/details',
    )
  })

  it('agrees with the three-argument form it delegates to', () => {
    expect(toRoomPath('aldenmoor.millbrook.north-gate', 'spawners')).toBe(
      toWorldPath('aldenmoor', 'aldenmoor.millbrook', 'aldenmoor.millbrook.north-gate', 'spawners'),
    )
  })

  it('degrades to the deepest thing a short key can address', () => {
    // Content is routinely wired before the thing it points at exists (§7.4), and a spawner's
    // room keys are stored as free strings - so a malformed one must route somewhere calm rather
    // than build a path with an undefined segment in it.
    expect(toRoomPath('aldenmoor.millbrook')).toBe('/builder/world/aldenmoor/millbrook')
    expect(toRoomPath('aldenmoor')).toBe('/builder/world/aldenmoor')
    expect(toRoomPath('')).toBe('/builder/world')
  })
})

/**
 * The server builds these same paths in `BuilderLinks.cs`, for the deep links `examine` and
 * `stats` hand a builder. Nothing connects the two but agreement, and a drift fails quietly —
 * the link routes to an empty tab rather than erroring — so the shapes are pinned here.
 */
describe('deep links handed out by the game', () => {
  it('matches BuilderLinks.ToItem', () => {
    expect(toItemsPath('rusted-blade')).toBe('/builder/items/rusted-blade')
  })

  it('matches BuilderLinks.ToMob', () => {
    expect(toMobsPath('giant-rat')).toBe('/builder/mobs/giant-rat')
  })

  it('matches BuilderLinks.ToRoom, which slugs each segment', () => {
    // The server has RoomKey "aldenmoor.millbrook.north-gate" and emits the slugged form.
    expect(toWorldPath('aldenmoor', 'aldenmoor.millbrook', 'aldenmoor.millbrook.north-gate')).toBe(
      '/builder/world/aldenmoor/millbrook/north-gate/details',
    )
  })

  it('round-trips a server-built room link back to the keys it names', () => {
    const path = '/builder/world/aldenmoor/millbrook/north-gate/details'
    const recovered = keysFromParams(paramsFromWorldPath(path))

    expect(recovered.worldKey).toBe('aldenmoor')
    expect(recovered.zoneKey).toBe('aldenmoor.millbrook')
    expect(recovered.roomKey).toBe('aldenmoor.millbrook.north-gate')
  })
})
