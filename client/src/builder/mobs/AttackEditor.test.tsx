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
