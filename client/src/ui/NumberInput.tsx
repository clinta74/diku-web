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
