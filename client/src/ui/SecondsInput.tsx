import { NumberInput, OptionalNumberInput } from './NumberInput'

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

interface OptionalProps {
  /** The stored value in pulses, or null when this duration is not set at all. */
  pulses: number | null
  /** Called with pulses, or null when the field is cleared. */
  onChange: (pulses: number | null) => void
  /** Floor, in pulses. Applied on blur, never mid-keystroke. */
  minPulses?: number
  disabled?: boolean
  'aria-label'?: string
}

/**
 * A duration in seconds where blank means the duration is not set at all.
 *
 * <b>The field this replaced could not accept a decimal.</b> It was fully controlled off the stored
 * pulse count, so every keystroke went through `toPulses` and back: typing "1." parsed as 1, stored
 * 4 pulses, and re-rendered as "1", erasing the point as it was typed. A weapon speed of 1.5 seconds
 * was unreachable, and on a quarter-second grid so was every value that is not a whole number of
 * seconds — three options in four.
 *
 * The buffer that fixes it lives in `OptionalNumberInput`, because a second field had the same bug.
 * This is only the unit conversion: seconds to the builder, pulses to the engine.
 */
export function OptionalSecondsInput({
  pulses,
  onChange,
  minPulses = 0,
  disabled,
  ...rest
}: OptionalProps) {
  return (
    <OptionalNumberInput
      value={pulses === null ? null : toSeconds(pulses)}
      min={toSeconds(minPulses)}
      allowDecimal
      disabled={disabled}
      aria-label={rest['aria-label']}
      onChange={(seconds) => onChange(seconds === null ? null : toPulses(seconds))}
    />
  )
}
