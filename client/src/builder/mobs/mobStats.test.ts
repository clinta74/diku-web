import { describe, expect, it } from 'vitest'
import { clampChance, MOB_STAT_GROUPS, OWNED_MOB_STAT_KEYS, readLoot, writeLoot } from './mobStats'

/**
 * The keys `MobSpawner` and `CombatSystem` read off a mob template, transcribed from the engine.
 *
 * The editor offered exactly one of these — health — so a mob's damage, accuracy, and armour
 * were settable only by seeding or by SQL. A builder could make a mob but not make it dangerous.
 */
const KEYS_THE_ENGINE_READS = [
  'health',
  'damageMin',
  'damageMax',
  'baseDamage',
  'attackRating',
  'defense',
  'armor',
  'damageMultiplier',
]

describe('mob stat fields', () => {
  it('offers every stat the engine reads', () => {
    for (const key of KEYS_THE_ENGINE_READS) {
      expect(OWNED_MOB_STAT_KEYS).toContain(key)
    }
  })

  it('offers each key exactly once', () => {
    expect(OWNED_MOB_STAT_KEYS.length).toBe(new Set(OWNED_MOB_STAT_KEYS).size)
  })

  it('labels every field in words rather than in camelCase', () => {
    for (const group of MOB_STAT_GROUPS) {
      for (const field of group.fields) {
        expect(field.label).not.toBe(field.key)
        expect(field.label).not.toMatch(/[a-z][A-Z]/)
      }
    }
  })

  it('does not offer the item-side attack rating key', () => {
    // Items use `bonus`, mobs use `attackRating`. Offering the wrong one would write a key the
    // engine never reads, and the field would do nothing at all.
    expect(OWNED_MOB_STAT_KEYS).not.toContain('bonus')
  })
})

describe('loot table', () => {
  it('round-trips a table through read and write', () => {
    const stored = [{ itemTemplateKey: 'rusted-blade', chance: 0.25 }]

    expect(writeLoot(readLoot(stored))).toEqual(stored)
  })

  it('reads a chance that arrived as a string', () => {
    // The table is a free-form bag, so the value's runtime type depends on how it round-tripped.
    expect(readLoot([{ itemTemplateKey: 'x', chance: '0.5' }])[0].chance).toBe(0.5)
  })

  it('reads an unusable chance as zero rather than dropping the row', () => {
    // A row a builder can see and fix beats a row that vanishes on save.
    const rows = readLoot([{ itemTemplateKey: 'x', chance: {} }])

    expect(rows).toHaveLength(1)
    expect(rows[0].chance).toBe(0)
  })

  it('drops rows with no item chosen', () => {
    // The "Add drop" button creates an empty row; saving before choosing an item should not
    // write a loot entry pointing at nothing.
    expect(writeLoot([{ itemTemplateKey: '', chance: 0.5 }])).toEqual([])
  })

  it('trims the key it writes', () => {
    expect(writeLoot([{ itemTemplateKey: '  blade  ', chance: 0.5 }])[0].itemTemplateKey).toBe('blade')
  })

  it('clamps a chance to between never and always', () => {
    // Above 1 always drops and below 0 never does; both are almost certainly a typo, and the
    // engine compares against a 0-1 roll so neither reads as intended.
    expect(clampChance(1.5)).toBe(1)
    expect(clampChance(-2)).toBe(0)
    expect(clampChance(Number.NaN)).toBe(0)
    expect(clampChance(0.25)).toBe(0.25)
  })

  it('reads an absent table as no drops', () => {
    expect(readLoot(undefined)).toEqual([])
  })
})
