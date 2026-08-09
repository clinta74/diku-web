import { useEffect, useRef, useState } from 'react'

interface Props {
  value: number | undefined
  onChange: (value: number | undefined) => void
  /** Whole numbers only — a flat armour value of 2.5 is not something the engine can use. */
  integer?: boolean
  disabled?: boolean
  'aria-label'?: string
}

/**
 * A numeric field where blank means *absent*, not zero.
 *
 * That distinction is the reason this exists rather than a plain `<input type="number">`. These
 * fields write into schemaless stat bags where a missing key and a stored 0 mean different
 * things: several stats fall back to a level-derived default when absent, so writing 0 for an
 * empty box would silently declare "this mob has no armour" instead of "use the default".
 *
 * It keeps its own text buffer while focused. Bound straight to the number, typing "1." parses
 * to 1 and React rewrites the field under the cursor before the "5" can be typed. On blur the
 * buffer resnaps to the committed value, so an abandoned partial entry cannot sit on screen
 * looking saved.
 */
export function NumberField({ value, onChange, integer = false, disabled, ...rest }: Props) {
  const [text, setText] = useState(() => (value === undefined ? '' : String(value)))
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current) setText(value === undefined ? '' : String(value))
  }, [value])

  function handle(raw: string) {
    // Filtered rather than validated, so the value can only ever be a number. A minus sign is
    // not accepted: none of the stats this drives has a meaningful negative.
    let out = ''
    let dot = false

    for (const c of raw) {
      if (c >= '0' && c <= '9') {
        out += c
      } else if (c === '.' && !dot && !integer) {
        out += c
        dot = true
      }
    }

    setText(out)

    if (out === '' || out === '.') {
      onChange(undefined)
      return
    }

    const parsed = Number(out)
    if (Number.isFinite(parsed)) onChange(parsed)
  }

  return (
    <input
      type="text"
      inputMode={integer ? 'numeric' : 'decimal'}
      className="number-input"
      value={text}
      placeholder="—"
      spellCheck={false}
      disabled={disabled}
      aria-label={rest['aria-label']}
      onFocus={() => {
        focused.current = true
      }}
      onBlur={() => {
        focused.current = false
        setText(value === undefined ? '' : String(value))
      }}
      onChange={(e) => handle(e.target.value)}
    />
  )
}
