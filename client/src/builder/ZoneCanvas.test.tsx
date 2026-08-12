// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
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

/**
 * Panning used to require Ctrl. These pin the two behaviours that replaced it — the guard that
 * makes a plain drag safe, and the threshold that keeps an ordinary click from nudging the map.
 *
 * jsdom reports every element as zero-sized, so `clamp` pins a pan to its own margin rather than
 * to the distance dragged. That is why these assert *whether* the map moved and never by how much:
 * the distance is a function of a viewport jsdom does not have.
 */
it('pans on a plain drag, with no modifier held', () => {
  const { surface, canvas } = renderCanvas()
  const before = canvas.style.transform

  fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 })
  fireEvent.mouseMove(surface, { clientX: 160, clientY: 140 })
  fireEvent.mouseUp(surface)

  expect(canvas.style.transform).not.toBe(before)
})

it('does not pan when the press has barely moved', () => {
  const { surface, canvas } = renderCanvas()
  const before = canvas.style.transform

  // Two pixels: a click by a hand that is not perfectly still, not a drag.
  fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 })
  fireEvent.mouseMove(surface, { clientX: 102, clientY: 101 })
  fireEvent.mouseUp(surface)

  expect(canvas.style.transform).toBe(before)
})

it('does not pan when the drag starts on a room box', () => {
  // The guard that made the Ctrl requirement unnecessary: without it, dragging a box moves the map
  // instead of selecting the room, which is the reason the modifier was there in the first place.
  const { box, canvas, onSelect } = renderCanvas()
  const before = canvas.style.transform

  fireEvent.mouseDown(box, { clientX: 100, clientY: 100 })
  fireEvent.mouseMove(box, { clientX: 160, clientY: 140 })
  fireEvent.mouseUp(box)

  expect(canvas.style.transform).toBe(before)

  fireEvent.click(box)
  expect(onSelect).toHaveBeenCalledWith(rooms[0].key)
})

it('releases the drag when the pointer leaves the surface', () => {
  // Without this the map would follow the cursor again the next time it wandered back over the
  // canvas, with no button held.
  const { surface, canvas } = renderCanvas()

  fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 })
  fireEvent.mouseMove(surface, { clientX: 160, clientY: 140 })
  fireEvent.mouseLeave(surface)

  const parked = canvas.style.transform
  fireEvent.mouseMove(surface, { clientX: 400, clientY: 400 })

  expect(canvas.style.transform).toBe(parked)
})
