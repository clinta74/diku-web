import { useEffect, useRef, useState } from 'react'

interface NumberInputProps {
  value: number
  onChange: (value: number) => void
  min?: number
  max?: number
  allowDecimal?: boolean
  allowNegative?: boolean
  placeholder?: string
  disabled?: boolean
  title?: string
  'aria-label'?: string
}

/**
 * A numeric field that behaves like a text field: no spinner arrows (they make typing a value
 * fiddly), and free typing with an input filter that silently rejects non-numeric characters.
 * The value is committed as you type when it parses, and normalised/clamped on blur so a partial
 * entry like "" or "-" settles to something valid.
 */
export function NumberInput({
  value,
  onChange,
  min,
  max,
  allowDecimal = false,
  allowNegative = false,
  placeholder,
  disabled,
  title,
  ...rest
}: NumberInputProps) {
  const [text, setText] = useState(() => String(value))
  const focused = useRef(false)

  // Reflect an external value change (e.g. a load), but never while the user is mid-edit.
  useEffect(() => {
    if (!focused.current && Number(text) !== value) {
      setText(Number.isFinite(value) ? String(value) : '')
    }
    // Only react to the incoming value.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  function sanitize(raw: string): string {
    let out = ''
    let dotSeen = false
    for (const c of raw) {
      if (c >= '0' && c <= '9') out += c
      else if (allowDecimal && c === '.' && !dotSeen) {
        out += c
        dotSeen = true
      } else if (allowNegative && c === '-' && out.length === 0) {
        out += c
      }
    }
    return out
  }

  function handleChange(raw: string) {
    const next = sanitize(raw)
    setText(next)
    const parsed = Number(next)
    // Skip incomplete entries ("", "-", ".", "-.") - blur will settle them.
    if (next === '' || next === '-' || next === '.' || next === '-.' || !Number.isFinite(parsed)) {
      return
    }
    onChange(parsed)
  }

  function handleBlur() {
    focused.current = false
    let parsed = Number(text)
    if (!Number.isFinite(parsed)) parsed = min ?? 0
    if (min !== undefined && parsed < min) parsed = min
    if (max !== undefined && parsed > max) parsed = max
    setText(String(parsed))
    if (parsed !== value) onChange(parsed)
  }

  return (
    <input
      type="text"
      inputMode={allowDecimal ? 'decimal' : 'numeric'}
      className="number-input"
      value={text}
      placeholder={placeholder}
      disabled={disabled}
      title={title}
      spellCheck={false}
      onFocus={() => {
        focused.current = true
      }}
      onChange={(e) => handleChange(e.target.value)}
      onBlur={handleBlur}
      aria-label={rest['aria-label']}
    />
  )
}

interface OptionalNumberInputProps {
  /** The stored value, or null when this is not set at all. */
  value: number | null
  /** Called with null when the field is cleared. */
  onChange: (value: number | null) => void
  /** Floor, applied on blur and never mid-keystroke. */
  min?: number
  allowDecimal?: boolean
  disabled?: boolean
  'aria-label'?: string
}

/**
 * A number field where <b>blank is a real value</b> rather than an incomplete entry.
 *
 * <see cref="NumberInput"/> settles a blank field to a number on blur, which is right everywhere it
 * is used and wrong wherever "not set" is a distinct state — a weapon with no attack delay is not a
 * weapon that swings instantly, and a mob attack with no damage multiplier is not one that deals
 * none.
 *
 * <b>The local text buffer is the whole point.</b> Two fields hand-rolled this and were fully
 * controlled off the parsed number, so every keystroke was parsed and rendered back: typing "1."
 * parsed as 1 and re-rendered as "1", erasing the point as it was typed. Neither field could accept
 * a decimal at all. Keeping what was typed, committing only when it parses, and settling on blur is
 * what fixes it — the same thing <see cref="NumberInput"/> does, which is why this lives beside it
 * rather than being written a third time.
 */
export function OptionalNumberInput({
  value,
  onChange,
  min,
  allowDecimal = false,
  disabled,
  ...rest
}: OptionalNumberInputProps) {
  const [text, setText] = useState(() => (value === null ? '' : String(value)))
  const focused = useRef(false)

  // Reflect a value that changed elsewhere, but never mid-edit, and never when the text already
  // means that number - so "1." survives a re-render that would otherwise rewrite it to "1".
  useEffect(() => {
    if (focused.current) return
    const typed = text.trim() === '' ? null : Number(text)
    if (typed === value) return
    setText(value === null ? '' : String(value))
    // Only react to the incoming value.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  function sanitize(raw: string): string {
    let out = ''
    let dotSeen = false
    for (const c of raw) {
      if (c >= '0' && c <= '9') out += c
      else if (allowDecimal && c === '.' && !dotSeen) {
        out += c
        dotSeen = true
      }
    }
    return out
  }

  function handleChange(raw: string) {
    const next = sanitize(raw)
    setText(next)

    if (next === '') {
      onChange(null)
      return
    }

    const parsed = Number(next)

    // "." alone is a point mid-word. Blur settles it.
    if (next === '.' || !Number.isFinite(parsed)) return

    // Deliberately unclamped: flooring per keystroke is what stops "0.5" existing on the way
    // to "0.75".
    onChange(parsed)
  }

  function handleBlur() {
    focused.current = false

    const parsed = Number(text)

    if (text.trim() === '' || !Number.isFinite(parsed)) {
      setText('')
      onChange(null)
      return
    }

    const settled = min !== undefined && parsed < min ? min : parsed
    setText(String(settled))
    onChange(settled)
  }

  return (
    <input
      type="text"
      inputMode={allowDecimal ? 'decimal' : 'numeric'}
      className="number-input"
      value={text}
      disabled={disabled}
      spellCheck={false}
      onFocus={() => {
        focused.current = true
      }}
      onChange={(e) => handleChange(e.target.value)}
      onBlur={handleBlur}
      aria-label={rest['aria-label']}
    />
  )
}
