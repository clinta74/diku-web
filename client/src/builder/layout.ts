import type { RoomDetail } from '../net/builderApi'

export interface PlacedRoom {
  room: RoomDetail
  x: number
  y: number
}

export const stepX = (direction: string) => {
  switch (direction) {
    case 'east':
      return 1
    case 'west':
      return -1
    case 'up':
      return 1 // Up lands the new room down-right (quadrant IV)
    case 'down':
      return -1 // Down lands the new room up-left (quadrant II)
    default:
      return 0
  }
}

export const stepY = (direction: string) => {
  switch (direction) {
    case 'north':
      return -1
    case 'south':
      return 1
    case 'up':
      return 1 // Up lands the new room down-right (quadrant IV)
    case 'down':
      return -1 // Down lands the new room up-left (quadrant II)
    default:
      return 0
  }
}

/**
 * Places a zone's rooms on the builder canvas based on exit topology (PLAN.md §7.2).
 *
 * Single-pass BFS from anchor room with collision-driven spacing:
 * - Each exit places the next room in the exit direction
 * - Collision avoidance nudges rooms east when needed
 * - Naturally handles UP/DOWN as diagonal moves that don't collapse cycles
 * - Preserves accurate spatial relationships between connected rooms
 *
 * Results in:
 * - North: up, South: down, East: right, West: left
 * - Up: lower-right diagonal (quadrant IV), Down: upper-left diagonal (quadrant II)
 * - Complex branching handled through collision nudging, not re-layout
 * - Unreachable rooms placed at the bottom
 */
export function layoutZone(rooms: RoomDetail[]): PlacedRoom[] {
  const byKey = new Map(rooms.map((r) => [r.key, r]))
  const placed = new Map<string, { x: number; y: number }>()
  const taken = new Set<string>()

  function claim(key: string, x: number, y: number) {
    let cx = x
    let cy = y

    // Shift entire map if we go negative
    if (cx < 0 || cy < 0) {
      const minX = Math.min(0, cx, ...[...placed.values()].map((p) => p.x))
      const minY = Math.min(0, cy, ...[...placed.values()].map((p) => p.y))

      if (minX < 0 || minY < 0) {
        const shifted = new Map<string, { x: number; y: number }>()
        for (const [k, pos] of placed) {
          shifted.set(k, { x: pos.x - minX, y: pos.y - minY })
        }
        placed.clear()
        for (const [k, pos] of shifted) {
          placed.set(k, pos)
        }
        taken.clear()
        for (const pos of placed.values()) {
          taken.add(`${pos.x},${pos.y}`)
        }
      }

      cx = cx - minX
      cy = cy - minY
    }

    // Collision avoidance: nudge east if needed
    while (taken.has(`${cx},${cy}`)) cx++

    placed.set(key, { x: cx, y: cy })
    taken.add(`${cx},${cy}`)
  }

  // Start with rooms that have explicit coordinates
  for (const room of rooms) {
    if (room.editorX !== null && room.editorY !== null) {
      claim(room.key, room.editorX, room.editorY)
    }
  }

  const queue = [...placed.keys()]

  // No explicit coordinates: anchor on first room and grow from exits
  if (queue.length === 0 && rooms.length > 0) {
    claim(rooms[0].key, 3, 3)
    queue.push(rooms[0].key)
  }

  // BFS walk: position rooms based on exit directions
  while (queue.length > 0) {
    const key = queue.shift()!
    const room = byKey.get(key)
    const at = placed.get(key)
    if (!room || !at) continue

    for (const exit of room.exits) {
      // Skip already placed or non-existent targets
      if (placed.has(exit.to) || !byKey.has(exit.to)) continue

      const nextX = at.x + stepX(exit.direction)
      const nextY = at.y + stepY(exit.direction)

      claim(exit.to, nextX, nextY)
      queue.push(exit.to)
    }
  }

  // Orphaned rooms (disconnected from graph) go in a row at the bottom
  let spare = 0
  const floor = placed.size === 0 ? 0 : Math.max(...[...placed.values()].map((p) => p.y)) + 3

  for (const room of rooms) {
    if (!placed.has(room.key)) {
      let cx = spare++
      let cy = floor
      while (taken.has(`${cx},${cy}`)) cx++
      placed.set(room.key, { x: cx, y: cy })
      taken.add(`${cx},${cy}`)
    }
  }

  return rooms.map((room) => ({ room, ...(placed.get(room.key) ?? { x: 0, y: 0 }) }))
}
