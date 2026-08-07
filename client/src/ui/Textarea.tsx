import { useEffect, useRef } from 'react'

interface TextareaProps {
  value: string
  onChange: (value: string) => void
  rows?: number
  placeholder?: string
  disabled?: boolean
  spellCheck?: boolean
}

/**
 * A multi-line text field that grows to fit its content, so a long room description is not
 * trapped behind a tiny scrollbar. Themed to match the other inputs.
 */
export function Textarea({ value, onChange, rows = 3, placeholder, disabled, spellCheck }: TextareaProps) {
  const ref = useRef<HTMLTextAreaElement>(null)

  // Resize to content on every value change, including external resets.
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = `${el.scrollHeight}px`
  }, [value])

  return (
    <textarea
      ref={ref}
      className="textarea"
      rows={rows}
      value={value}
      placeholder={placeholder}
      disabled={disabled}
      spellCheck={spellCheck}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}
