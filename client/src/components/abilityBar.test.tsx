// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { AbilityBar } from './AbilityBar'
import { gameReducer, initialGameState } from '../state/gameReducer'
import { PULSE_MS, type AbilityEntry } from '../net/protocol'

const kick: AbilityEntry = {
  key: 'warden.kick',
  name: 'Kick',
  verb: 'kick',
  costType: 'Stamina',
  costValue: 10,
  cooldownPulses: 24,
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

afterEach(cleanup)

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

it('shows nothing at all when the character has no abilities', () => {
  // A level-1 character of a Path with nothing yet, and every screen before the roster arrives.
  // An empty bar would be a strip of border with no content.
  const { container } = render(
    <AbilityBar abilities={[]} cooldownUntil={{}} onPick={() => {}} />,
  )

  expect(container.querySelector('.ability-bar')).toBeNull()
})

it('counts a cooldown down in seconds', () => {
  const until = Date.now() + 24 * PULSE_MS // 6s

  const { rerender } = render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} onPick={() => {}} />,
  )

  expect(screen.getByText('6')).toBeTruthy()

  act(() => void vi.advanceTimersByTime(2000))
  rerender(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} onPick={() => {}} />,
  )

  expect(screen.getByText('4')).toBeTruthy()
})

it('rounds the countdown up, so nothing reads ready while it is still refused', () => {
  // At 200ms left, "0" would be a lie the server would then contradict with a refusal.
  const until = Date.now() + 200

  render(
    <AbilityBar abilities={[kick]} cooldownUntil={{ 'warden.kick': until }} onPick={() => {}} />,
  )

  expect(screen.getByText('1')).toBeTruthy()
})

it('shows the cost once an ability is ready', () => {
  render(<AbilityBar abilities={[kick]} cooldownUntil={{}} onPick={() => {}} />)

  expect(screen.getByText('10')).toBeTruthy()
})

it('fills the input rather than casting', () => {
  // A click that fired the ability would be a way to spend a resource by brushing a trackpad. The
  // game is typed; the bar teaches the verb.
  const picked: string[] = []
  render(<AbilityBar abilities={[bolt]} cooldownUntil={{}} onPick={(v) => picked.push(v)} />)

  fireEvent.click(screen.getByRole('button', { name: /Bolt/ }))

  // "cast bolt", not "bolt": `cast` refuses a skill and the verb form always works, so the server
  // decides which word this is and the client repeats it.
  expect(picked).toEqual(['cast bolt'])
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
