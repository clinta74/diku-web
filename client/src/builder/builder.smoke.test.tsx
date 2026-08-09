// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Navigate, Route, Routes } from 'react-router'
import type { RoomDetail } from '../net/builderApi'

// A tiny in-memory builder API so the shell can load without a network. Only the reads the
// world tab performs on mount are needed.
// Hoisted alongside the vi.mock factory below, which is lifted to the top of the file and so
// cannot see an ordinary const declared here.
const neutral = vi.hoisted(() => ({
  strength: 1,
  health: 1,
  damage: 1,
  xp: 1,
  gold: 1,
  itemValue: 1,
  itemPower: 1,
  spawnDensity: 1,
}))

const worlds = [
  { key: 'aldenmoor', name: 'Aldenmoor', description: '', sortOrder: 0, flags: {}, multipliers: neutral, zoneCount: 1 },
]
const zones = [
  { key: 'aldenmoor.millbrook', worldKey: 'aldenmoor', name: 'Millbrook', description: '', minLevel: 1, maxLevel: 5, flags: {}, multipliers: neutral, roomCount: 2 },
]
const rooms: RoomDetail[] = [
  { key: 'aldenmoor.millbrook.north-gate', zoneKey: 'aldenmoor.millbrook', title: 'The North Gate', description: 'A gate.', flags: {}, resolved: [], grid: [], legend: {}, editorX: null, editorY: null, exits: [] },
  { key: 'aldenmoor.millbrook.market-row', zoneKey: 'aldenmoor.millbrook', title: 'Market Row', description: 'A market.', flags: {}, resolved: [], grid: [], legend: {}, editorX: null, editorY: null, exits: [] },
]

const room = (key: string) => rooms.find((r) => r.key === key)!

vi.mock('../net/builderApi', () => ({
  DIRECTIONS: ['north', 'east', 'south', 'west', 'up', 'down'],
  OPPOSITE: { north: 'south', south: 'north', east: 'west', west: 'east', up: 'down', down: 'up' },
  MULTIPLIER_KEYS: [
    'strength',
    'health',
    'damage',
    'xp',
    'gold',
    'itemValue',
    'itemPower',
    'spawnDensity',
  ],
  NEUTRAL_MULTIPLIERS: neutral,
  builderApi: {
    zonePreview: () =>
      Promise.resolve({
        zoneKey: 'aldenmoor.millbrook',
        worldMultipliers: neutral,
        zoneMultipliers: neutral,
        templates: [],
      }),
    roomFlags: () => Promise.resolve([]),
    worlds: () => Promise.resolve(worlds),
    zones: () => Promise.resolve(zones),
    rooms: () => Promise.resolve(rooms),
    validate: () => Promise.resolve({ zoneKey: 'aldenmoor.millbrook', warnings: [] }),
    room: (key: string) => Promise.resolve(room(key)),
    mobTemplates: () => Promise.resolve([]),
    itemTemplates: () => Promise.resolve([]),
    spawners: () => Promise.resolve([]),
  },
}))

import { BuilderShell } from './BuilderShell'
import { WorldTab } from './world/WorldTab'
import { MobsTab } from './mobs/MobsTab'
import { ItemsTab } from './items/ItemsTab'

class FakeEventSource {
  close() {}
  addEventListener() {}
}

beforeEach(() => {
  // BuilderData opens one of these; jsdom has no EventSource.
  ;(globalThis as unknown as { EventSource: unknown }).EventSource = FakeEventSource
})

afterEach(cleanup)

interface RenderOpts {
  onClose?: () => void
  initialPath?: string
  occupiedRoom?: string | null
  follow?: boolean
}

