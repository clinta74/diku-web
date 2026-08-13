// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { Ability } from '../../net/builderApi'

const kick = vi.hoisted(
  (): Ability => ({
    key: 'warden.kick',
    path: 'Warden',
    unlockLevel: 1,
    name: 'Kick',
    description: 'A boot to the knee.',
    costType: 'Stamina',
    costValue: 10,
    cooldownPulses: 24,
    castTimePulses: null,
    targetingType: 'SingleTarget',
    effectKey: 'damage.physical',
    effectParams: { scalingFactor: '1.1', minDamage: '3' },
    problems: [],
  }),
)

const broken = vi.hoisted(
  (): Ability => ({
    key: 'adept.misfire',
    path: 'Adept',
    unlockLevel: 5,
    name: 'Misfire',
    description: 'Authored against an effect that does not exist.',
    costType: 'Focus',
    costValue: 12,
    cooldownPulses: 24,
    castTimePulses: null,
    targetingType: 'SingleTarget',
    effectKey: 'damage.nonexistent',
    effectParams: {},
    problems: [
      { severity: 'Error', message: "No effect executor is registered for 'damage.nonexistent'." },
    ],
  }),
)

const calls = vi.hoisted(() => ({ list: 0, updated: null as unknown, fail: false }))

vi.mock('../../net/builderApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../net/builderApi')>()
  return {
    ...actual,
    builderApi: {
      abilities: () => {
        calls.list++
        return calls.fail
          ? Promise.reject(new Error('Request failed: 404'))
          : Promise.resolve([kick, broken])
      },
      ability: (key: string) => Promise.resolve(key === kick.key ? kick : broken),
      updateAbility: (_key: string, body: unknown) => {
        calls.updated = body
        return Promise.resolve(kick)
      },
    },
  }
})

import { ToastProvider } from '../../ui/Toast'
import { AbilitiesTab } from './AbilitiesTab'

beforeEach(() => {
  calls.list = 0
  calls.updated = null
  calls.fail = false
})

afterEach(cleanup)

function renderTab(path = '/builder/abilities') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/builder/abilities/:abilityKey?" element={<AbilitiesTab />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  )
}

/**
 * The property that matters most, and the reason this file exists at all: the screen actually
 * calls the API. §12's recurring lesson is an endpoint written, checked off, and wired to nothing
 * — the quest editor was claimed in the plan for months while `builderApi`'s quest functions had
 * zero callers. Asserting on rendered text alone would pass against a component holding a
 * hardcoded list.
 */
it('asks the server for the abilities', async () => {
  renderTab()

  await waitFor(() => expect(calls.list).toBeGreaterThan(0))
  expect(await screen.findByText('Kick')).toBeTruthy()
})

it('groups by Path so a progression can be read down the column', async () => {
  renderTab()

  expect(await screen.findByText('Warden')).toBeTruthy()
  expect(await screen.findByText('Adept')).toBeTruthy()
})

it('marks an ability that will not work', async () => {
  // The list is the only place a builder finds out about a row that arrived by import or by hand,
  // since nobody saw a save-time refusal for those.
  renderTab()

  await screen.findByText('Misfire')
  expect(document.querySelector('.ability-flag.bad')).toBeTruthy()
})

it('shows the reason on the ability itself', async () => {
  renderTab('/builder/abilities/adept.misfire')

  expect(await screen.findByText(/No effect executor is registered/)).toBeTruthy()
})

it('saves an edited cooldown', async () => {
  renderTab('/builder/abilities/warden.kick')

  const cooldown = await screen.findByDisplayValue('24')
  fireEvent.change(cooldown, { target: { value: '48' } })

  const save = screen.getByRole('button', { name: 'Save' })
  await waitFor(() => expect(save.hasAttribute('disabled')).toBe(false))
  fireEvent.click(save)

  await waitFor(() => expect(calls.updated).not.toBeNull())
  expect((calls.updated as { cooldownPulses: number }).cooldownPulses).toBe(48)
})

it('says so when the list cannot be loaded', async () => {
  // Reported from a dev server: the tab was empty and there was no way to tell an empty database
  // from a request that failed. It was `.catch(() => [])`, so a 404 from a server started before
  // this tab existed rendered as "this game has no abilities" - the exact silent-failure shape the
  // ability validator exists to prevent, reintroduced in the screen built to show it.
  calls.fail = true
  renderTab()

  expect(await screen.findByText(/404/)).toBeTruthy()
})

it('does not offer Save until something changed', async () => {
  // A Save that is always live invites a no-op write, and every write here lands a content_audit
  // row - so "who changed this ability" fills up with saves that changed nothing.
  renderTab('/builder/abilities/warden.kick')

  await screen.findByDisplayValue('24')
  expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true)
})
