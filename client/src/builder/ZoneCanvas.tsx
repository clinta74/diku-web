import { useMemo, useRef, useState } from 'react'
import { type RoomDetail } from '../net/builderApi'
import { layoutZone, stepX, stepY, type PlacedRoom } from './layout'
import { LinkRoomsDialog } from './dialogs/LinkRoomsDialog'

interface Props {
  rooms: RoomDetail[]
  selected: string | null
  occupied: string | null
  onSelect: (roomKey: string) => void
  onChanged: () => void
}

const CELL = 150
const BOX_W = 120
const BOX_H = 80
const PAN_STEP = CELL

/**
 * How far the pointer must travel before a press becomes a pan rather than a click.
 *
 * Not load-bearing - a press on bare canvas has nothing else to mean, since only room boxes are
 * selectable. It is there so the tremor in an ordinary click cannot nudge the map a pixel or two,
 * which reads as the view drifting on its own.
 */
const DRAG_THRESHOLD = 4

/**
 * The zone map. Rooms auto-layout from their exit topology (see layout.ts); this draws the
 * boxes and the edges between them, and lets a builder pan and link.
 *
 * Dragging the canvas pans it. This used to require Ctrl, because an earlier version started a
 * pan from anywhere including a room box, so dragging a box moved the map instead of selecting it.
 * The fix for that is the check in `handlePanStart` below - a press that lands on a box is that
 * box's - and once it was there the modifier was guarding nothing. A modifier the UI has to
 * explain is worse than one it does not need, and there is no key to hold on a touch screen.
 * There is no zoom.
 */
