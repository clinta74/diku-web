import { useState } from 'react'
import { paramPlaceholder, paramToDisplay, paramToStored, type EffectParamField } from './effects'

interface Props {
  param: EffectParamField
  /** The value as stored — pulses for a duration, verbatim otherwise. */
  stored: string
  onChange: (stored: string) => void
}

/**
 * One effect parameter, shown in the unit a builder thinks in and stored in the unit the engine
 * reads.
 *
 * <b>It keeps the raw text while you type, and that is not a nicety.</b> Without it a duration
 * field cannot accept a decimal at all: converting on every keystroke means typing "2." becomes
 * `Number("2.")` → 2 → 8 pulses → redisplayed as "2", so the dot disappears as fast as it is
 * typed and "2.5" is unreachable. The draft is held until focus leaves, then dropped so the field
 * re-syncs from what was actually stored.
 *
 * Caught by a test rather than in play, which is worth noting because the failure looks like a
 * stuck keyboard rather than like a conversion bug.
 */
export function ParamInput({ param, stored, onChange }: Props) {
  const [draft, setDraft] = useState<string | null>(null)

  return (
    <input
      value={draft ?? paramToDisplay(param, stored)}
      placeholder={paramPlaceholder(param)}
      inputMode={param.integer ? 'decimal' : 'text'}
      onChange={(e) => {
        setDraft(e.target.value)
        onChange(paramToStored(param, e.target.value))
      }}
      onBlur={() => setDraft(null)}
    />
  )
}
