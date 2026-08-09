import { MULTIPLIER_KEYS, NEUTRAL_MULTIPLIERS, type Multipliers } from '../../net/builderApi'

/**
 * Reads a stored multiplier set, falling back to neutral per factor.
 *
 * A missing key renders as 1.0 rather than blank or zero: the set is stored as one jsonb object,
 * so a factor added in a later build is simply absent on older rows, and showing that as 0 would
 * make an untouched zone look like it awarded nothing.
 */
export function readMultipliers(raw: Partial<Multipliers> | undefined): Multipliers {
  const next = { ...NEUTRAL_MULTIPLIERS }
  if (!raw) return next
  for (const key of MULTIPLIER_KEYS) {
    const value = raw[key]
    if (typeof value === 'number' && Number.isFinite(value)) next[key] = value
  }
  return next
}

/** True when every factor is 1.0 — the scope is not scaled at all. */
export function isNeutral(multipliers: Multipliers): boolean {
  return MULTIPLIER_KEYS.every((key) => multipliers[key] === 1)
}
