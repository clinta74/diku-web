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
      return -1 // Up goes to upper-left
    case 'down':
      return 1 // Down goes to lower-right
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
      return -1 // Up goes to upper-left
    case 'down':
      return 1 // Down goes to lower-right
    default:
      return 0
  }
}

/**
 * Places a zone's rooms on the builder canvas based on exit topology (PLAN.md §7.2).
 *
 * Multi-pass layout with collision resolution:
 * 1. Initial BFS places rooms 1 cell in exit direction
 * 2. Subsequent passes increase step distance where collisions occur
 * 3. Each pass allows complex topologies to spread out properly
 *
 * Ensures rooms with multiple branching exits have space to avoid overlap
 * and allows vertical (up/down) rooms to force horizontal shifts where needed.
 *
 * Results in:
 * - North: up, South: down, East: right, West: left
 * - Up: upper-left diagonal, Down: lower-right diagonal
 * - Complex topologies spread to avoid collision
 * - Unreachable rooms placed at the bottom
 */
export function layoutZone(rooms: RoomDetail[]): PlacedRoom[] {
  const byKey = new Map(rooms.map((r) => [r.key, r]))
  let placed = new Map<string, { x: number; y: number }>()

  function doLayoutPass(stepMultiplier: number) {
    const newPlaced = new Map<string, { x: number; y: number }>()
    const newTaken = new Set<string>()

    function claim(key: string, x: number, y: number) {
      let cx = x
      let cy = y

      // Shift if negative
      if (cx < 0 || cy < 0) {
        const minX = Math.min(0, cx, ...[...newPlaced.values()].map((p) => p.x))
        const minY = Math.min(0, cy, ...[...newPlaced.values()].map((p) => p.y))

        if (minX < 0 || minY < 0) {
          const shifted = new Map<string, { x: number; y: number }>()
          for (const [k, pos] of newPlaced) {
            shifted.set(k, { x: pos.x - minX, y: pos.y - minY })
          }
          newPlaced.clear()
          for (const [k, pos] of shifted) {
            newPlaced.set(k, pos)
          }
          newTaken.clear()
          for (const pos of newPlaced.values()) {
            newTaken.add(`${pos.x},${pos.y}`)
          }
        }

        cx = cx - minX
        cy = cy - minY
      }

      // Collision avoidance: nudge east
      while (newTaken.has(`${cx},${cy}`)) cx++

      newPlaced.set(key, { x: cx, y: cy })
      newTaken.add(`${cx},${cy}`)
    }

    // Anchor explicit positions
    for (const room of rooms) {
      if (room.editorX !== null && room.editorY !== null) {
        claim(room.key, room.editorX, room.editorY)
      }
    }

    const queue = [...newPlaced.keys()]

    // Anchor first room if nothing is explicit
    if (queue.length === 0 && rooms.length > 0) {
      claim(rooms[0].key, 3, 3)
      queue.push(rooms[0].key)
    }

    // BFS: position rooms by exit direction
    while (queue.length > 0) {
      const key = queue.shift()!
      const room = byKey.get(key)
      const at = newPlaced.get(key)
      if (!room || !at) continue

      for (const exit of room.exits) {
        if (newPlaced.has(exit.to) || !byKey.has(exit.to)) continue

        const dx = stepX(exit.direction) * stepMultiplier
        const dy = stepY(exit.direction) * stepMultiplier
        claim(exit.to, at.x + dx, at.y + dy)
        queue.push(exit.to)
      }
    }

    placed = newPlaced
  }

  // Pass 1: initial placement at 1x distance
  doLayoutPass(1)

  // Pass 2: spread rooms at 1.5x to resolve collisions and branching conflicts
  doLayoutPass(1.5)

  // Pass 3: final spread at 2x for complex topologies with many branches
  doLayoutPass(2)

  // Place orphaned rooms
  let spare = 0
  const floor = placed.size === 0 ? 0 : Math.max(...[...placed.values()].map((p) => p.y)) + 3
  const taken = new Set<string>()
  for (const pos of placed.values()) {
    taken.add(`${pos.x},${pos.y}`)
  }

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
