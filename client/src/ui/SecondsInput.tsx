import { NumberInput } from './NumberInput'

/** One pulse in seconds (PLAN.md §2.3). The engine counts in pulses; people do not. */
export const PULSE_SECONDS = 0.25

interface Props {
  /** The stored value, in pulses. */
  pulses: number
  /** Called with pulses, so the caller never sees seconds. */
  onChange: (pulses: number) => void
  /** Floor, in pulses. Weapon and attack delays have one of 4. */
  minPulses?: number
  disabled?: boolean
  'aria-label'?: string
}

export function toSeconds(pulses: number): number {
  // Rounded to two places so 10 pulses reads 2.5 and floating point does not print 2.4999999.
  return Math.round(pulses * PULSE_SECONDS * 100) / 100
}

export function toPulses(seconds: number): number {
  return Math.max(0, Math.round(seconds / PULSE_SECONDS))
}

/**
 * A duration a builder reads and types in seconds, stored in pulses.
 *
 * <b>A pulse is an engine implementation detail and it had reached the builder.</b> Five fields
 * asked for pulses while two others beside them asked for seconds, so authoring one mob meant
 * converting between two units inside a single editor — and the hint that made the conversion
 * possible was on some of the pulse fields and missing from others (UX.md finding 6).
 *
 * <b>Converted here rather than in the API.</b> The stored shape stays pulses, because that is
 * what the loop counts and what every existing row, bundle and migration holds. This is the
 * boundary the emote fields already sat on the right side of — they have always taken seconds and
 * let `MobEmote.FromSeconds` convert.
 *
 * Quarter-second steps, because that is the real granularity: rounding to whole seconds would
 * silently retune anything authored on an odd pulse count.
 */
export function SecondsInput({ pulses, onChange, minPulses = 0, disabled, ...rest }: Props) {
  return (
    <NumberInput
      value={toSeconds(pulses)}
      onChange={(seconds) => onChange(Math.max(minPulses, toPulses(seconds)))}
      min={toSeconds(minPulses)}
      allowDecimal
      disabled={disabled}
      aria-label={rest['aria-label']}
    />
  )
}
