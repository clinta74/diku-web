/**
 * Reading values out of an item template's schemaless `baseStats` bag.
 *
 * The editor renders five multiplier fields, but the bag also carries values it has no field for
 * — `damage: "1d6"` above all. It used to load the whole bag through `Number(v) || 0` and save
 * the result, so editing an item's *name* silently rewrote its damage dice to 0. Nothing failed;
 * the item just quietly stopped hitting.
 */

/**
 * Reads a multiplier for display, or undefined when the value is not one this form can represent.
 *
 * Undefined means "leave it alone": the field renders blank and the key is written back exactly
 * as it was found. Deliberately stricter than `Number()`, which turns `true` into 1 and `[]`
 * into 0 — inventing a multiplier out of a value that never was one.
 */
export function asMultiplier(raw: unknown): number | undefined {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : undefined
  if (typeof raw === 'string' && raw.trim() !== '') {
    const parsed = Number(raw)
    return Number.isFinite(parsed) ? parsed : undefined
  }
  return undefined
}
