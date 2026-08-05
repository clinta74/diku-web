import { useMemo, useRef, useState } from 'react'
import { builderApi, OPPOSITE, type RoomDetail } from '../net/builderApi'
import { layoutZone, stepX, stepY } from './layout'

interface Props {
  rooms: RoomDetail[]
  selected: string | null
  /** The room the builder's character is standing in, when follow mode is on (PLAN.md §7.6). */
  occupied: string | null
  onSelect: (roomKey: string) => void
  onChanged: () => void
}

const CELL = 96
const BOX_W = 78
const BOX_H = 52

/**
 * Rooms as boxes, exits as lines (PLAN.md §7.2). Drag to arrange; drag from one box onto
 * another to link them.
 *
 * Positions come from editor_x/editor_y, which is builder-only layout metadata - players never
 * see a zone map and no rule reads it. Rooms without stored coordinates get laid out from the
 * exit graph on first open, so a zone imported or seeded without them is still usable.
 */
export function ZoneCanvas({ rooms, selected, occupied, onSelect, onChanged }: Props) {
  const [dragging, setDragging] = useState<string | null>(null)
  const [linkFrom, setLinkFrom] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const surface = useRef<HTMLDivElement>(null)

  const placed = useMemo(() => layoutZone(rooms), [rooms])

  const width = Math.max(...placed.map((r) => r.x), 4) + 2
  const height = Math.max(...placed.map((r) => r.y), 3) + 2

  function moveTo(roomKey: string, event: React.MouseEvent) {
    const bounds = surface.current?.getBoundingClientRect()
    if (!bounds) return

    const x = Math.max(0, Math.round((event.clientX - bounds.left - BOX_W / 2) / CELL))
    const y = Math.max(0, Math.round((event.clientY - bounds.top - BOX_H / 2) / CELL))

    void builderApi
      .updateRoom(roomKey, { editorX: x, editorY: y })
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not move that room.'))
  }

  function link(fromKey: string, toKey: string) {
    const from = placed.find((r) => r.room.key === fromKey)
    const to = placed.find((r) => r.room.key === toKey)
    if (!from || !to || fromKey === toKey) return

    // The direction is read off the canvas geometry, which is the whole point of dragging
    // between two boxes: the builder is already showing where the rooms sit relative to
    // each other, so making them say "north" as well would be asking twice.
    const dx = to.x - from.x
    const dy = to.y - from.y
    const direction =
      Math.abs(dx) >= Math.abs(dy) ? (dx >= 0 ? 'east' : 'west') : dy >= 0 ? 'south' : 'north'

    void builderApi
      .setExit(fromKey, direction, toKey)
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not link those rooms.'))
  }

  return (
    <div className="zone-canvas-wrap">
      {error && <p className="bad">{error}</p>}
      {linkFrom && (
        <p className="detail">
          Linking from <code>{linkFrom}</code> — click a room, or{' '}
          <button type="button" className="link" onClick={() => setLinkFrom(null)}>
            cancel
          </button>
        </p>
      )}

      <div
        className="zone-canvas"
        ref={surface}
        style={{ width: width * CELL, height: height * CELL }}
        onMouseUp={(event) => {
          if (dragging) {
            moveTo(dragging, event)
            setDragging(null)
          }
        }}
      >
        <svg className="edges" width={width * CELL} height={height * CELL}>
          {placed.flatMap((from) =>
            from.room.exits.map((exit) => {
              const to = placed.find((r) => r.room.key === exit.to)

              // A dangling exit has nothing to draw a line to, so it gets a stub instead -
              // visible, clearly unfinished, and not mistaken for a missing exit.
              if (!to) {
                return (
                  <line
                    key={`${from.room.key}-${exit.direction}`}
                    className="edge dangling"
                    x1={from.x * CELL + BOX_W / 2}
                    y1={from.y * CELL + BOX_H / 2}
                    x2={from.x * CELL + BOX_W / 2 + stubX(exit.direction)}
                    y2={from.y * CELL + BOX_H / 2 + stubY(exit.direction)}
                  />
                )
              }

              return (
                <line
                  key={`${from.room.key}-${exit.direction}`}
                  className="edge"
                  x1={from.x * CELL + BOX_W / 2}
                  y1={from.y * CELL + BOX_H / 2}
                  x2={to.x * CELL + BOX_W / 2}
                  y2={to.y * CELL + BOX_H / 2}
                />
              )
            }),
          )}
        </svg>

        {placed.map(({ room, x, y }) => {
          const unfinished = room.flags.unfinished === true
          const classes = ['room-box']
          if (room.key === selected) classes.push('selected')
          if (room.key === occupied) classes.push('occupied')
          if (unfinished) classes.push('unfinished')

          return (
            <button
              key={room.key}
              type="button"
              className={classes.join(' ')}
              style={{ left: x * CELL, top: y * CELL, width: BOX_W, height: BOX_H }}
              title={`${room.key}${unfinished ? ' (unfinished)' : ''}`}
              onMouseDown={(event) => {
                if (event.shiftKey) {
                  setLinkFrom(room.key)
                  return
                }
                setDragging(room.key)
              }}
              onClick={() => {
                if (linkFrom && linkFrom !== room.key) {
                  link(linkFrom, room.key)
                  setLinkFrom(null)
                  return
                }
                onSelect(room.key)
              }}
            >
              <span className="room-box-title">{room.title}</span>
              <span className="room-box-key">{room.key.split('.').pop()}</span>
            </button>
          )
        })}
      </div>

      <p className="dim detail">
        Drag to arrange. Shift-drag from a room, then click another, to link them — the
        direction comes from where they sit. Hatched rooms are still stubs.
      </p>
    </div>
  )
}

const stubX = (direction: string) => stepX(direction) * 28
const stubY = (direction: string) => stepY(direction) * 28

export { OPPOSITE }
