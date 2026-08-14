import { describe, expect, it } from 'vitest'
import {
  ALL_EFFECTS,
  ATTACK_EFFECTS,
  effectOption,
  paramToDisplay,
  paramToStored,
  pruneParams,
} from './effects'

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
      'buff.defense',
      'debuff.expose',
      'buff.max-health',
    ]

    // Both lists, not just the attack subset. An ability effect with no executor behind it is the
    // same silent failure as an attack rider with none.
    for (const effect of ALL_EFFECTS) {
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

describe('durations in seconds', () => {
  const seconds = ATTACK_EFFECTS.find((e) => e.key === 'control.stun')!.params.find(
    (p) => p.key === 'durationPulses',
  )!
  const plain = ATTACK_EFFECTS.find((e) => e.key === 'debuff.weaken')!.params.find(
    (p) => p.key === 'outgoingMultiplier',
  )!

  it('shows a stored pulse count as seconds', () => {
    // A pulse is 250ms and an engine detail. Five builder fields asked for pulses while two beside
    // them asked for seconds, so authoring one mob meant converting between units mid-form.
    expect(paramToDisplay(seconds, '8')).toBe('2')
    expect(paramToDisplay(seconds, '24')).toBe('6')
  })

  it('stores what was typed back in pulses', () => {
    expect(paramToStored(seconds, '2')).toBe('8')
    expect(paramToStored(seconds, '6')).toBe('24')
  })

  it('keeps quarter-second precision rather than rounding to whole seconds', () => {
    // 10 pulses is 2.5s. Rounding to whole seconds would silently retune anything authored on an
    // odd pulse count.
    expect(paramToDisplay(seconds, '10')).toBe('2.5')
    expect(paramToStored(seconds, '2.5')).toBe('10')
  })

  it('leaves a parameter that is not a duration alone', () => {
    expect(paramToDisplay(plain, '0.75')).toBe('0.75')
    expect(paramToStored(plain, '0.75')).toBe('0.75')
  })

  it('passes a blank through untouched', () => {
    // Converting "" to 0 would fight somebody clearing a field.
    expect(paramToDisplay(seconds, '')).toBe('')
    expect(paramToStored(seconds, '')).toBe('')
  })

  it('converts a half-typed decimal, which is why ParamInput holds the raw text', () => {
    // Number("2.") is 2, not NaN, so this returns 8 pulses and the field would redisplay "2" -
    // eating the dot as fast as it is typed and making "2.5" unreachable. The conversion is right;
    // it is the round trip through a controlled input that cannot happen on every keystroke.
    expect(paramToStored(seconds, '2.')).toBe('8')
  })
})
