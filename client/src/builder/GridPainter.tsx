import { useState } from 'react'

interface Props {
  grid: string[]
  legend: Record<string, string>
  onChange: (grid: string[], legend: Record<string, string>) => void
}

const DEFAULT_PALETTE: [string, string][] = [
  ['.', 'floor'],
  ['#', 'wall'],
  ['~', 'water'],
  [',', 'grass'],
  ['"', 'undergrowth'],
  ['+', 'door'],
  ['o', 'prop'],
  ['^', 'stairs'],
]

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

  const palette = [...DEFAULT_PALETTE]
  for (const [key, tile] of Object.entries(legend)) {
    if (!palette.some(([g]) => g === key)) palette.push([key, tile])
  }

  const height = grid.length
  const width = height > 0 ? Math.max(...grid.map((row) => row.length)) : 0

  function paint(x: number, y: number) {
    const rows = normalise(grid)
    if (!rows[y] || x >= rows[y].length) return

    const row = rows[y].split('')
    row[x] = glyph
    rows[y] = row.join('')

    const tile = palette.find(([g]) => g === glyph)?.[1] ?? 'floor'
    onChange(rows, { ...legend, [glyph]: legend[glyph] ?? tile })
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
        <button type="button" onClick={() => resize(11, 5)}>
          Start a grid
        </button>
      </div>
    )
  }

  const rows = normalise(grid)

  return (
    <div className="grid-painter">
      <div className="palette">
        {palette.map(([g, tile]) => (
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

      {/* onMouseLeave ends a drag that left the canvas, so the mouse-up never arrives and
          the grid would otherwise keep painting on the next hover. */}
      <div
        className="canvas"
        onMouseDown={() => setPainting(true)}
        onMouseUp={() => setPainting(false)}
        onMouseLeave={() => setPainting(false)}
      >
        {rows.map((row, y) => (
          <div key={y} className="grid-row">
            {row.split('').map((cell, x) => (
              <button
                key={x}
                type="button"
                className="cell"
                title={legend[cell] ?? cell}
                onMouseDown={() => paint(x, y)}
                onMouseEnter={() => painting && paint(x, y)}
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
          <input
            type="number"
            min={1}
            max={40}
            value={width}
            onChange={(e) => resize(Number(e.target.value), height)}
          />
        </label>
        <label>
          h
          <input
            type="number"
            min={1}
            max={20}
            value={height}
            onChange={(e) => resize(width, Number(e.target.value))}
          />
        </label>
        <button type="button" className="link" onClick={() => onChange([], {})}>
          clear art
        </button>
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
