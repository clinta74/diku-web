// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import type { TemplatePlacement } from '../../net/builderApi'

/**
 * The placement rail (PLAN.md §7.9).
 *
 * What is asserted here is the reading rather than the join — the server tests own whether the
 * rows are right. This owns whether a builder can tell the three states apart: placed, placed
 * somewhere that no longer exists, and placed nowhere at all. The third is the one that matters,
 * because it is a mob nobody will ever meet and it looks identical to a finished one from inside
 * the editor.
 */

const mobPlacement = vi.hoisted(() => vi.fn())
const itemPlacement = vi.hoisted(() => vi.fn())

vi.mock('../../net/builderApi', () => ({
  builderApi: { mobPlacement, itemPlacement },
}))

import { TemplatePlacementPanel } from './TemplatePlacementPanel'

afterEach(() => {
  cleanup()
  mobPlacement.mockReset()
  itemPlacement.mockReset()
})

const empty: TemplatePlacement = {
  templateKey: 'rat',
  kind: 'mob',
  spawners: [],
  droppedBy: [],
  soldBy: [],
  quests: [],
}

/** Renders the panel with the current path echoed, so a navigation can be asserted on. */
function renderPanel(kind: 'mob' | 'item', templateKey = 'rat') {
  function Path() {
    return <span data-testid="path">{useLocation().pathname}</span>
  }

  return render(
    <MemoryRouter initialEntries={['/builder/mobs/rat']}>
      <Path />
      <Routes>
        <Route
          path="*"
          element={<TemplatePlacementPanel kind={kind} templateKey={templateKey} revision={0} />}
        />
      </Routes>
    </MemoryRouter>,
  )
}

it('lists each spawner with the rooms it fills', async () => {
  mobPlacement.mockResolvedValue({
    ...empty,
    spawners: [
      {
        id: 'a',
        zoneKey: 'aldenmoor.millbrook',
        zoneName: 'Millbrook',
        targetCount: 2,
        respawnSeconds: 60,
        fightsAtLevel: 5,
        rooms: [
          { key: 'aldenmoor.millbrook.burrow', title: 'The Rat Burrow' },
          { key: 'aldenmoor.millbrook.ditch', title: 'A Wet Ditch' },
        ],
      },
    ],
  } satisfies TemplatePlacement)

  renderPanel('mob')

  expect(await screen.findByText('Millbrook')).toBeTruthy()
  expect(screen.getByText(/fights at level 5/)).toBeTruthy()
  expect(screen.getByRole('button', { name: 'The Rat Burrow' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'A Wet Ditch' })).toBeTruthy()
})

it('goes to the room when one is clicked', async () => {
  // The point of the rail: the next thing a builder does after finding out where a mob spawns is
  // go and look at it.
  mobPlacement.mockResolvedValue({
    ...empty,
    spawners: [
      {
        id: 'a',
        zoneKey: 'aldenmoor.millbrook',
        zoneName: 'Millbrook',
        targetCount: 1,
        respawnSeconds: 60,
        fightsAtLevel: 3,
        rooms: [{ key: 'aldenmoor.millbrook.burrow', title: 'The Rat Burrow' }],
      },
    ],
  } satisfies TemplatePlacement)

  renderPanel('mob')
  fireEvent.click(await screen.findByRole('button', { name: 'The Rat Burrow' }))

  expect(screen.getByTestId('path').textContent).toBe(
    '/builder/world/aldenmoor/millbrook/burrow/details',
  )
})

it('shows a spawner room that no longer exists rather than hiding it', async () => {
  mobPlacement.mockResolvedValue({
    ...empty,
    spawners: [
      {
        id: 'a',
        zoneKey: 'aldenmoor.millbrook',
        zoneName: 'Millbrook',
        targetCount: 1,
        respawnSeconds: 60,
        fightsAtLevel: 3,
        rooms: [{ key: 'aldenmoor.millbrook.gone', title: null }],
      },
    ],
  } satisfies TemplatePlacement)

  renderPanel('mob')

  expect(await screen.findByText('aldenmoor.millbrook.gone')).toBeTruthy()
  expect(screen.getByText(/no such room/)).toBeTruthy()
})

it('says plainly when nothing places a mob', async () => {
  mobPlacement.mockResolvedValue(empty)

  renderPanel('mob')

  expect(await screen.findByText(/nobody will ever meet it/)).toBeTruthy()
})

it('names an item’s drops, its shops, and the quest that hands it over', async () => {
  itemPlacement.mockResolvedValue({
    templateKey: 'lamp',
    kind: 'item',
    spawners: [],
    droppedBy: [{ key: 'brute', name: 'a brute', placed: true, chance: 0.25 }],
    soldBy: [{ key: 'trader', name: 'a trader', placed: true, chance: null }],
    quests: [
      { key: 'lost-ledger', name: 'The Lost Ledger', zoneKey: 'aldenmoor.millbrook', role: 'reward' },
    ],
  } satisfies TemplatePlacement)

  renderPanel('item', 'lamp')

  expect(await screen.findByText('Dropped by')).toBeTruthy()
  expect(screen.getByRole('button', { name: 'a brute' })).toBeTruthy()
  expect(screen.getByText(/25%/)).toBeTruthy()
  expect(screen.getByText('Sold by')).toBeTruthy()
  expect(screen.getByRole('button', { name: 'a trader' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'The Lost Ledger' })).toBeTruthy()
})

it('calls out loot on a mob nothing places', async () => {
  // Loot on an unplaced mob is loot nobody can reach, and neither template's editor shows it.
  itemPlacement.mockResolvedValue({
    templateKey: 'lamp',
    kind: 'item',
    spawners: [],
    droppedBy: [{ key: 'ghost', name: 'a ghost', placed: false, chance: 1 }],
    soldBy: [],
    quests: [],
  } satisfies TemplatePlacement)

  renderPanel('item', 'lamp')

  expect(await screen.findByText(/no spawner places it/)).toBeTruthy()
  // A certain drop reads as "always"; a percentage would invite the reader to look for the odds.
  expect(screen.getByText(/always/)).toBeTruthy()
})

it('distinguishes an item that only exists as a reward', async () => {
  // A real thing to author - it is how an epic stays unbuyable (§4.13) - and also exactly what a
  // forgotten loot table looks like.
  itemPlacement.mockResolvedValue({
    templateKey: 'epic-blade',
    kind: 'item',
    spawners: [],
    droppedBy: [],
    soldBy: [],
    quests: [
      { key: 'the-long-road', name: 'The Long Road', zoneKey: 'ossara.gatetown', role: 'reward' },
    ],
  } satisfies TemplatePlacement)

  renderPanel('item', 'epic-blade')

  expect(await screen.findByText(/no source in the world itself/)).toBeTruthy()
})

it('asks for nothing until a template is selected', () => {
  renderPanel('mob', null as unknown as string)

  expect(mobPlacement).not.toHaveBeenCalled()
  expect(screen.getByText(/Select a template/)).toBeTruthy()
})
