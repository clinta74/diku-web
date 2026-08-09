import type { StatGroup } from '../items/stats'

/**
 * The `baseStats` keys the combat engine reads off a mob.
 *
 * The mob editor offered exactly one — health — so every other number a mob can carry was
 * unreachable from the browser. A mob's damage, its chance to hit, and its armour were all
 * settable only by seeding or by SQL, which is the same gap the item editor had.
 *
 * The keys differ from the item side in two places worth knowing: a mob's attack rating is
 * `attackRating` where an item's is `bonus`, and a mob may declare its damage as a range string
 * (`"4-7"`) in `damage` as well as through `damageMin`/`damageMax`.
 */
export const MOB_STAT_GROUPS: StatGroup[] = [
  {
    label: 'Body',
    fields: [
      {
        key: 'health',
        label: 'Health',
        hint: 'Scaled at spawn by the zone’s Strength multiplier.',
        kind: 'int',
      },
      {
        key: 'defense',
        label: 'Defence',
        hint: 'Raises the roll an attacker must beat. Left blank it derives from level.',
        kind: 'int',
      },
      {
        key: 'armorFlat',
        label: 'Flat reduction',
        hint: 'Subtracted from each hit. A hit never drops below 1.',
        kind: 'int',
      },
      {
        key: 'armorPercent',
        label: 'Damage reduction',
        hint: '0.2 takes a fifth off every hit. Capped at 0.95.',
        kind: 'decimal',
      },
    ],
  },
  {
    label: 'Attack',
    hint: 'Declared numbers win; anything left blank is derived from the mob’s level.',
    fields: [
      { key: 'damageMin', label: 'Damage min', kind: 'int' },
      { key: 'damageMax', label: 'Damage max', kind: 'int' },
      {
        key: 'baseDamage',
        label: 'Flat damage',
        hint: 'Added after the roll, once.',
        kind: 'int',
      },
      {
        key: 'attackRating',
        label: 'Attack rating',
        hint: 'Added to the mob’s d20. The item-side key for this is “bonus”.',
        kind: 'int',
      },
    ],
  },
  {
    label: 'Multipliers',
    hint: '1.0 is no change. These scale the numbers above, so a multiplier alone does nothing.',
    fields: [
      { key: 'damageMultiplier', label: 'Damage multiplier', kind: 'decimal' },
      { key: 'armorMultiplier', label: 'Armour multiplier', kind: 'decimal' },
    ],
  },
]

/** Every key the mob form owns, so anything else in the bag shows as carried through. */
export const OWNED_MOB_STAT_KEYS: string[] = MOB_STAT_GROUPS.flatMap((g) =>
  g.fields.map((f) => f.key),
)

/** One row of a mob's loot table: an item key and the chance it drops. */
export interface LootRow {
  itemTemplateKey: string
  chance: number
}

/**
 * Reads a stored loot table into rows the editor can edit.
 *
 * The table is a list of free-form bags, so the chance may arrive as a number, a string, or a
 * `JsonElement` depending on how it round-tripped. Anything unreadable becomes 0 rather than
 * being dropped — a row a builder can see and fix beats a row that silently vanishes on save.
 */
export function readLoot(raw: Array<Record<string, unknown>> | undefined): LootRow[] {
  if (!Array.isArray(raw)) return []

  return raw.map((entry) => ({
    itemTemplateKey: String(entry?.itemTemplateKey ?? ''),
    chance: toChance(entry?.chance),
  }))
}

/** Folds rows back into the stored shape, dropping ones with no item chosen yet. */
export function writeLoot(rows: LootRow[]): Array<Record<string, unknown>> {
  return rows
    .filter((row) => row.itemTemplateKey.trim() !== '')
    .map((row) => ({
      itemTemplateKey: row.itemTemplateKey.trim(),
      chance: clampChance(row.chance),
    }))
}

/** 0 to 1. A chance above 1 always drops and below 0 never does; both are almost certainly typos. */
export function clampChance(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.min(1, Math.max(0, value))
}

function toChance(raw: unknown): number {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : 0
  if (typeof raw === 'string') {
    const parsed = Number(raw)
    return Number.isFinite(parsed) ? parsed : 0
  }
  return 0
}
