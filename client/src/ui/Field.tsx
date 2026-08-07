import type { ReactNode } from 'react'

interface FieldProps {
  label: ReactNode
  /** Quiet helper text under the control. */
  hint?: ReactNode
  /** Inline validation message, shown in the error colour when present. */
  error?: string | null
  children: ReactNode
}

/**
 * A labelled control: label above, control, then an optional hint and inline error. Collapses
 * the ~40 hand-written `<label>Foo<input/></label>` blocks scattered through the editors into
 * one consistent shape.
 */
export function Field({ label, hint, error, children }: FieldProps) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
      {error && <span className="field-error">{error}</span>}
    </label>
  )
}
