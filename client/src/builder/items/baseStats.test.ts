import { describe, expect, it } from 'vitest'
import { asMultiplier } from './baseStats'

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
