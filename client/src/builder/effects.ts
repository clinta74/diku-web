/**
 * Every ability effect and the parameters it reads — and, separately, the subset a mob attack may
 * carry as a rider.
 *
 * Transcribed from the executors in `Muwbta.Domain/Abilities/Effects`. They read parameters by
 * name and **skip anything they do not recognise**, so a plausible-but-wrong key produces an
 * effect that runs with defaults and reports nothing — which is why these are fields rather than
 * a free-text bag.
 *
 * `ABILITY_EFFECTS` offers **every** effect, because an ability points either way by design and the
 * executor's own `IsHarmful` is what decides which targets it gathers. The server agrees: it
 * validates that an ability's effect key is *registered*, and draws no ability/attack line at all.
 *
 * `ATTACK_EFFECTS` offers only the harmful ones. A rider applies to whoever the attack *hit*, so a
 * helpful one would mean a mob buffing or mending the player it just struck. That is almost never
 * intended, and offering it is how it happens by accident. The server validates that the key is
 * *known* rather than that it is harmful, which leaves the deliberate oddity possible without
 * making it easy — an authoring guardrail, not a rule of the world.
 *
 * <b>The two are one list with a flag, not two lists.</b> They were two, and five effects — root,
 * stun, wound-over-time, expose, weaken — ended up in the rider list only. So the ability editor
 * offered six options while shipped abilities used eleven, and `<select value="control.root">`
 * matching no `<option>` made the browser paint the first one: Hamstring, a root, read as "Damage".
 * The fields below it were right the whole time, because they come from a lookup across both lists,
 * which is what made it look like a subtype nobody could reach rather than a missing option.
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
  /**
   * Stored in pulses, shown in seconds. A pulse is an engine detail (PLAN.md §2.3) and asking a
   * builder to convert while authoring is UX.md finding 6 - the editors that take seconds sit
   * beside the ones that took pulses.
   */
  pulses?: boolean
}

export interface EffectOption {
  key: string
  label: string
  summary: string
  params: EffectParamField[]
  /**
   * Whether a mob attack may carry this as a rider — the harmful ones only, per the header.
   * Absent means abilities-only, which is the safe default: an effect added without thinking about
   * riders stays out of the attack list rather than being offered to land on whoever was hit.
   */
  rider?: boolean
}

const duration = (fallback: string, ceiling?: string): EffectParamField => ({
  key: 'durationPulses',
  label: 'Lasts (seconds)',
  hint: ceiling ? `Clamped to ${ceiling}.` : 'How long it stays on the target.',
  fallback,
  integer: true,
  pulses: true,
})

const label = (fallback: string): EffectParamField => ({
  key: 'name',
  label: 'Shown as',
  hint: 'The word the player sees, in the status panel and in "You are …!".',
  fallback,
})

/**
 * Every effect, in the order the dropdowns read.
 *
 * The two damage effects sit together deliberately: they are separate executors rather than one
 * with a subtype, and a builder who wants "damage" has to choose between a hit and a bleed. Putting
 * them apart is what made a wound-over-time ability look like a plain one.
 */