export function ZoneCanvas({ rooms, selected, occupied, onSelect, onChanged }: Props) {
  const [linkFrom, setLinkFrom] = useState<string | null>(null)
  const [linkTo, setLinkTo] = useState<{ from: string; to: string; direction: string } | null>(null)
  const [offset, setOffset] = useState({ x: 24, y: 24 })
  const [panning, setPanning] = useState(false)

  /**
   * Where the press landed and what the offset was then, or null when no button is down.
   *
   * The offset is captured at press time rather than read during the drag, so the map follows the
   * pointer exactly however far it travels.
   */
  const panStart = useRef<{
    pointerX: number
    pointerY: number
    offsetX: number
    offsetY: number
  } | null>(null)
  const surface = useRef<HTMLDivElement>(null)

  const placed = useMemo(() => layoutZone(rooms), [rooms])
  const byKey = useMemo(() => new Map(placed.map((p) => [p.room.key, p])), [placed])

  const width = (Math.max(...placed.map((r) => r.x), 4) + 2) * CELL
  const height = (Math.max(...placed.map((r) => r.y), 3) + 2) * CELL

  // Keep at least a margin of the map on screen, so it can never be dragged fully out of view.
  function clamp(x: number, y: number) {
    const rect = surface.current?.getBoundingClientRect()
    const vw = rect?.width ?? 600
    const vh = rect?.height ?? 400
    const margin = 80
    return {
      x: Math.max(margin - width, Math.min(vw - margin, x)),
      y: Math.max(margin - height, Math.min(vh - margin, y)),
    }
  }

  function nudge(dx: number, dy: number) {
    setOffset((o) => clamp(o.x + dx, o.y + dy))
  }

  function recenter() {
    const target = selected ? byKey.get(selected) : null
    const rect = surface.current?.getBoundingClientRect()
    const viewW = rect?.width ?? 600
    const viewH = rect?.height ?? 400

    if (target) {
      // Centre the selected room in the viewport.
      setOffset(
        clamp(viewW / 2 - (target.x * CELL + BOX_W / 2), viewH / 2 - (target.y * CELL + BOX_H / 2)),
      )
    } else {
      setOffset({ x: 24, y: 24 })
    }
  }

  const handlePanStart = (e: React.MouseEvent) => {
    // A press that lands on a room box belongs to that box. This is the check that made the Ctrl
    // requirement unnecessary, and removing it would bring back the reason the modifier existed.
    if ((e.target as HTMLElement).closest('.room-box')) return

    // Stops the drag from selecting the room titles as text on its way across the canvas.
    e.preventDefault()

    panStart.current = {
      pointerX: e.clientX,
      pointerY: e.clientY,
      offsetX: offset.x,
      offsetY: offset.y,
    }
  }

  const handlePan = (e: React.MouseEvent) => {
    const start = panStart.current
    if (!start) return

    const dx = e.clientX - start.pointerX
    const dy = e.clientY - start.pointerY

    if (!panning) {
      if (Math.abs(dx) < DRAG_THRESHOLD && Math.abs(dy) < DRAG_THRESHOLD) return
      setPanning(true)
    }

    setOffset(clamp(start.offsetX + dx, start.offsetY + dy))
  }

  const endPan = () => {
    panStart.current = null
    setPanning(false)
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    switch (e.key) {
      case 'ArrowLeft':
        nudge(PAN_STEP, 0)
        break
      case 'ArrowRight':
        nudge(-PAN_STEP, 0)
        break
      case 'ArrowUp':
        nudge(0, PAN_STEP)
        break
      case 'ArrowDown':
        nudge(0, -PAN_STEP)
        break
      default:
        return
    }
    e.preventDefault()
  }

  function clickRoom(key: string) {
    if (linkFrom && linkFrom !== key) {
      const from = byKey.get(linkFrom)
      const to = byKey.get(key)
      const direction = from && to ? guessDirection(from, to) : 'north'
      setLinkTo({ from: linkFrom, to: key, direction })
      setLinkFrom(null)
      return
    }
    onSelect(key)
  }

  // The surface always advertises that it can be dragged. Room boxes set their own pointer cursor,
  // so this does not claim the boxes are draggable when they are not.
  const cursor = panning ? 'grabbing' : 'grab'

  return (
    <div className="zone-canvas-container">
      <div className="zone-canvas-header">
        {linkFrom && (
          <p className="link-status">
            <span className="link-icon">🔗</span>
            Linking from <code>{linkFrom}</code> — click a room to choose the direction, or{' '}
            <button type="button" className="cancel-link" onClick={() => setLinkFrom(null)}>
              cancel
            </button>
          </p>
        )}
      </div>

      <div
        className="zone-canvas-wrapper"
        ref={surface}
        tabIndex={0}
        onMouseDown={handlePanStart}
        onMouseMove={handlePan}
        onMouseUp={endPan}
        onMouseLeave={endPan}
        onKeyDown={handleKeyDown}
        style={{ cursor }}
      >
        <div
          className="zone-canvas"
          style={{ width, height, transform: `translate(${offset.x}px, ${offset.y}px)` }}
        >
          <svg className="edges" width={width} height={height}>
            <Edges placed={placed} byKey={byKey} />
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
                  if (event.shiftKey) setLinkFrom(room.key)
                }}
                onClick={() => clickRoom(room.key)}
              >
                <span className="room-box-title">{room.title}</span>
                <span className="room-box-key">{room.key.split('.').pop()}</span>
              </button>
            )
          })}
        </div>
      </div>

      <div className="zone-canvas-controls">
        <div className="pan-pad" role="group" aria-label="Pan the map">
          <button type="button" onClick={() => nudge(0, PAN_STEP)} aria-label="Pan up">
            ↑
          </button>
          <button type="button" onClick={() => nudge(PAN_STEP, 0)} aria-label="Pan left">
            ←
          </button>
          <button type="button" onClick={recenter}>
            Recenter
          </button>
          <button type="button" onClick={() => nudge(-PAN_STEP, 0)} aria-label="Pan right">
            →
          </button>
          <button type="button" onClick={() => nudge(0, -PAN_STEP)} aria-label="Pan down">
            ↓
          </button>
        </div>
        <p className="canvas-help">
          <span className="help-icon">?</span>
          <strong>Drag</strong> or use the arrows to pan • <strong>Shift-click</strong> two rooms to link
        </p>
      </div>

      {linkTo && (
        <LinkRoomsDialog
          open
          onOpenChange={(open) => !open && setLinkTo(null)}
          fromKey={linkTo.from}
          toKey={linkTo.to}
          guessedDirection={linkTo.direction}
          onLinked={onChanged}
        />
      )}
    </div>
  )
}

