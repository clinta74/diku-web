import { describe, expect, it } from 'vitest'
import { ATTACK_EFFECTS, effectOption, pruneParams } from './effects'

describe('the offered effects', () => {
  /**
   * A rider applies to whoever the attack hit, so a helpful effect would mean a mob buffing or
   * mending the player it just struck. Offering one is how that happens by accident.
   */
  it('offers only effects that make sense aimed at whoever was hit', () => {
    const offered = ATTACK_EFFECTS.map((e) => e.key)

    expect(offered).not.toContain('heal.restore')
    expect(offered).not.toContain('buff.damage-up')
  })

  it('names effect keys the engine registry actually has', () => {
    // Transcribed from DikuWeb.Domain/Abilities/Effects. A key with no executor behind it is an
    // attack that swings for damage and applies nothing, with no error anywhere.
    const known = [
      'damage.physical',
      'heal.restore',
      'buff.damage-up',
      'debuff.weaken',
      'damage.overtime',
      'control.stun',
      'control.root',
      'control.taunt',
    ]

    for (const effect of ATTACK_EFFECTS) {
      expect(known).toContain(effect.key)
    }
  })

  it('states what each parameter does when left blank', () => {
    // The executors skip what they do not recognise and fall back silently, so a blank field is
    // only a real choice if the builder can see what it means.
    for (const effect of ATTACK_EFFECTS) {
      expect(effect.params.length).toBeGreaterThan(0)
      for (const param of effect.params) {
        expect(param.fallback).not.toBe('')
      }
    }
  })

  it('gives every effect a name field, since that is what the player is shown', () => {
    for (const effect of ATTACK_EFFECTS) {
      expect(effect.params.map((p) => p.key)).toContain('name')
    }
  })
})

describe('effectOption', () => {
  it('finds a known effect', () => {
    expect(effectOption('control.stun')?.label).toBe('Stun')
  })

  it('returns null for none, blank, or unknown', () => {
    expect(effectOption(null)).toBeNull()
    expect(effectOption('')).toBeNull()
    expect(effectOption('control.disintegrate')).toBeNull()
  })
})

describe('pruneParams', () => {
  it('keeps the parameters the chosen effect reads', () => {
    expect(pruneParams('control.stun', { durationPulses: '12', name: 'reeling' })).toEqual({
      durationPulses: '12',
      name: 'reeling',
    })
  })

  it('drops parameters belonging to a previously chosen effect', () => {
    // Switching the dropdown must not leave the old effect's keys behind. The executor would skip
    // them, so the row would read as meaning something it does not do.
    expect(pruneParams('control.stun', { tickDamage: '7', durationPulses: '12' })).toEqual({
      durationPulses: '12',
    })
  })

  it('drops blanks rather than sending empty strings', () => {
    expect(pruneParams('control.stun', { durationPulses: '  ', name: 'reeling' })).toEqual({
      name: 'reeling',
    })
  })

  it('collapses an all-blank bag to null', () => {
    // Null, not {}, so a plain attack round-trips as a plain attack.
    expect(pruneParams('control.stun', { durationPulses: '' })).toBeNull()
    expect(pruneParams('control.stun', {})).toBeNull()
  })

  it('returns null when no effect is chosen', () => {
    expect(pruneParams(null, { durationPulses: '12' })).toBeNull()
  })
})
