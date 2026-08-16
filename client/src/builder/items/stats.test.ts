import { describe, expect, it } from 'vitest'
import { asMultiplier, OWNED_STAT_KEYS, STAT_GROUPS } from './stats'

/**
 * The keys `EquipmentResolver` reads off an equipped item, transcribed from the resolver itself.
 *
 * This list has been wrong in both directions. It first offered a subset, which is why an authored
 * breastplate did nothing. Then the armour rework replaced `armorFlat` and `armorPercent` with one
 * `armor` rating through `ArmorCurve` and dropped `armorMultiplier`, and the transcription was not
 * updated — so the form kept three inert armour fields and had no way to set the live one.
 *
 * A transcription checked by hand drifts. `tools/check-builder-keys.py` now reads both sides and
 * fails on a key the engine does not name; this list is the readable half of the same rule.
 */
const KEYS_THE_ENGINE_READS = [
  // Weapon, main hand only.
  'bonus',
  'damageMin',
  'damageMax',
  'baseDamage',
  'damageMultiplier',
  // Armour slots only.
  'armor',
  'defense',
]

describe('item stat fields', () => {
  it('offers every stat the combat engine reads', () => {
    for (const key of KEYS_THE_ENGINE_READS) {
      expect(OWNED_STAT_KEYS).toContain(key)
    }
  })

  it('offers each key exactly once', () => {
    // A key in two groups would render two fields writing the same bag entry, and whichever was
    // edited last would win.
    expect(OWNED_STAT_KEYS.length).toBe(new Set(OWNED_STAT_KEYS).size)
  })

  it('labels every field in words rather than in camelCase', () => {
    for (const group of STAT_GROUPS) {
      for (const field of group.fields) {
        expect(field.label).not.toBe(field.key)
        expect(field.label).not.toMatch(/[a-z][A-Z]/)
      }
    }
  })

  it('gives every group a label and at least one field', () => {
    for (const group of STAT_GROUPS) {
      expect(group.label.trim()).not.toBe('')
      expect(group.fields.length).toBeGreaterThan(0)
    }
  })

  it('marks whole-number stats as integers', () => {
    // An armour rating of 2.5 is not something the resolver can use - it reads these with an int
    // parser and would drop the value entirely.
    const kinds = new Map(STAT_GROUPS.flatMap((g) => g.fields).map((f) => [f.key, f.kind]))

    expect(kinds.get('armor')).toBe('int')
    expect(kinds.get('defense')).toBe('int')
    expect(kinds.get('damageMin')).toBe('int')
    expect(kinds.get('damageMax')).toBe('int')
    expect(kinds.get('baseDamage')).toBe('int')
    expect(kinds.get('bonus')).toBe('int')
  })

  it('marks proportional stats as decimals', () => {
    const kinds = new Map(STAT_GROUPS.flatMap((g) => g.fields).map((f) => [f.key, f.kind]))

    expect(kinds.get('damageMultiplier')).toBe('decimal')
  })

  it('offers no key the engine has retired', () => {
    // The armour rework's casualties, named so the failure says what happened rather than just
    // that a count changed. Stored, exported, and never read - the quietest failure here.
    for (const dead of [
      'armorFlat',
      'armorPercent',
      'armorMultiplier',
      'healthMultiplier',
      'focusMultiplier',
      'staminaMultiplier',
    ]) {
      expect(OWNED_STAT_KEYS).not.toContain(dead)
    }
  })
})

/**
 * The item editor renders five multiplier fields, but `baseStats` is a schemaless bag that also
 * carries values it has no field for — `damage: "1d6"` above all. It used to load the whole bag
 * through `Number(v) || 0` and save the result, so editing an item's *name* silently rewrote its
 * damage dice to 0. Nothing failed; the item just quietly stopped hitting.
 *
 * `asMultiplier` is the guard: it reads a value for display without ever claiming a value the
 * form cannot represent, so untouched keys are left exactly as they were found.
 */
describe('asMultiplier', () => {
  it('reads a number', () => {
    expect(asMultiplier(1.5)).toBe(1.5)
  })

  it('reads a number that arrived as a string', () => {
    // jsonb round-trips can hand back either shape; both mean the same multiplier.
    expect(asMultiplier('1.5')).toBe(1.5)
  })

  it('reads zero as zero rather than as absent', () => {
    // 0 is a meaningful multiplier (it zeroes the stat); blank is what removes the key.
    expect(asMultiplier(0)).toBe(0)
  })

  it('treats a dice string as unrepresentable rather than as 0', () => {
    // The whole bug in one assertion: `Number("1d6") || 0` was 0.
    expect(asMultiplier('1d6')).toBeUndefined()
  })

  it('treats a damage range as unrepresentable rather than as 0', () => {
    expect(asMultiplier('4-7')).toBeUndefined()
  })

  it('treats absent, blank, and non-finite values as unrepresentable', () => {
    expect(asMultiplier(undefined)).toBeUndefined()
    expect(asMultiplier(null)).toBeUndefined()
    expect(asMultiplier('')).toBeUndefined()
    expect(asMultiplier('   ')).toBeUndefined()
    expect(asMultiplier(Number.NaN)).toBeUndefined()
    expect(asMultiplier(Number.POSITIVE_INFINITY)).toBeUndefined()
  })

  it('does not coerce a boolean or an object into a number', () => {
    // Number(true) is 1 and Number([]) is 0 — both would invent a multiplier from nothing.
    expect(asMultiplier(true)).toBeUndefined()
    expect(asMultiplier([])).toBeUndefined()
    expect(asMultiplier({})).toBeUndefined()
  })
})