const EFFECTS: EffectOption[] = [
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
    key: 'damage.overtime',
    label: 'Wound over time',
    summary: 'Damage on an interval until it runs out. Only ticks during a fight.',
    rider: true,
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
        label: 'Ticks every (seconds)',
        hint: 'Total damage is roughly damage × (duration ÷ interval).',
        fallback: '8',
        integer: true,
        pulses: true,
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
    key: 'heal.restore',
    label: 'Heal',
    summary: 'Restores health. Helpful abilities land on the caster when no target is named.',
    params: [
      {
        key: 'baseHeal',
        label: 'Heals',
        hint: 'A flat amount of health. Ignored when a percentage is set.',
        fallback: '20',
        integer: true,
      },
      {
        key: 'healPercent',
        label: 'Or heals (% of max)',
        hint:
          "A share of the TARGET's maximum health, in whole points. Wins over the flat amount. " +
          'For a heal whose idea is proportional - a second wind is worth getting back on your ' +
          'feet, which is a different number at level 13 and at level 50.',
        fallback: '',
        integer: true,
      },
    ],
  },
  {
    key: 'resource.restore',
    label: 'Restore resource',
    summary:
      'Puts focus, stamina or health back. Pair it with a cost in the OTHER resource and the ' +
      'ability is a conversion - the exchange rate is the cost and this amount, side by side.',
    params: [
      {
        key: 'resource',
        label: 'Which bar',
        hint: 'Focus, Stamina or Health.',
        fallback: 'Focus',
      },
      {
        key: 'percent',
        label: 'Restores (% of max)',
        hint: 'A share of that bar, in whole points. Wins over the flat amount.',
        fallback: '25',
        integer: true,
      },
      {
        key: 'amount',
        label: 'Or restores (flat)',
        hint: 'A flat amount. Ignored when a percentage is set.',
        fallback: '',
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
        key: 'mitigation',
        label: 'Absorbs (%)',
        hint: 'Percentage points off each blow that does land. 6 means six percent.',
        fallback: '6',
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
    key: 'debuff.expose',
    label: 'Expose',
    summary: "Strips a target's guard: easier to hit, and blows land harder.",
    rider: true,
    params: [
      {
        key: 'defenseRating',
        label: 'Easier to hit by',
        hint: 'A positive amount, meaning how much guard to take away.',
        fallback: '4',
        integer: true,
      },
      {
        key: 'mitigation',
        label: 'Armour stripped (%)',
        hint: 'Percentage points taken off what their armour absorbs. A positive number.',
        fallback: '6',
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
    rider: true,
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
  {
    key: 'control.stun',
    label: 'Stun',
    summary: 'The target does not act: no swings, no casts, and anything mid-cast breaks.',
    rider: true,
    params: [duration('8', '24 (6s)'), label('stunned')],
  },
  {
    key: 'control.root',
    label: 'Root',
    summary: 'The target can still fight but cannot flee or walk away.',
    rider: true,
    // "held fast", not "rooted": this is the placeholder, so it has to be the word the engine
    // actually falls back to. RootEffect says "held fast" and it is the better sentence in
    // "You are …!" — so the placeholder moved rather than the engine.
    params: [duration('16', '40 (10s)'), label('held fast')],
  },
]

/**
 * Every effect an ability may use — all of them, because the server draws no ability/attack line.
 * <c>ABILITY_EFFECTS[0]</c> is the default a freshly added effect takes, so Damage stays first.
 */
export const ABILITY_EFFECTS: EffectOption[] = EFFECTS

/**
 * The harmful subset, for mob attack riders. **Filtered from the list above rather than written
 * out**, which is what the old comment claimed and the old code did not do — and an effect existing
 * for one surface and quietly missing from the other is exactly what that cost.
 */
export const ATTACK_EFFECTS: EffectOption[] = EFFECTS.filter((e) => e.rider)

/** Every effect either surface can offer, keyed for lookup. */
export const ALL_EFFECTS: EffectOption[] = EFFECTS

/** One pulse in seconds (PLAN.md §2.3). */
const PULSE_SECONDS = 0.25

/**
 * The stored value as the builder should see it — seconds for a duration, verbatim otherwise.
 *
 * Kept beside the schema rather than in the two components that render params, so a field marked
 * `pulses` cannot be converted on one screen and shown raw on the other.
 */
export function paramToDisplay(param: EffectParamField, stored: string): string {
  if (!param.pulses || stored === '') return stored

  const pulses = Number(stored)
  if (!Number.isFinite(pulses)) return stored

  return String(Math.round(pulses * PULSE_SECONDS * 100) / 100)
}

/** What was typed, back in the unit the engine stores. */
export function paramToStored(param: EffectParamField, typed: string): string {
  if (!param.pulses || typed.trim() === '') return typed

  const seconds = Number(typed)
  if (!Number.isFinite(seconds)) return typed

  return String(Math.max(0, Math.round(seconds / PULSE_SECONDS)))
}

/** The placeholder, which is stored in pulses like everything else and shown in seconds. */
export function paramPlaceholder(param: EffectParamField): string {
  return paramToDisplay(param, param.fallback)
}

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