function renderBuilder({
  onClose = () => {},
  initialPath = '/builder/world',
  occupiedRoom = null,
  follow = false,
}: RenderOpts = {}) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route
          path="/builder"
          element={
            <BuilderShell
              occupiedRoom={occupiedRoom}
              follow={follow}
              onFollowChange={() => {}}
              onClose={onClose}
            />
          }
        >
          <Route index element={<Navigate to="world" replace />} />
          <Route path="world/:world?/:zone?/:room?/:section?" element={<WorldTab />} />
          <Route path="mobs/:templateKey?" element={<MobsTab />} />
          <Route path="items/:templateKey?" element={<ItemsTab />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('builder shell', () => {
  it('renders the top tabs and an exit control', async () => {
    renderBuilder()
    expect(await screen.findByRole('tab', { name: 'World' })).toBeTruthy()
    expect(screen.getByRole('tab', { name: 'Mobs' })).toBeTruthy()
    expect(screen.getByRole('tab', { name: 'Items' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /exit builder/i })).toBeTruthy()
  })

  it('calls onClose when the exit control is clicked', async () => {
    const onClose = vi.fn()
    renderBuilder({ onClose })
    fireEvent.click(await screen.findByRole('button', { name: /exit builder/i }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('switches the edited room when a room in the list is clicked', async () => {
    renderBuilder()

    // Pick the zone so its rooms list populates, then select rooms from that list.
    fireEvent.click(await screen.findByRole('button', { name: /Millbrook/ }))

    const roomList = () => document.querySelector('.room-list') as HTMLElement
    await waitFor(() => expect(roomList()).toBeTruthy())
    const inList = (name: RegExp) =>
      Array.from(roomList().querySelectorAll('button')).find((b) => name.test(b.textContent ?? ''))!

    fireEvent.click(inList(/The North Gate/))

    await waitFor(() => {
      const editor = document.querySelector('.room-editor')
      expect(editor?.textContent).toContain('aldenmoor.millbrook.north-gate')
    })

    // Now the other room; the editor must re-target it, not stay on the first.
    fireEvent.click(inList(/Market Row/))

    await waitFor(() => {
      const editor = document.querySelector('.room-editor')
      expect(editor?.textContent).toContain('aldenmoor.millbrook.market-row')
      expect(editor?.textContent).not.toContain('north-gate')
    })
  })

  // Radix activates a tab on a real pointer press, which jsdom does not simulate; the tab→URL
  // wiring is covered in the app. What we assert here is that each tab's route renders its own
  // content, so a URL landing on it (deep link, reload) shows the right tab.
  it('renders the Mobs tab content at its route', async () => {
    renderBuilder({ initialPath: '/builder/mobs' })
    expect(await screen.findByText(/Select a mob template/)).toBeTruthy()
  })

  it('renders the Items tab content at its route', async () => {
    renderBuilder({ initialPath: '/builder/items' })
    expect(await screen.findByText(/Select an item template/)).toBeTruthy()
  })

  it('does not snap back to the occupied room when follow is on', async () => {
    // With follow enabled and the character standing in north-gate, manually selecting a
    // different room must stick - the follow effect must only re-target on an actual move,
    // not fight the click (the navigate identity changing must not trigger it).
    renderBuilder({
      occupiedRoom: 'aldenmoor.millbrook.north-gate',
      follow: true,
      initialPath: '/builder/world/aldenmoor/millbrook',
    })

    const roomList = () => document.querySelector('.room-list') as HTMLElement
    await waitFor(() => expect(roomList()).toBeTruthy())
    const inList = (name: RegExp) =>
      Array.from(roomList().querySelectorAll('button')).find((b) => name.test(b.textContent ?? ''))!

    fireEvent.click(inList(/Market Row/))

    await waitFor(() => {
      const editor = document.querySelector('.room-editor')
      expect(editor?.textContent).toContain('aldenmoor.millbrook.market-row')
    })

    // Give the follow effect a chance to (wrongly) fire, then confirm we stayed put.
    await new Promise((r) => setTimeout(r, 50))
    expect(document.querySelector('.room-editor')?.textContent).toContain('market-row')
    expect(document.querySelector('.room-editor')?.textContent).not.toContain('north-gate')
  })

  it('prompts before discarding unsaved prose when switching rooms', async () => {
    renderBuilder()
    fireEvent.click(await screen.findByRole('button', { name: /Millbrook/ }))

    const roomList = () => document.querySelector('.room-list') as HTMLElement
    await waitFor(() => expect(roomList()).toBeTruthy())
    const inList = (name: RegExp) =>
      Array.from(roomList().querySelectorAll('button')).find((b) => name.test(b.textContent ?? ''))!

    fireEvent.click(inList(/The North Gate/))

    // Edit the title so there is unsaved prose.
    const title = (await screen.findByRole('textbox', { name: 'Title' })) as HTMLInputElement
    fireEvent.change(title, { target: { value: 'The Changed Gate' } })

    // Attempt to switch rooms - the guard must intervene.
    fireEvent.click(inList(/Market Row/))

    expect(await screen.findByText(/Discard unsaved changes/)).toBeTruthy()

    // Still on the original room until the user decides.
    const editor = document.querySelector('.room-editor')
    expect(editor?.textContent).toContain('north-gate')
  })
})
