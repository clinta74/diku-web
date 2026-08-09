/**
 * The `baseStats` keys the combat engine actually reads off an equipped item.
 *
 * Every key here is one `EquipmentResolver` looks for by name. The editor used to offer only the
 * five multipliers, which left the multipliers with nothing to multiply: an authored breastplate
 * had `armorMultiplier` and no `armorFlat`, so its protection resolved to `0 × 2 = 0`. Damage
 * escaped that only because unarmed combat has a 1-2 baseline for `damageMultiplier` to scale.
 *
 * Labels are written out in words. The raw key is what goes in the bag, but a form that shows
 * "armorMultiplier" is asking a builder to read camelCase to find out what a field does.
 */
export type StatKind = 'int' | 'decimal'

export interface StatField {
  /** The key written into `baseStats` — must match what the resolver reads. */
  key: string
  label: string
  hint?: string
  kind: StatKind
}

export interface StatGroup {
  label: string
  hint?: string
  fields: StatField[]
}

export const STAT_GROUPS: StatGroup[] = [
  {
    label: 'As a weapon',
    hint: 'Read from the main hand only. Leave blank and the wielder falls back to unarmed 1-2.',
    fields: [
      {
        key: 'damageMin',
        label: 'Damage min',
        hint: 'Low end of the damage roll.',
        kind: 'int',
      },
      {
        key: 'damageMax',
        label: 'Damage max',
        hint: 'High end. A critical rolls the dice twice.',
        kind: 'int',
      },
      {
        key: 'baseDamage',
        label: 'Flat damage',
        hint: 'Added after the roll, once, even on a critical.',
        kind: 'int',
      },
      {
        key: 'bonus',
        label: 'Attack rating',
        hint: 'Added to the d20. The target is hit on 10 + their defence, so +2 is a real swing.',
        kind: 'int',
      },
    ],
  },
  {
    label: 'As armour',
    hint: 'Read from armour slots only — a weapon carrying these does nothing with them.',
    fields: [
      {
        key: 'armorPercent',
        label: 'Damage reduction',
        hint: '0.1 takes a tenth off every hit. Capped at 0.95, and scales evenly at every level.',
        kind: 'decimal',
      },
      {
        key: 'armorFlat',
        label: 'Flat reduction',
        hint: 'Subtracted before the percentage. A hit never drops below 1, so large values trivialise weak enemies.',
        kind: 'int',
      },
      {
        key: 'defense',
        label: 'Defence',
        hint: 'Raises the roll an attacker must beat. Avoids the hit rather than shrinking it.',
        kind: 'int',
      },
    ],
  },
  {
    label: 'Multipliers',
    hint: '1.0 is no change. These scale the numbers above, so a multiplier on an item that declares none does nothing.',
    fields: [
      { key: 'damageMultiplier', label: 'Damage multiplier', kind: 'decimal' },
      { key: 'armorMultiplier', label: 'Armour multiplier', kind: 'decimal' },
      { key: 'healthMultiplier', label: 'Health multiplier', kind: 'decimal' },
      { key: 'focusMultiplier', label: 'Focus multiplier', kind: 'decimal' },
      { key: 'staminaMultiplier', label: 'Stamina multiplier', kind: 'decimal' },
    ],
  },
]

/** Every key the form owns, so anything else in the bag can be shown as carried through. */
export const OWNED_STAT_KEYS: string[] = STAT_GROUPS.flatMap((g) => g.fields.map((f) => f.key))

/**
 * Reads a stat for display, or undefined when the stored value is not a number.
 *
 * Undefined means "leave it alone": the field renders blank and the key is written back exactly
 * as found. Deliberately stricter than `Number()`, which turns `true` into 1 and `[]` into 0 —
 * inventing a value out of something that never was one.
 */
export function asNumber(raw: unknown): number | undefined {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : undefined
  if (typeof raw === 'string' && raw.trim() !== '') {
    const parsed = Number(raw)
    return Number.isFinite(parsed) ? parsed : undefined
  }
  return undefined
}

/** Kept for the existing multiplier tests; the general reader is `asNumber`. */
export const asMultiplier = asNumber
