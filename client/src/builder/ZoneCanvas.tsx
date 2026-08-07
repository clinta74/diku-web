import { useMemo, useRef, useState } from 'react'
import { builderApi, OPPOSITE, type RoomDetail } from '../net/builderApi'
import { layoutZone, stepX, stepY } from './layout'

interface Props {
  rooms: RoomDetail[]
  selected: string | null
  occupied: string | null
  onSelect: (roomKey: string) => void
  onChanged: () => void
}

const CELL = 96
const BOX_W = 120
const BOX_H = 80

/**
 * Automatic room layout based on exit topology (PLAN.md §7.2).
 * Rooms position themselves based on exit directions:
 * - North: up, South: down, East: right, West: left
 * - Up: upper-left, Down: lower-right
 *
 * Click rooms to select. Shift-click to link (creates the exit).
 * Drag canvas to pan, scroll to zoom.
 */
export function ZoneCanvas({ rooms, selected, occupied, onSelect, onChanged }: Props) {
  const [linkFrom, setLinkFrom] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [offsetX, setOffsetX] = useState(0)
  const [offsetY, setOffsetY] = useState(0)
  const [panning, setPanning] = useState(false)
  const [panStart, setPanStart] = useState({ x: 0, y: 0 })
  const surface = useRef<HTMLDivElement>(null)
  const container = useRef<HTMLDivElement>(null)

  const placed = useMemo(() => layoutZone(rooms), [rooms])

  const width = Math.max(...placed.map((r) => r.x), 4) + 2
  const height = Math.max(...placed.map((r) => r.y), 3) + 2

  function link(fromKey: string, toKey: string) {
    const from = placed.find((r) => r.room.key === fromKey)
    const to = placed.find((r) => r.room.key === toKey)
    if (!from || !to || fromKey === toKey) return

    // Direction is determined by the layout position (exit direction already defined the position)
    const dx = to.x - from.x
    const dy = to.y - from.y

    let direction = 'north'
    if (Math.abs(dx) > Math.abs(dy)) {
      direction = dx > 0 ? 'east' : 'west'
    } else if (dy > 0) {
      direction = 'south'
    } else if (dy < 0) {
      direction = 'north'
    }

    // Check for vertical direction
    if (dx === -1 && dy === -1) direction = 'up'
    if (dx === 1 && dy === 1) direction = 'down'

    void builderApi
      .setExit(fromKey, direction, toKey)
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not link those rooms.'))
  }

  const handlePanStart = (e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('.room-box')) return
    setPanning(true)
    setPanStart({ x: e.clientX - offsetX, y: e.clientY - offsetY })
  }

  const handlePan = (e: React.MouseEvent) => {
    if (!panning) return
    setOffsetX(e.clientX - panStart.x)
    setOffsetY(e.clientY - panStart.y)
  }

  const handlePanEnd = () => {
    setPanning(false)
  }

  const isVerticalExit = (direction: string) => direction === 'up' || direction === 'down'

  return (
    <div className="zone-canvas-container">
      <div className="zone-canvas-header">
        {error && <p className="error-msg">{error}</p>}
        {linkFrom && (
          <p className="link-status">
            <span className="link-icon">🔗</span>
            Linking from <code>{linkFrom}</code> — click a room to complete the link or{' '}
            <button type="button" className="cancel-link" onClick={() => setLinkFrom(null)}>
              cancel
            </button>
          </p>
        )}
      </div>

      <div
        className="zone-canvas-wrapper"
        ref={container}
        onMouseDown={handlePanStart}
        onMouseMove={handlePan}
        onMouseUp={handlePanEnd}
        onMouseLeave={handlePanEnd}
        style={{ cursor: panning ? 'grabbing' : 'grab' }}
      >
        <div
          className="zone-canvas"
          ref={surface}
          style={{
            width: width * CELL,
            height: height * CELL,
            transform: `translate(${offsetX}px, ${offsetY}px)`,
          }}
        >
          <svg className="edges" width={width * CELL} height={height * CELL}>
            {placed.flatMap((from) =>
              from.room.exits.map((exit) => {
                const to = placed.find((r) => r.room.key === exit.to)
                const isVertical = isVerticalExit(exit.direction)
                const cx = from.x * CELL + BOX_W / 2
                const cy = from.y * CELL + BOX_H / 2

                if (!to) {
                  // Dangling exit - show as stub
                  if (isVertical) {
                    const radius = 12
                    return (
                      <g key={`${from.room.key}-${exit.direction}`}>
                        <circle cx={cx} cy={cy} r={radius} className="edge vertical-stub" />
                        <circle cx={cx} cy={cy} r={radius - 4} className="edge vertical-stub" />
                        <text x={cx} y={cy + 2} className="direction-label" textAnchor="middle" fontSize="8">
                          {exit.direction[0].toUpperCase()}
                        </text>
                      </g>
                    )
                  }

                  return (
                    <line
                      key={`${from.room.key}-${exit.direction}`}
                      className="edge dangling"
                      x1={cx}
                      y1={cy}
                      x2={cx + stubX(exit.direction)}
                      y2={cy + stubY(exit.direction)}
                    />
                  )
                }

                // Normal exit to another room
                const toCx = to.x * CELL + BOX_W / 2
                const toCy = to.y * CELL + BOX_H / 2

                if (isVertical) {
                  // Vertical exits: curved path
                  const controlX = cx + (exit.direction === 'up' ? -15 : 15)
                  const label = exit.direction === 'up' ? '⬆' : '⬇'

                  return (
                    <g key={`${from.room.key}-${exit.direction}`}>
                      <path
                        d={`M ${cx} ${cy} Q ${controlX} ${(cy + toCy) / 2} ${toCx} ${toCy}`}
                        className="edge vertical-exit"
                        fill="none"
                        strokeWidth="2"
                      />
                      <text x={controlX} y={(cy + toCy) / 2 - 8} className="direction-label" textAnchor="middle" fontSize="10">
                        {label}
                      </text>
                    </g>
                  )
                }

                // Horizontal exits: straight lines with cardinal direction labels
                const label =
                  exit.direction === 'north'
                    ? '↑'
                    : exit.direction === 'south'
                      ? '↓'
                      : exit.direction === 'east'
                        ? '→'
                        : '←'
                return (
                  <g key={`${from.room.key}-${exit.direction}`}>
                    <line className="edge" x1={cx} y1={cy} x2={toCx} y2={toCy} />
                    <circle cx={(cx + toCx) / 2} cy={(cy + toCy) / 2} r="5" className="direction-indicator" />
                    <text x={(cx + toCx) / 2} y={(cy + toCy) / 2 + 2} className="direction-label" textAnchor="middle" fontSize="9">
                      {label}
                    </text>
                  </g>
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
                  event.stopPropagation()
                  if (event.shiftKey) {
                    setLinkFrom(room.key)
                  }
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
                {room.exits.some((e) => isVerticalExit(e.direction)) && (
                  <span className="vertical-indicator" title="Has vertical exits">
                    ⬍
                  </span>
                )}
              </button>
            )
          })}
        </div>
      </div>

      <div className="zone-canvas-controls">
        <p className="canvas-help">
          <span className="help-icon">?</span>
          <strong>Map auto-layouts</strong> by exit directions • <strong>Drag canvas to pan</strong> • <strong>Shift-click</strong> to link rooms
        </p>
      </div>
    </div>
  )
}

const stubX = (direction: string) => stepX(direction) * 28
const stubY = (direction: string) => stepY(direction) * 28

export { OPPOSITE }
