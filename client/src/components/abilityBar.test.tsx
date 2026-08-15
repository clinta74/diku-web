// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import { AbilityBar } from './AbilityBar'
import { gameReducer, initialGameState } from '../state/gameReducer'
import { PULSE_MS, type AbilityEntry } from '../net/protocol'

const kick: AbilityEntry = {
  key: 'warden.kick',
  name: 'Kick',
  verb: 'kick',
  costType: 'Stamina',
  costValue: 10,
  cooldownPulses: 24, // 6s
  remainingPulses: 0,
  isSpell: false,
}

const bolt: AbilityEntry = {
  key: 'adept.bolt',
  name: 'Bolt',
  verb: 'cast bolt',
  costType: 'Focus',
  costValue: 15,
  cooldownPulses: 24,
  remainingPulses: 0,
  isSpell: true,
}

/** The fill as drawn, both halves of it: ten cells, some solid and the rest shaded. */
function bar(container: HTMLElement) {
  return container.querySelector('.ability-chip-bar')?.textContent ?? ''
}

/** How much of the fill is still solid. */
function filled(container: HTMLElement) {
  return [...bar(container)].filter((cell) => cell === '█').length
}

afterEach(cleanup)

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

it('shows nothing at all when nothing is cooling', () => {
  // The common case, and the reason the bar was rewritten: a level-45 character has a dozen
  // abilities and is not usually waiting on any of them. A row of ready chips is a row of things
  // the player already knows, taking space from the transcript.
  const { container } = render(
    <AbilityBar abilities={[kick, bolt]} cooldownUntil={{}} />,
  )

  expect(container.querySelector('.ability-bar')).toBeNull()
})

it('shows only the ability that is cooling', () => {
  const { container } = render(
    <AbilityBar
      abilities={[kick, bolt]}
      cooldownUntil={{ 'adept.bolt': Date.now() + 24 * PULSE_MS }}
    />,
  )

  expect(container.querySelectorAll('.ability-chip')).toHaveLength(1)
  expect(screen.getByText('Bolt')).toBeTruthy()
  expect(screen.queryByText('Kick')).toBeNull()
})

it('empties the fill as the cooldown runs', () => {
  const until = Date.now() + 24 * PULSE_MS // 6s

  const { container, rerender } = render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} />,
  )

  expect(filled(container)).toBe(10)
  expect(bar(container)).toHaveLength(10)

  act(() => void vi.advanceTimersByTime(3000))
  rerender(<AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} />)

  expect(filled(container)).toBe(5)
  expect(bar(container)).toHaveLength(10)
})

it('keeps a cell while the ability is still refused', () => {
  // At 200ms of a 6s cooldown the honest fraction is 3% of a cell. An empty bar there reads as
  // ready on something the server would still refuse, which is the same lie the old "0" told.
  const { container } = render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': Date.now() + 200 }} />,
  )

  expect(filled(container)).toBe(1)
})

it('disappears the moment the cooldown reaches zero', () => {
  const until = Date.now() + 24 * PULSE_MS

  const { container, rerender } = render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} />,
  )

  expect(container.querySelector('.ability-chip')).toBeTruthy()

  act(() => void vi.advanceTimersByTime(6000))
  rerender(<AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} />)

  expect(container.querySelector('.ability-bar')).toBeNull()
})

it('draws no more than a full bar when the roster disagrees with the cooldown', () => {
  // A builder shortening a cooldown while one is running leaves the client holding an instant
  // further out than the new duration allows. Clamped rather than trusted: the fraction would
  // otherwise exceed 1 and repeat() would draw a bar wider than the chip.
  const shortened = { ...kick, cooldownPulses: 4 } // 1s, against 6s still in flight
  const { container } = render(
    <AbilityBar
      abilities={[shortened]}
      cooldownUntil={{ 'warden.kick': Date.now() + 6000 }}
    />,
  )

  expect(bar(container)).toHaveLength(10)
  expect(filled(container)).toBe(10)
})

it('announces the remaining seconds, since the fill is unreadable aloud', () => {
  render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': Date.now() + 4000 }} />,
  )

  expect(screen.getByLabelText('Kick, 4 seconds')).toBeTruthy()
})

it('resyncs from the roster, because a reconnect missed every cooldown event', () => {
  // The property the roster carries remaining cooldowns for. A client that has been away has no
  // idea what fired while it was gone, and the ring buffer may even replay a stale cooldown event
  // for something that has since come back up.
  const stale = gameReducer(initialGameState, {
    kind: 'event',
    event: { type: 'cooldown', data: { key: 'warden.kick', pulses: 240 } },
  })

  expect(stale.cooldownUntil['warden.kick']).toBeGreaterThan(Date.now())

  const resynced = gameReducer(stale, {
    kind: 'event',
    event: { type: 'abilities', data: { abilities: [{ ...kick, remainingPulses: 0 }] } },
  })

  expect(resynced.cooldownUntil['warden.kick']).toBeUndefined()
  expect(resynced.abilities).toHaveLength(1)
})

it('starts a cooldown when one is announced', () => {
  const next = gameReducer(initialGameState, {
    kind: 'event',
    event: { type: 'cooldown', data: { key: 'adept.bolt', pulses: 24 } },
  })

  expect(next.cooldownUntil['adept.bolt']).toBe(Date.now() + 24 * PULSE_MS)
})
