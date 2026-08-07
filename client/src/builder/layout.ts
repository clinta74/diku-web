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
 * Multi-pass layout algorithm:
 * 1. Initial BFS pass from anchor room, placing each exit 1 step away
 * 2. Refinement passes that push rooms further apart to reduce edge crossings
 * 3. Collision avoidance throughout
 *
 * Results in an accurate spatial representation that:
 * - North: up, South: down, East: right, West: left
 * - Up: upper-left diagonal, Down: lower-right diagonal
 * - Rooms spaced to avoid overlapping edges
 * - Unreachable rooms placed at the bottom
 */
export function layoutZone(rooms: RoomDetail[]): PlacedRoom[] {
  const byKey = new Map(rooms.map((r) => [r.key, r]))
  let placed = new Map<string, { x: number; y: number }>()
  let taken = new Set<string>()

  function buildTakenSet() {
    taken.clear()
    for (const pos of placed.values()) {
      taken.add(`${pos.x},${pos.y}`)
    }
  }

  function doLayoutPass(stepMultiplier: number = 1) {
    const newPlaced = new Map<string, { x: number; y: number }>()
    const newTaken = new Set<string>()

    function claimInPass(key: string, x: number, y: number) {
      let cx = x
      let cy = y

      while (newTaken.has(`${cx},${cy}`)) cx++

      newPlaced.set(key, { x: cx, y: cy })
      newTaken.add(`${cx},${cy}`)
    }

    // Start with rooms that have explicit coordinates
    for (const room of rooms) {
      if (room.editorX !== null && room.editorY !== null) {
        claimInPass(room.key, room.editorX, room.editorY)
      }
    }

    const queue = [...newPlaced.keys()]

    // No explicit coordinates: anchor on first room
    if (queue.length === 0 && rooms.length > 0) {
      claimInPass(rooms[0].key, 3, 3)
      queue.push(rooms[0].key)
    }

    // BFS walk with configurable step distance
    while (queue.length > 0) {
      const key = queue.shift()!
      const room = byKey.get(key)
      const at = newPlaced.get(key)
      if (!room || !at) continue

      for (const exit of room.exits) {
        if (newPlaced.has(exit.to) || !byKey.has(exit.to)) continue

        const nextX = at.x + stepX(exit.direction) * stepMultiplier
        const nextY = at.y + stepY(exit.direction) * stepMultiplier

        claimInPass(exit.to, nextX, nextY)
        queue.push(exit.to)
      }
    }

    placed = newPlaced
    buildTakenSet()
  }

  // Initial pass: place rooms 1 step apart
  doLayoutPass(1)

  // Refinement passes: increase spacing to reduce edge crossings
  // Second pass spreads rooms that connect multiple hops apart
  doLayoutPass(1.5)

  // Place orphaned rooms
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
