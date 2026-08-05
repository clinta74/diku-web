import type { RoomDetail } from '../net/builderApi'

export interface PlacedRoom {
  room: RoomDetail
  x: number
  y: number
}

export const stepX = (direction: string) =>
  direction === 'east' ? 1 : direction === 'west' ? -1 : 0

export const stepY = (direction: string) =>
  direction === 'south' ? 1 : direction === 'north' ? -1 : 0

/**
 * Places a zone's rooms on the builder canvas (PLAN.md §7.2).
 *
 * Uses stored editor coordinates where they exist and walks the exit graph for the rest.
 * Walking rather than a force-directed layout because a MUD exit graph is already spatial:
 * north means up. Breadth-first from an anchor, one cell per exit, produces a map that reads
 * correctly. Dug rooms already arrive with the right coordinates (PLAN.md §7.6), so this
 * mostly serves hand-seeded or imported content.
 *
 * Guarantees, all of which have tests: every room is placed exactly once, no two rooms share
 * a cell, and rooms the graph never reaches still appear rather than vanishing off-canvas.
 */
export function layoutZone(rooms: RoomDetail[]): PlacedRoom[] {
  const byKey = new Map(rooms.map((r) => [r.key, r]))
  const placed = new Map<string, { x: number; y: number }>()
  const taken = new Set<string>()

  function claim(key: string, x: number, y: number) {
    let cx = Math.max(0, x)
    const cy = Math.max(0, y)

    // Two rooms on one cell would overlap and be unclickable. Nudging east is arbitrary but
    // deterministic, and the builder can drag it wherever they actually want it.
    while (taken.has(`${cx},${cy}`)) cx++

    placed.set(key, { x: cx, y: cy })
    taken.add(`${cx},${cy}`)
  }

  for (const room of rooms) {
    if (room.editorX !== null && room.editorY !== null) {
      claim(room.key, room.editorX, room.editorY)
    }
  }

  const queue = [...placed.keys()]

  // Nothing has coordinates at all: anchor on the first room and grow from there.
  if (queue.length === 0 && rooms.length > 0) {
    claim(rooms[0].key, 1, 1)
    queue.push(rooms[0].key)
  }

  while (queue.length > 0) {
    const key = queue.shift()!
    const room = byKey.get(key)
    const at = placed.get(key)
    if (!room || !at) continue

    for (const exit of room.exits) {
      // Skip rooms already placed, and exits leading out of this zone or at nothing at all -
      // a dangling exit has no room to position (PLAN.md §7.4).
      if (placed.has(exit.to) || !byKey.has(exit.to)) continue

      claim(exit.to, at.x + stepX(exit.direction), at.y + stepY(exit.direction))
      queue.push(exit.to)
    }
  }

  // Anything the walk never reached - an orphan, or a disconnected component - goes into a row
  // underneath rather than being left off the canvas, where it would be uneditable.
  let spare = 0
  const floor = placed.size === 0 ? 0 : Math.max(...[...placed.values()].map((p) => p.y)) + 2

  for (const room of rooms) {
    if (!placed.has(room.key)) claim(room.key, spare++, floor)
  }

  return rooms.map((room) => ({ room, ...(placed.get(room.key) ?? { x: 0, y: 0 }) }))
}
