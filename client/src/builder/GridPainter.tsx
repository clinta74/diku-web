import { useState } from 'react'
import { Button } from '../ui/Button'
import { NumberInput } from '../ui/NumberInput'
import { PALETTE, PALETTE_GLYPHS, tileFor } from './palette'

interface Props {
  grid: string[]
  legend: Record<string, string>
  onChange: (grid: string[], legend: Record<string, string>) => void
}

/**
 * The size a fresh grid starts at, matching what the starter zone is drawn at and what the
 * client falls back to for a room with no art. Was 11x5, which is too small to fit a room's
 * furniture inside its own walls.
 */
const NEW_GRID_WIDTH = 21
const NEW_GRID_HEIGHT = 9

/**
 * The room grid edited as ASCII art (PLAN.md §7.2): pick a glyph, click or drag to paint,
 * resize the rectangle from the edges.
 *
 * Worth remembering while reading this: none of it means anything to the rules. Terrain is
 * decoration and spawn-placement surface, walls block nothing, and a room with no grid at all
 * renders a plain rectangle - art is an upgrade, not a tax (PLAN.md §4.2, §4.3).
 */
export function GridPainter({ grid, legend, onChange }: Props) {
  const [glyph, setGlyph] = useState('.')
  const [painting, setPainting] = useState(false)

  // Glyphs this room already uses that the palette does not offer — art authored before a
  // group existed, or imported. Shown so painting over one is possible without retyping it.
  const extras = Object.entries(legend).filter(
    ([key]) => !PALETTE_GLYPHS.some(([g]) => g === key),
  )

  const height = grid.length
  const width = height > 0 ? Math.max(...grid.map((row) => row.length)) : 0

  function paint(x: number, y: number) {
    const rows = normalise(grid)
    if (!rows[y] || x >= rows[y].length) return

    const row = rows[y].split('')
    row[x] = glyph
    rows[y] = row.join('')

    // The room's own name for a glyph wins: a builder who renamed `═` to "bar" keeps it.
    onChange(rows, { ...legend, [glyph]: legend[glyph] ?? tileFor(glyph) })
  }

  function resize(nextWidth: number, nextHeight: number) {
    const w = Math.max(1, Math.min(40, nextWidth))
    const h = Math.max(1, Math.min(20, nextHeight))
    const rows: string[] = []

    for (let y = 0; y < h; y++) {
      const existing = grid[y] ?? ''
      rows.push(existing.padEnd(w, '.').slice(0, w))
    }

    onChange(rows, Object.keys(legend).length ? legend : { '.': 'floor' })
  }

  if (height === 0) {
    return (
      <div className="grid-painter empty">
        <p className="dim">
          No terrain art. The client draws a plain rectangle, which is a perfectly good room.
        </p>
        <button type="button" onClick={() => resize(NEW_GRID_WIDTH, NEW_GRID_HEIGHT)}>
          Start a grid
        </button>
      </div>
    )
  }

  const rows = normalise(grid)

  return (
    <div className="grid-painter">
      <div className="palette-groups">
        {PALETTE.map((group) => (
          <div className="palette-group" key={group.label}>
            <span className="palette-label" title={group.hint}>
              {group.label}
            </span>
            <div className="palette">
              {group.glyphs.map(([g, tile]) => (
                <button
                  key={g}
                  type="button"
                  className={g === glyph ? 'swatch selected' : 'swatch'}
                  title={legend[g] ? `${legend[g]} (in this room)` : tile}
                  onClick={() => setGlyph(g)}
                >
                  {g === ' ' ? '␠' : g}
                </button>
              ))}
            </div>
          </div>
        ))}

        {extras.length > 0 && (
          <div className="palette-group">
            <span className="palette-label" title="Glyphs this room uses that are not in the palette.">
              In this room
            </span>
            <div className="palette">
              {extras.map(([g, tile]) => (
                <button
                  key={g}
                  type="button"
                  className={g === glyph ? 'swatch selected' : 'swatch'}
                  title={tile}
                  onClick={() => setGlyph(g)}
                >
                  {g === ' ' ? '␠' : g}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* onPointerLeave ends a drag that left the canvas, so the release never arrives and
          the grid would otherwise keep painting on the next hover. */}
      <div
        className="canvas"
        onPointerDown={() => setPainting(true)}
        onPointerUp={() => setPainting(false)}
        onPointerCancel={() => setPainting(false)}
        onPointerLeave={() => setPainting(false)}
      >
        {rows.map((row, y) => (
          <div key={y} className="grid-row">
            {row.split('').map((cell, x) => (
              <button
                key={x}
                type="button"
                className="cell"
                title={legend[cell] ?? cell}
                onPointerDown={(e) => {
                  // Touch gives the first cell implicit pointer capture, which sends every later
                  // move to that same cell — so a drag across the grid would paint one square and
                  // nothing else. Releasing it puts the events back on the element under the
                  // finger, which is what makes onPointerEnter below fire at all.
                  e.currentTarget.releasePointerCapture?.(e.pointerId)
                  paint(x, y)
                }}
                onPointerEnter={() => painting && paint(x, y)}
              >
                {cell}
              </button>
            ))}
          </div>
        ))}
      </div>

      <div className="grid-size">
        <label>
          w
          <NumberInput min={1} max={40} value={width} onChange={(w) => resize(w, height)} />
        </label>
        <label>
          h
          <NumberInput min={1} max={20} value={height} onChange={(h) => resize(width, h)} />
        </label>
        <Button variant="link" onClick={() => onChange([], {})}>
          clear art
        </Button>
      </div>

      <details className="legend-editor">
        <summary>Legend ({Object.keys(legend).length})</summary>
        <ul>
          {Object.entries(legend).map(([g, tile]) => (
            <li key={g}>
              <span className="glyph">{g}</span>
              <input
                value={tile}
                onChange={(e) => onChange(grid, { ...legend, [g]: e.target.value })}
              />
            </li>
          ))}
        </ul>
      </details>
    </div>
  )
}

/**
 * Pads every row to the longest one. A ragged grid is allowed to reach the server - validation
 * is advisory and never blocks a save - but the painter itself needs a rectangle to click on.
 */
function normalise(grid: string[]): string[] {
  const width = Math.max(...grid.map((row) => row.length))
  return grid.map((row) => row.padEnd(width, '.'))
}
