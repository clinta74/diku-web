// @vitest-environment jsdom
import { useState } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { MobAttack } from '../../net/builderApi'
import { AttackEditor } from './AttackEditor'

afterEach(cleanup)

const attack = (verb: string): MobAttack => ({
  verb,
  delayPulses: 8,
  damageMultiplier: null,
  effectKey: null,
  effectParams: null,
})

/** The editor is controlled, so the state it edits has to live somewhere for the test to see it. */
function Harness({ initial }: { initial: MobAttack[] }) {
  const [attacks, setAttacks] = useState(initial)
  return (
    <>
      <AttackEditor attacks={attacks} onChange={setAttacks} />
      <pre data-testid="state">{JSON.stringify(attacks)}</pre>
    </>
  )
}

const state = (): MobAttack[] => JSON.parse(screen.getByTestId('state').textContent ?? '[]')

describe('AttackEditor', () => {
  it('gives each attack one card holding its timing and its effect', () => {
    render(<Harness initial={[attack('bite'), attack('claw')]} />)

    // Two cards, and each carries a full set of fields - not two lists of halves.
    const cards = document.querySelectorAll('.attack-card')
    expect(cards).toHaveLength(2)
    for (const card of cards) {
      expect(card.querySelectorAll('input, select').length).toBeGreaterThanOrEqual(4)
    }
  })

  it('keeps an effect on the attack it was chosen for', () => {
    render(<Harness initial={[attack('bite'), attack('claw')]} />)

    fireEvent.change(screen.getAllByLabelText(/On a hit/)[1], {
      target: { value: 'control.stun' },
    })

    // The property the old two-list layout only correlated by position and by a quoted verb.
    expect(state()[0].effectKey).toBeNull()
    expect(state()[1].effectKey).toBe('control.stun')
  })

  it('tells two attacks apart when they share a verb', () => {
    // A fresh attack defaults to "hit", so clicking Add twice produced two blocks whose only
    // label was the same quoted word.
    render(<Harness initial={[attack('hit'), attack('hit')]} />)

    expect(screen.getByText(/Attack 1/)).toBeTruthy()
    expect(screen.getByText(/Attack 2/)).toBeTruthy()
  })

  it('shows an effect’s parameters only once it has an effect', () => {
    render(<Harness initial={[attack('bite')]} />)
    expect(document.querySelector('.attack-effect')).toBeNull()

    fireEvent.change(screen.getByLabelText(/On a hit/), {
      target: { value: 'damage.overtime' },
    })

    const params = document.querySelector('.attack-effect')
    expect(params).not.toBeNull()
    expect(screen.getByLabelText(/Damage per tick/)).toBeTruthy()
  })

  it('drops the previous effect’s parameters when the effect changes', () => {
    render(<Harness initial={[attack('bite')]} />)

    fireEvent.change(screen.getByLabelText(/On a hit/), { target: { value: 'damage.overtime' } })
    fireEvent.change(screen.getByLabelText(/Damage per tick/), { target: { value: '9' } })
    expect(state()[0].effectParams).toEqual({ tickDamage: '9' })

    fireEvent.change(screen.getByLabelText(/On a hit/), { target: { value: 'control.stun' } })

    // A stale tickDamage on a stun is invisible rather than wrong, which is worse.
    expect(state()[0].effectParams).toBeNull()
  })

  it('removes the attack whose button was pressed', () => {
    render(<Harness initial={[attack('bite'), attack('claw'), attack('gore')]} />)

    fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[1])

    expect(state().map((a) => a.verb)).toEqual(['bite', 'gore'])
  })

  it('adds an attack that already works', () => {
    render(<Harness initial={[]} />)
    expect(screen.getByText('No attacks of its own.')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Add attack' }))

    expect(state()).toEqual([
      { verb: 'hit', delayPulses: 8, damageMultiplier: null, effectKey: null, effectParams: null },
    ])
  })
})

/**
 * A multiplier that cannot take a decimal is not a multiplier.
 *
 * The field was fully controlled off the parsed number, so typing "1." parsed as 1 and re-rendered
 * as "1" — the point erased as it was typed. Only whole multipliers were reachable, on a dial whose
 * authored values are 1.5, 2.4, 3.6. The same bug the weapon delay had, in the one place a
 * multiplier is still a real dial.
 */
describe('the damage multiplier', () => {
  it('accepts a decimal typed one character at a time', () => {
    render(<Harness initial={[attack('bite')]} />)

    const field = screen.getByLabelText('Damage multiplier') as HTMLInputElement

    for (const so_far of ['1', '1.', '1.5']) {
      fireEvent.change(field, { target: { value: so_far } })
      expect(field.value).toBe(so_far)
    }

    expect(state()[0].damageMultiplier).toBe(1.5)
  })

  it('reads blank as the mob using its own damage', () => {
    render(<Harness initial={[{ ...attack('bite'), damageMultiplier: 2.4 }]} />)

    const field = screen.getByLabelText('Damage multiplier') as HTMLInputElement
    expect(field.value).toBe('2.4')

    fireEvent.change(field, { target: { value: '' } })

    expect(state()[0].damageMultiplier).toBeNull()
  })
})
