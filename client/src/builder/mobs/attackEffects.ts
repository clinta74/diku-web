/**
 * What a mob attack may carry, and the parameters each one reads.
 *
 * Transcribed from the executors in `DikuWeb.Domain/Abilities/Effects`. They read parameters by
 * name and **skip anything they do not recognise**, so a plausible-but-wrong key produces an
 * effect that runs with defaults and reports nothing — which is why these are fields rather than
 * a free-text bag.
 *
 * Only harmful effects are offered. A rider applies to whoever the attack *hit*, so a helpful one
 * would mean a mob buffing or mending the player it just struck. That is almost never intended,
 * and offering it is how it happens by accident. The server validates that the key is *known*
 * rather than that it is harmful, which leaves the deliberate oddity possible without making it
 * easy — an authoring guardrail, not a rule of the world.
 */

export interface EffectParamField {
  key: string
  label: string
  hint: string
  /** Shown as the placeholder: what the executor uses when the field is left blank. */
  fallback: string
  integer?: boolean
}

export interface AttackEffectOption {
  key: string
  label: string
  summary: string
  params: EffectParamField[]
}

const duration = (fallback: string, ceiling?: string): EffectParamField => ({
  key: 'durationPulses',
  label: 'Duration (pulses)',
  hint: ceiling ? `4 pulses ≈ 1s. Clamped to ${ceiling}.` : '4 pulses ≈ 1s.',
  fallback,
  integer: true,
})

const label = (fallback: string): EffectParamField => ({
  key: 'name',
  label: 'Shown as',
  hint: 'The word the player sees, in the status panel and in "You are …!".',
  fallback,
})

export const ATTACK_EFFECTS: AttackEffectOption[] = [
  {
    key: 'control.stun',
    label: 'Stun',
    summary: 'The target does not act: no swings, no casts, and anything mid-cast breaks.',
    params: [duration('8', '24 (6s)'), label('stunned')],
  },
  {
    key: 'control.root',
    label: 'Root',
    summary: 'The target can still fight but cannot flee or walk away.',
    params: [duration('16', '40 (10s)'), label('rooted')],
  },
  {
    key: 'damage.overtime',
    label: 'Wound over time',
    summary: 'Damage on an interval until it runs out. Only ticks during a fight.',
    params: [
      {
        key: 'tickDamage',
        label: 'Damage per tick',
        hint: 'Flat, and not scaled by the zone multipliers the way the swing is.',
        fallback: '4',
        integer: true,
      },
      {
        key: 'tickIntervalPulses',
        label: 'Tick every (pulses)',
        hint: 'Total damage is roughly damage × (duration ÷ interval).',
        fallback: '8',
        integer: true,
      },
      duration('48'),
      {
        key: 'maxStacks',
        label: 'Max stacks',
        hint: 'Above 1, repeated hits pile up rather than refreshing.',
        fallback: '1',
        integer: true,
      },
      label('bleeding'),
    ],
  },
  {
    key: 'debuff.weaken',
    label: 'Weaken',
    summary: 'The target deals less damage, or takes more of it.',
    params: [
      {
        key: 'outgoingMultiplier',
        label: 'Their damage ×',
        hint: 'Below 1.0 weakens. 0.75 means they hit for three quarters.',
        fallback: '1.0',
      },
      {
        key: 'incomingMultiplier',
        label: 'Damage they take ×',
        hint: 'Above 1.0 opens them up. 1.3 means everything lands for 30% more.',
        fallback: '1.0',
      },
      duration('240'),
      label('weakened'),
    ],
  },
]

export function effectOption(key: string | null | undefined): AttackEffectOption | null {
  return ATTACK_EFFECTS.find((e) => e.key === key) ?? null
}

/**
 * Drops parameters that are blank or belong to another effect.
 *
 * Switching the dropdown must not leave the previous effect's keys behind: the executors skip
 * what they do not recognise, so a stale `tickDamage` on a stun is invisible rather than wrong —
 * until someone reads the row and cannot tell what it was meant to do.
 */
export function pruneParams(
  effectKey: string | null,
  params: Record<string, string> | null | undefined,
): Record<string, string> | null {
  const option = effectOption(effectKey)
  if (!option || !params) {
    return null
  }

  const owned = new Set(option.params.map((p) => p.key))
  const kept = Object.entries(params).filter(
    ([key, value]) => owned.has(key) && value.trim() !== '',
  )

  return kept.length === 0 ? null : Object.fromEntries(kept)
}
