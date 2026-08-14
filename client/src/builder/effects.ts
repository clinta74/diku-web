/**
 * Every ability effect and the parameters it reads — and, separately, the subset a mob attack may
 * carry as a rider.
 *
 * Transcribed from the executors in `DikuWeb.Domain/Abilities/Effects`. They read parameters by
 * name and **skip anything they do not recognise**, so a plausible-but-wrong key produces an
 * effect that runs with defaults and reports nothing — which is why these are fields rather than
 * a free-text bag.
 *
 * `ATTACK_EFFECTS` offers only the harmful ones. A rider applies to whoever the attack *hit*, so a
 * helpful one would mean a mob buffing or mending the player it just struck. That is almost never
 * intended, and offering it is how it happens by accident. The server validates that the key is
 * *known* rather than that it is harmful, which leaves the deliberate oddity possible without
 * making it easy — an authoring guardrail, not a rule of the world.
 *
 * `ABILITY_EFFECTS` offers all eight, because an ability points either way by design and the
 * executor's own `IsHarmful` is what decides which targets it gathers.
 *
 * One file rather than two because the parameter names are the same contract either way, and a
 * second transcription of the executors is a second thing to forget when one changes.
 */

export interface EffectParamField {
  key: string
  label: string
  hint: string
  /** Shown as the placeholder: what the executor uses when the field is left blank. */
  fallback: string
  integer?: boolean
}

export interface EffectOption {
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

/** Every effect an ability may use. Ordered as the catalogue reads: damage, heal, then riders. */
export const ABILITY_EFFECTS: EffectOption[] = [
  {
    key: 'damage.physical',
    label: 'Damage',
    summary: 'A single hit, scaled off the caster rather than a flat number.',
    params: [
      {
        key: 'scalingFactor',
        label: 'Damage ×',
        hint: 'Multiplies what the caster would otherwise hit for. 1.2 is a light opener.',
        fallback: '1.0',
      },
      {
        key: 'minDamage',
        label: 'At least',
        hint: 'A floor, so the ability is never worse than a poke.',
        fallback: '1',
        integer: true,
      },
    ],
  },
  {
    key: 'heal.restore',
    label: 'Heal',
    summary: 'Restores health. Helpful abilities land on the caster when no target is named.',
    params: [
      {
        key: 'baseHeal',
        label: 'Heals',
        hint: 'A flat amount of health.',
        fallback: '20',
        integer: true,
      },
    ],
  },
  {
    key: 'buff.damage-up',
    label: 'Damage buff',
    summary: 'The target deals more damage for a while.',
    params: [
      {
        key: 'outgoingMultiplier',
        label: 'Their damage ×',
        hint: 'Above 1.0 helps. 1.25 is a quarter more. Below 1.0 is refused — that is a weaken.',
        fallback: '1.25',
      },
      duration('80'),
      {
        key: 'maxStacks',
        label: 'Max stacks',
        hint: 'Leave at 1 unless repeated casts are meant to pile up.',
        fallback: '1',
        integer: true,
      },
      label('emboldened'),
    ],
  },
  {
    key: 'buff.defense',
    label: 'Guard',
    summary: 'Harder to hit, and blows that land cost less. Positive amounts only.',
    params: [
      {
        key: 'defenseRating',
        label: 'Harder to hit by',
        hint: 'Added to what an attack roll must beat. Changes how often a blow lands.',
        fallback: '4',
        integer: true,
      },
      {
        key: 'armorFlat',
        label: 'Absorbs',
        hint: 'Taken off each blow that does land. Changes what one costs.',
        fallback: '3',
        integer: true,
      },
      duration('80'),
      label('guarded'),
    ],
  },
  {
    key: 'buff.max-health',
    label: 'Maximum health',
    summary:
      'Raises the ceiling and hands over that much health with it — once, on the first cast. A refresh adds no more, so it is a buff rather than a heal on a short cooldown.',
    params: [
      {
        key: 'maxHealth',
        label: 'Extra health',
        hint: 'Added to the maximum, and granted. Taken back when it expires, clamping current health under the new ceiling.',
        fallback: '40',
        integer: true,
      },
      duration('96'),
      label('steeled'),
    ],
  },
  {
    key: 'control.taunt',
    label: 'Taunt',
    summary: 'Puts the caster at the top of the target\'s hate list — a lead, not a lock.',
    params: [
      {
        key: 'leadFraction',
        label: 'Lead',
        hint: "A fraction of the target's max health, so it means the same on a rat and a dragon.",
        fallback: '0.30',
      },
    ],
  },
]

/**
 * The harmful subset, for mob attack riders. Built from the list above rather than written out,
 * so an effect cannot exist for abilities and quietly go missing here.
 */
export const ATTACK_EFFECTS: EffectOption[] = [
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
    key: 'debuff.expose',
    label: 'Expose',
    summary: "Strips a target's guard: easier to hit, and blows land harder.",
    params: [
      {
        key: 'defenseRating',
        label: 'Easier to hit by',
        hint: 'A positive amount, meaning how much guard to take away.',
        fallback: '4',
        integer: true,
      },
      {
        key: 'armorFlat',
        label: 'Armour stripped',
        hint: 'A positive amount, taken off what their armour absorbs.',
        fallback: '3',
        integer: true,
      },
      duration('80'),
      label('exposed'),
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

/** Every effect either surface can offer, keyed for lookup. */
export const ALL_EFFECTS: EffectOption[] = [...ABILITY_EFFECTS, ...ATTACK_EFFECTS]

export function effectOption(key: string | null | undefined): EffectOption | null {
  return ALL_EFFECTS.find((e) => e.key === key) ?? null
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
