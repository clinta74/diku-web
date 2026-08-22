// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'

/**
 * The *Respawn zone* button (PLAN.md §7.5).
 *
 * Multipliers resolve at spawn time (§4.4), so a saved difficulty edit reaches the next spawn and
 * never the mob already standing in the room. This button is the only thing that closes that gap
 * short of a restart, and what is asserted here is the part a server test cannot see: that it is
 * reachable, that it says what happened, and that it is held shut while there are unsaved numbers
 * — a respawn applies what the server has, so offering it over a dirty form would show a builder
 * the previous save's numbers and look like the button had failed.
 */

const neutral = vi.hoisted(() => ({
  strength: 1,
  health: 1,
  damage: 1,
  xp: 1,
  gold: 1,
  itemValue: 1,
}))

const respawnZone = vi.hoisted(() => vi.fn())

const zone = {
  key: 'aldenmoor.millbrook',
  worldKey: 'aldenmoor',
  name: 'Millbrook',
  description: 'A village.',
  minLevel: 1,
  maxLevel: 5,
  flags: {},
  multipliers: neutral,
  roomCount: 2,
}

vi.mock('../../net/builderApi', () => ({
  MULTIPLIER_KEYS: ['strength', 'health', 'damage', 'xp', 'gold', 'itemValue'],
  NEUTRAL_MULTIPLIERS: neutral,
  builderApi: {
    respawnZone,
    zonePreview: () =>
      Promise.resolve({
        zoneKey: zone.key,
        worldMultipliers: neutral,
        zoneMultipliers: neutral,
        templates: [],
      }),
    updateZone: () => Promise.resolve(zone),
  },
}))

vi.mock('../BuilderData', () => ({
  useBuilderData: () => ({ zones: [zone], loadZones: () => Promise.resolve() }),
}))

import { ToastProvider } from '../../ui/Toast'
import { ZonePanel } from './ZonePanel'

afterEach(() => {
  cleanup()
  respawnZone.mockReset()
})

/** Renders the panel on its Difficulty section, which is where the button lives. */
async function renderDifficulty() {
  render(
    <ToastProvider>
      <ZonePanel zoneKey={zone.key} />
    </ToastProvider>,
  )

  // Radix activates a manual tab on a pointer press rather than a synthesised click.
  fireEvent.mouseDown(await screen.findByRole('tab', { name: 'Difficulty' }), { button: 0 })

  return await screen.findByRole('button', { name: /respawn zone/i })
}

it('reports what the respawn moved', async () => {
  respawnZone.mockResolvedValue({ zoneKey: zone.key, despawned: 4, spawned: 6 })

  fireEvent.click(await renderDifficulty())

  await waitFor(() => expect(respawnZone).toHaveBeenCalledWith(zone.key))
  expect(await screen.findByText('Respawned 6 (4 removed)')).toBeTruthy()
})

it('says so when a zone has nothing to respawn', async () => {
  // Otherwise "Respawned" alone leaves a builder waiting for a change to the world that was
  // never going to come - the zone has no mob spawners at all.
  respawnZone.mockResolvedValue({ zoneKey: zone.key, despawned: 0, spawned: 0 })

  fireEvent.click(await renderDifficulty())

  expect(await screen.findByText(/no mob spawners/)).toBeTruthy()
})

it('will not respawn over unsaved multipliers', async () => {
  const button = await renderDifficulty()

  const gold = screen.getByLabelText(/gold/i)
  fireEvent.change(gold, { target: { value: '3' } })

  await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(true))
  expect(respawnZone).not.toHaveBeenCalled()
})