/** The best-guess direction from two placements, offered to the link dialog as a default. */
function guessDirection(from: PlacedRoom, to: PlacedRoom): string {
  const dx = to.x - from.x
  const dy = to.y - from.y
  if (dx === 1 && dy === 1) return 'up'
  if (dx === -1 && dy === -1) return 'down'
  if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? 'east' : 'west'
  return dy > 0 ? 'south' : 'north'
}

const isVertical = (direction: string) => direction === 'up' || direction === 'down'

/** Clamp the endpoint of a centre-to-centre line to the source box's border. */
function clipToBox(cx: number, cy: number, towardX: number, towardY: number) {
  const dx = towardX - cx
  const dy = towardY - cy
  if (dx === 0 && dy === 0) return { x: cx, y: cy }
  const hw = BOX_W / 2
  const hh = BOX_H / 2
  const t = Math.min(
    dx === 0 ? Infinity : hw / Math.abs(dx),
    dy === 0 ? Infinity : hh / Math.abs(dy),
  )
  return { x: cx + dx * t, y: cy + dy * t }
}

function Edges({
  placed,
  byKey,
}: {
  placed: PlacedRoom[]
  byKey: Map<string, PlacedRoom>
}) {
  const drawn = new Set<string>()
  const elements: React.ReactNode[] = []

  for (const from of placed) {
    const cx = from.x * CELL + BOX_W / 2
    const cy = from.y * CELL + BOX_H / 2

    for (const exit of from.room.exits) {
      const to = byKey.get(exit.to)

      // Dangling exit: no room to connect to. Draw a stub offset out of the box, like a
      // signpost, never on top of the room itself.
      if (!to) {
        const ox = cx + stepX(exit.direction) * 34
        const oy = cy + stepY(exit.direction) * 34
        elements.push(
          <g key={`${from.room.key}-${exit.direction}-stub`}>
            <line className="edge dangling" x1={cx} y1={cy} x2={ox} y2={oy} />
            <text className="direction-label" x={ox} y={oy + 3} textAnchor="middle" fontSize="9">
              {exit.direction[0].toUpperCase()}
            </text>
          </g>,
        )
        continue
      }

      // One connector per unordered pair - a north/south pair is one line, not two stacked.
      const pairKey = [from.room.key, to.room.key].sort().join('|')
      if (drawn.has(pairKey)) continue
      drawn.add(pairKey)

      const toCx = to.x * CELL + BOX_W / 2
      const toCy = to.y * CELL + BOX_H / 2
      const a = clipToBox(cx, cy, toCx, toCy)
      const b = clipToBox(toCx, toCy, cx, cy)
      const oneWay = !to.room.exits.some((e) => e.to === from.room.key)
      const midX = (a.x + b.x) / 2
      const midY = (a.y + b.y) / 2

      if (isVertical(exit.direction)) {
        // Bow the path perpendicular to the run so a vertical link reads as vertical without
        // having to read the label.
        const dx = b.x - a.x
        const dy = b.y - a.y
        const len = Math.hypot(dx, dy) || 1
        const px = (-dy / len) * 26
        const py = (dx / len) * 26
        elements.push(
          <g key={pairKey}>
            <path
              className="edge vertical-exit"
              d={`M ${a.x} ${a.y} Q ${midX + px} ${midY + py} ${b.x} ${b.y}`}
              fill="none"
            />
            <text
              className="direction-label"
              x={midX + px}
              y={midY + py + 3}
              textAnchor="middle"
              fontSize="11"
            >
              {exit.direction === 'up' ? '⬆' : '⬇'}
            </text>
          </g>,
        )
        continue
      }

      const label =
        exit.direction === 'north' ? '↑' : exit.direction === 'south' ? '↓' : exit.direction === 'east' ? '→' : '←'
      elements.push(
        <g key={pairKey}>
          <line className={oneWay ? 'edge one-way' : 'edge'} x1={a.x} y1={a.y} x2={b.x} y2={b.y} />
          <circle className="direction-indicator" cx={midX} cy={midY} r="7" />
          <text className="direction-label" x={midX} y={midY + 3} textAnchor="middle" fontSize="9">
            {label}
          </text>
        </g>,
      )
    }
  }

  return <>{elements}</>
}
