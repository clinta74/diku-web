import { describe, expect, it } from 'vitest'
import { layoutZone } from './layout'
import type { RoomDetail } from '../net/builderApi'

function room(
  slug: string,
  exits: { direction: string; to: string; targetExists?: boolean }[] = [],
  editor?: { x: number; y: number },
): RoomDetail {
  return {
    key: `w.z.${slug}`,
    zoneKey: 'w.z',
    title: slug,
    description: '',
    flags: {},
    resolved: [],
    grid: [],
    legend: {},
    editorX: editor?.x ?? null,
    editorY: editor?.y ?? null,
    exits: exits.map((e) => ({
      direction: e.direction,
      to: e.to,
      targetExists: e.targetExists ?? true,
      // Layout is geography and does not care who may pass, but an exit carries its conditions
      // (PLAN.md §4.15) and the fixture has to be a whole one.
      requiredFlagKey: null,
      requiredItemKey: null,
      refusalMessage: null,
    })),
  }
}

const at = (placed: ReturnType<typeof layoutZone>, slug: string) => {
  const found = placed.find((p) => p.room.key === `w.z.${slug}`)
  return found ? { x: found.x, y: found.y } : null
}

describe('zone canvas layout', () => {
  it('honours stored editor coordinates', () => {
    const placed = layoutZone([room('a', [], { x: 3, y: 4 })])

    expect(at(placed, 'a')).toEqual({ x: 3, y: 4 })
  })

  it('walks the exit graph for rooms with no coordinates', () => {
    // North is up and east is right, because a MUD exit graph is already spatial.
    const placed = layoutZone([
      room('centre', [{ direction: 'north', to: 'w.z.up' }, { direction: 'east', to: 'w.z.right' }], {
        x: 5,
        y: 5,
      }),
      room('up'),
      room('right'),
    ])

    expect(at(placed, 'up')).toEqual({ x: 5, y: 4 })
    expect(at(placed, 'right')).toEqual({ x: 6, y: 5 })
  })

  it('anchors somewhere sensible when nothing has coordinates', () => {
    const placed = layoutZone([
      room('first', [{ direction: 'south', to: 'w.z.second' }]),
      room('second'),
    ])

    // The anchor sits at (3,3) so up/down diagonals have room to grow in either direction
    // without the whole map having to shift.
    expect(at(placed, 'first')).toEqual({ x: 3, y: 3 })
    expect(at(placed, 'second')).toEqual({ x: 3, y: 4 })
  })

  it('sends up down-right and down up-left (quadrants IV and II)', () => {
    // The convention a builder asked for: walking up lands the new room below-right of the
    // one you left, walking down lands it above-left.
    const placed = layoutZone([
      room('centre', [
        { direction: 'up', to: 'w.z.above' },
        { direction: 'down', to: 'w.z.below' },
      ], { x: 5, y: 5 }),
      room('above'),
      room('below'),
    ])

    expect(at(placed, 'above')).toEqual({ x: 6, y: 6 })
    expect(at(placed, 'below')).toEqual({ x: 4, y: 4 })
  })

  it('nudges a colliding room off the cell its direction wanted', () => {
    // 'a' and 'b' both point east into 'c' and 'd' from the same column; the second placement
    // collides and is nudged east. Position is therefore NOT authoritative for direction -
    // which is why shift-click linking must ask rather than infer from coordinates.
    const placed = layoutZone([
      room('a', [{ direction: 'east', to: 'w.z.shared' }], { x: 1, y: 1 }),
      room('b', [{ direction: 'east', to: 'w.z.shared2' }], { x: 1, y: 2 }),
      room('shared'),
      room('shared2'),
    ])

    const cells = placed.map((p) => `${p.x},${p.y}`)
    expect(new Set(cells).size).toBe(4)
  })

  it('never puts two rooms on the same cell', () => {
    // Two rooms both claiming (2,2) would overlap and the one underneath would be
    // unclickable, which is worse than being nudged one cell over.
    const placed = layoutZone([
      room('a', [], { x: 2, y: 2 }),
      room('b', [], { x: 2, y: 2 }),
      room('c', [], { x: 2, y: 2 }),
    ])

    const cells = placed.map((p) => `${p.x},${p.y}`)
    expect(new Set(cells).size).toBe(3)
  })

  it('places every room exactly once', () => {
    const placed = layoutZone([
      room('a', [{ direction: 'east', to: 'w.z.b' }]),
      room('b', [{ direction: 'east', to: 'w.z.c' }]),
      room('c'),
    ])

    expect(placed).toHaveLength(3)
    expect(new Set(placed.map((p) => p.room.key)).size).toBe(3)
  })

  it('still shows a room nothing links to', () => {
    // An orphan is exactly the room a builder needs to find and fix, so dropping it off the
    // canvas would hide the problem it is there to reveal.
    const placed = layoutZone([
      room('linked', [], { x: 0, y: 0 }),
      room('orphan'),
    ])

    expect(at(placed, 'orphan')).not.toBeNull()
  })

  it('ignores exits that lead out of the zone or nowhere at all', () => {
    // A dangling exit has no room to position (PLAN.md §7.4), and a cross-zone exit's target
    // is not in this list. Neither may throw or invent a placement.
    const placed = layoutZone([
      room('a', [
        { direction: 'north', to: 'w.z.missing', targetExists: false },
        { direction: 'east', to: 'other.zone.room' },
      ]),
    ])

    expect(placed).toHaveLength(1)
    expect(at(placed, 'a')).toEqual({ x: 3, y: 3 })
  })

  it('keeps every coordinate on the canvas', () => {
    // Walking west from x=0 would otherwise produce -1, which renders off the left edge.
    const placed = layoutZone([
      room('edge', [{ direction: 'west', to: 'w.z.beyond' }], { x: 0, y: 0 }),
      room('beyond'),
    ])

    for (const { x, y } of placed) {
      expect(x).toBeGreaterThanOrEqual(0)
      expect(y).toBeGreaterThanOrEqual(0)
    }
  })

  it('handles an empty zone', () => {
    expect(layoutZone([])).toEqual([])
  })

  it('does not loop forever on a cyclic exit graph', () => {
    const placed = layoutZone([
      room('a', [{ direction: 'east', to: 'w.z.b' }]),
      room('b', [{ direction: 'west', to: 'w.z.a' }]),
    ])

    expect(placed).toHaveLength(2)
  })
})
