// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { RoomDetail } from '../net/builderApi'
import { ZoneCanvas } from './ZoneCanvas'

const rooms: RoomDetail[] = [
  {
    key: 'aldenmoor.millbrook.north-gate',
    zoneKey: 'aldenmoor.millbrook',
    title: 'The North Gate',
    description: 'A gate.',
    flags: {},
    resolved: [],
    grid: [],
    legend: {},
    editorX: null,
    editorY: null,
    exits: [],
  },
]

afterEach(cleanup)

function renderCanvas() {
  const onSelect = vi.fn()
  const result = render(
    <ZoneCanvas
      rooms={rooms}
      selected={null}
      occupied={null}
      onSelect={onSelect}
      onChanged={() => undefined}
    />,
  )

  const surface = result.container.querySelector('.zone-canvas-wrapper') as HTMLElement
  const canvas = result.container.querySelector('.zone-canvas') as HTMLElement
  const box = result.container.querySelector('.room-box') as HTMLElement

  return { onSelect, surface, canvas, box, ...result }
}

/** The scale out of `transform: translate(...) scale(n)`. */
function scaleOf(canvas: HTMLElement): number {
  const match = /scale\(([\d.]+)\)/.exec(canvas.style.transform)
  return match ? Number(match[1]) : 1
}

/**
 * Panning used to require Ctrl, and only ever answered to a mouse. These pin what replaced it —
 * the guard that makes a plain drag safe, the threshold that keeps an ordinary click from nudging
 * the map, and the pinch that the same pointer handlers now carry.
 *
 * jsdom reports every element as zero-sized, so `clamp` pins a pan to its own margin rather than
 * to the distance dragged. That is why these assert *whether* the map moved and never by how much:
 * the distance is a function of a viewport jsdom does not have.
 */
it('pans on a plain drag, with no modifier held', () => {
  const { surface, canvas } = renderCanvas()
  const before = canvas.style.transform

  fireEvent.pointerDown(surface, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerMove(surface, { pointerId: 1, clientX: 160, clientY: 140 })
  fireEvent.pointerUp(surface, { pointerId: 1 })

  expect(canvas.style.transform).not.toBe(before)
})

it('does not pan when the press has barely moved', () => {
  const { surface, canvas } = renderCanvas()
  const before = canvas.style.transform

  // Two pixels: a click by a hand that is not perfectly still, not a drag.
  fireEvent.pointerDown(surface, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerMove(surface, { pointerId: 1, clientX: 102, clientY: 101 })
  fireEvent.pointerUp(surface, { pointerId: 1 })

  expect(canvas.style.transform).toBe(before)
})

it('does not pan when the drag starts on a room box', () => {
  // The guard that made the Ctrl requirement unnecessary: without it, dragging a box moves the map
  // instead of selecting the room, which is the reason the modifier was there in the first place.
  const { box, canvas, onSelect } = renderCanvas()
  const before = canvas.style.transform

  fireEvent.pointerDown(box, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerMove(box, { pointerId: 1, clientX: 160, clientY: 140 })
  fireEvent.pointerUp(box, { pointerId: 1 })

  expect(canvas.style.transform).toBe(before)

  fireEvent.click(box)
  expect(onSelect).toHaveBeenCalledWith(rooms[0].key)
})

it('releases the drag when the pointer leaves the surface', () => {
  // A mouse released outside the window never reports the release, so without this the map would
  // follow the cursor again the next time it wandered back with no button held.
  const { surface, canvas } = renderCanvas()

  fireEvent.pointerDown(surface, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerMove(surface, { pointerId: 1, clientX: 160, clientY: 140 })
  fireEvent.pointerLeave(surface, { pointerId: 1 })

  const parked = canvas.style.transform
  fireEvent.pointerMove(surface, { pointerId: 1, clientX: 400, clientY: 400 })

  expect(canvas.style.transform).toBe(parked)
})

it('zooms out and in from the buttons', () => {
  const { canvas } = renderCanvas()

  expect(scaleOf(canvas)).toBe(1)

  fireEvent.click(screen.getByRole('button', { name: 'Zoom out' }))
  expect(scaleOf(canvas)).toBeLessThan(1)

  fireEvent.click(screen.getByRole('button', { name: 'Zoom in' }))
  expect(scaleOf(canvas)).toBeCloseTo(1, 5)
})

it('will not zoom past its limits', () => {
  const { canvas } = renderCanvas()

  for (let i = 0; i < 20; i++) {
    fireEvent.click(screen.getByRole('button', { name: 'Zoom in' }))
  }
  expect(scaleOf(canvas)).toBeLessThanOrEqual(1.4)

  for (let i = 0; i < 40; i++) {
    fireEvent.click(screen.getByRole('button', { name: 'Zoom out' }))
  }
  expect(scaleOf(canvas)).toBeGreaterThanOrEqual(0.35)
})

it('zooms on a two-finger pinch', () => {
  // The gesture the whole Pointer Events move was for. Spreading the fingers scales up; bringing
  // them together scales back down.
  const { surface, canvas } = renderCanvas()

  fireEvent.pointerDown(surface, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerDown(surface, { pointerId: 2, clientX: 200, clientY: 100 })

  fireEvent.pointerMove(surface, { pointerId: 2, clientX: 300, clientY: 100 })
  expect(scaleOf(canvas)).toBeGreaterThan(1)

  fireEvent.pointerMove(surface, { pointerId: 2, clientX: 150, clientY: 100 })
  expect(scaleOf(canvas)).toBeLessThan(1)
})

it('does not pan while two fingers are down', () => {
  // A pinch that also panned would slide the map about while the player was only trying to zoom,
  // because the two fingers rarely move by the same amount.
  const { surface, canvas } = renderCanvas()

  fireEvent.pointerDown(surface, { pointerId: 1, clientX: 100, clientY: 100 })
  fireEvent.pointerDown(surface, { pointerId: 2, clientX: 200, clientY: 100 })

  const translated = /translate\([^)]*\)/.exec(canvas.style.transform)?.[0]

  // Both fingers travel the same distance: no scale change, and nothing should move.
  fireEvent.pointerMove(surface, { pointerId: 1, clientX: 140, clientY: 100 })
  fireEvent.pointerMove(surface, { pointerId: 2, clientX: 240, clientY: 100 })

  expect(/translate\([^)]*\)/.exec(canvas.style.transform)?.[0]).toBe(translated)
})
